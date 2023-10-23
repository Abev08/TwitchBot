using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace AbevBot
{
  public static class Chat
  {
    private const int MESSAGESENTMAXLEN = 460; // 500 characters Twitch limit, -40 characters as a buffer
    private const string RESPONSEMESSAGESPATH = "Resources/ResponseMessages.csv";

    /// <summary> Chat bot started. </summary>
    public static bool Started { get; private set; }
    private static Socket ChatSocket = null;
    private static readonly TimeSpan CooldownBetweenTheSameMessage = new(0, 0, 10);
    public static readonly Dictionary<string, (string, DateTime)> ResponseMessages = new();
    private static Thread ChatReceiveThread;
    private static Thread ChatSendThread;
    public static List<string> PeriodicMessages { get; set; } = new();
    private static int PeriodicMessageIndex = -1;
    private static DateTime LastPeriodicMessage = DateTime.Now;
    public static TimeSpan PeriodicMessageInterval = new(0, 10, 0);
    private static readonly List<string> MessageQueue = new();
    private static readonly HttpClient Client = new();
    private static DateTime RespMsgFileTimestamp;
    private static readonly List<SkipSongChatter> SkipSongChatters = new();

    public static void Start()
    {
      if (Started) return;
      Started = true;

      MainWindow.ConsoleWarning(">> Starting chat bot.");
      LoadResponseMessages();
      if (!ResponseMessages.TryAdd("!commands", (string.Empty, DateTime.MinValue)))
      {
        MainWindow.ConsoleWarning(">> Couldn't add !commands response. Maybe something already declared it?");
      }
      if (!ResponseMessages.TryAdd("!lang", ("Please speak English in the chat, thank you ❤", DateTime.MinValue)))
      {
        MainWindow.ConsoleWarning(">> Couldn't add !lang response. Maybe something already declared it?");
      }

      ChatReceiveThread = new Thread(Update)
      {
        Name = "Chat receive thread",
        IsBackground = true
      };
      ChatReceiveThread.Start();

      ChatSendThread = new Thread(UpdateSend)
      {
        Name = "Chat send thread",
        IsBackground = true
      };
      ChatSendThread.Start();
    }

    private static void Update()
    {
      byte[] receiveBuffer = new byte[16384]; // Max IRC message is 4096 bytes? let's allocate 4 times that, 2 times max message length wasn't enaugh for really fast chats
      int zeroBytesReceivedCounter = 0, currentIndex, nextIndex, bytesReceived, messageStartOffset = 0, temp2;
      string userBadge, userName, customRewardID, temp;
      long userID;
      List<string> messages = new();
      /// <summary> message[0] - header, message[1] - body </summary>
      string[] message = new string[2];
      (string, DateTime) dictionaryResponse;
      Chatter chatter;
      ManualResetEvent receiveEvent = new(false);
      while (true)
      {
        while (ChatSocket?.Connected == true)
        {
          receiveEvent.Reset();

          ChatSocket.BeginReceive(receiveBuffer, messageStartOffset, receiveBuffer.Length - messageStartOffset, SocketFlags.None, new AsyncCallback((IAsyncResult ar) =>
          {
            try
            {
              if (ChatSocket.Connected) bytesReceived = ChatSocket.EndReceive(ar);
              else bytesReceived = -1;
            }
            catch (Exception ex)
            {
              MainWindow.ConsoleWarning($">> Chat bot error: {ex.Message}");
              bytesReceived = -1;
            }

            if (bytesReceived > 0)
            {
              messages.Clear();
              messages.AddRange(Encoding.UTF8.GetString(receiveBuffer, 0, bytesReceived + messageStartOffset).Split("\r\n", StringSplitOptions.RemoveEmptyEntries));

              if (messageStartOffset > 0)
              {
                // For some reason if the missing part of previous message is "\r\n" it's not send on next message
                // Cut out the new message appended to previous one and insert it as new one
                if (receiveBuffer[messageStartOffset] == '@')
                {
                  messages.Insert(1, messages[0].Substring(messageStartOffset));
                  messages[0] = messages[0].Substring(0, messageStartOffset);
                }
              }

              for (int i = 0; i < messages.Count; i++)
              {
                // message[0] - header, message[1] - body
                currentIndex = messages[i].IndexOf($"#{Config.Data[Config.Keys.ChannelName]}");
                if (currentIndex < 0)
                {
                  message[0] = messages[i].Trim();
                  message[1] = ":"; // Add fake message
                }
                else
                {
                  message[0] = messages[i].Substring(0, currentIndex).Trim();
                  message[1] = messages[i].Substring(currentIndex + Config.Data[Config.Keys.ChannelName].Length + 1).Trim(); // +1 because of '#' symbol in channel name
                }

                // Check if received message is incomplete (just for last received message)
                if ((i == messages.Count - 1) && (receiveBuffer[bytesReceived + messageStartOffset - 1] != (byte)'\n') && (receiveBuffer[bytesReceived + messageStartOffset - 2] != (byte)'\r'))
                {
                  // Move the message to beginning of receiveBuffer
                  if (messageStartOffset == 0) Array.Clear(receiveBuffer);

                  string s = string.Join($"#{Config.Data[Config.Keys.ChannelName]}", message);
                  for (int j = 0; j < s.Length; j++) receiveBuffer[j + messageStartOffset] = (byte)s[j];
                  messageStartOffset += s.Length;
                  continue;
                }
                else messageStartOffset = 0;

                // Ping request, let's play PING - PONG with the server :D
                if (message[0].StartsWith("PING"))
                {
                  string response = message[0].Replace("PING", "PONG");
                  ChatSocket.Send(Encoding.UTF8.GetBytes(response + "\r\n"));
                  continue;
                }

                // Standard message without extra tags
                if (message[0].StartsWith(':') && message[0].EndsWith("PRIVMSG"))
                {
                  MainWindow.ConsoleWriteLine(string.Format(
                    "{0, 20}{1, 2}{2, -0}",
                    message[0].Substring(1, message[0].IndexOf('!') - 1) // Username
                    , ": ",
                    message[1][1..] // message
                  ));
                }
                // Standard message with extra tags
                else if (message[0].StartsWith("@") && message[0].EndsWith("PRIVMSG"))
                {
                  // Check if message was from custom reward
                  currentIndex = message[0].IndexOf("custom-reward-id=");
                  if (currentIndex > 0)
                  {
                    currentIndex += 17; // "custom-reward-id=".Length
                    customRewardID = message[0].Substring(currentIndex, message[0].IndexOf(';', currentIndex) - currentIndex);
                    if (customRewardID.Length > 0) // For some reason sometimes the message contains empty "custom-reward-id"
                    {
                      currentIndex = message[0].IndexOf("display-name=") + 13; // 13 == "display-name=".Length
                      if (currentIndex >= 0)
                      {
                        nextIndex = message[0].IndexOf(';', currentIndex);
                        userName = message[0].Substring(currentIndex, nextIndex - currentIndex);
                      }
                      else { userName = "?"; }
                      MainWindow.ConsoleWriteLine(string.Concat(
                        "> ",
                        userName,
                        " redeemed custom reward with ID: ",
                        customRewardID,
                        ". ",
                        message[1][1..]
                      ));
                      continue;
                    }
                  }

                  // Check if message had some bits cheered
                  currentIndex = message[0].IndexOf("bits=");
                  if (currentIndex > 0)
                  {
                    currentIndex += 5; // "bits=".Length
                    MainWindow.ConsoleWriteLine(string.Concat(
                      "> Cheered with ",
                      message[0].Substring(currentIndex, message[0].IndexOf(';', currentIndex) - currentIndex),
                      "bits. ",
                      message[1][1..]
                    ));
                    continue;
                  }

                  // Read chatter badges
                  currentIndex = message[0].IndexOf("badges=") + 7; // 7 == "badges=".Length
                  userBadge = message[0].Substring(currentIndex, (nextIndex = message[0].IndexOf(';', currentIndex - 1)) - currentIndex);
                  if (userBadge.Contains("broadcaster")) userBadge = "STR";
                  else if (userBadge.Contains("moderator")) userBadge = "MOD";
                  else if (userBadge.Contains("subscriber")) userBadge = "SUB";
                  else if (userBadge.Contains("vip")) userBadge = "VIP";
                  else userBadge = string.Empty;
                  currentIndex = nextIndex;

                  // Read chatter name
                  currentIndex = message[0].LastIndexOf("user-id=") + 8; // 8 == "user-id=".Length
                  nextIndex = message[0].IndexOf(';', currentIndex);
                  userID = long.Parse(message[0].Substring(currentIndex, nextIndex - currentIndex));
                  currentIndex = message[0].IndexOf("display-name=") + 13; // 13 == "display-name=".Length
                  nextIndex = message[0].IndexOf(';', currentIndex);
                  userName = message[0].Substring(currentIndex, nextIndex - currentIndex);
                  currentIndex = nextIndex;

                  MainWindow.ConsoleWriteLine(string.Format(
                    "{0, -4}{1, 22}{2, 2}{3, -0}",
                    userBadge,
                    userName,
                    ": ",
                    message[1][1..]
                  ));

                  chatter = Chatter.GetChatterByID(userID, userName);
                  chatter.LastChatted = DateTime.Now;
                  if (Notifications.WelcomeMessagesEnabled && chatter?.WelcomeMessage.Length > 0 && chatter.LastWelcomeMessage.Date != DateTime.Now.Date)
                  {
                    MainWindow.ConsoleWarning($">> Creating {chatter.Name} welcome message TTS.");
                    chatter.SetLastWelcomeMessageToNow();
                    Notifications.CreateTTSNotification(chatter.WelcomeMessage);
                  }

                  if (message[1][1..].StartsWith("!tts")) // Check if the message starts with !tts key
                  {
                    if (message[1].Length <= 5) { MainWindow.ConsoleWarning(">> !tts command without a message Susge"); } // No message to read, do nothing
                    else if (Notifications.ChatTTSEnabled || Chatter.AlwaysReadTTSFromThem.Contains(userName)) { Notifications.CreateTTSNotification(message[1][6..]); } // 6.. - without ":!tts "
                    else { AddMessageToQueue($"@{userName} TTS disabled peepoSad"); }
                  }
                  else if (message[1][1..].StartsWith("!gamba")) // Check if the message starts with !gamba key
                  {
                    MinigameGamba.NewGamba(userID, userName, message[1][7..]); // 7.. - without ":!gamba"
                  }
                  else if (message[1][1..].StartsWith("!fight")) // Check if the message starts with !fight key
                  {
                    MinigameFight.NewFight(userID, userName, message[1][7..]); // 7.. - without ":!fight"
                  }
                  else if (message[1][1..].StartsWith("!rude")) // Check if the message starts with !rude key
                  {
                    MinigameRude.AddRudePoint(userName, message[1][6..]); // 6.. - without ":!rude"
                  }
                  else if (message[1][1..].StartsWith("!point")) // Check if the message starts with !point key
                  {
                    if (userBadge.Equals("STR") || message[1].Length == 7) MinigameBackseat.AddBackseatPoint(message[1][7..], 1); // 7.. - without ":!point"
                    else AddMessageToQueue($"@{userName} That's for the streamer, you shouldn't be using it Madge");
                  }
                  else if (message[1][1..].StartsWith("!unpoint")) // Check if the message starts with !unpoint key
                  {
                    if (userBadge.Equals("STR")) MinigameBackseat.AddBackseatPoint(message[1][9..], -1); // 9.. - without ":!unpoint"
                  }
                  else if (message[1][1..].StartsWith("!vanish")) // Check if the message starts with !vanish key
                  {
                    if (message[1].Length == 8)
                    {
                      // Vanish the command user
                      if (!userBadge.Equals("STR")) BanChatter("!vanish command", userID, userName);
                    }
                    else
                    {
                      // Vanish other user, only available for streamer and moderators
                      if (userBadge.Equals("STR") || userBadge.Equals("MOD"))
                      {
                        BanChatter("!vanish command", -1, message[1][8..]); // 8.. - without ":!vanish"
                      }
                    }
                  }
                  else if (message[1][1..].StartsWith("!hug")) // Check if the message starts with !hug key
                  {
                    Hug(userName, message[1][5..]); // 5.. - without ":!hug"
                  }
                  else if (message[1][1..].StartsWith("!commands")) // Check if the message starts with !commands key
                  {
                    // Check the timeout
                    if (DateTime.Now - ResponseMessages["!commands"].Item2 >= CooldownBetweenTheSameMessage)
                    {
                      SendCommandsResponse(userName);
                      ResponseMessages["!commands"] = (string.Empty, DateTime.Now);
                    }
                    else { MainWindow.ConsoleWarning(">> Not sending response for \"!commands\" key. Cooldown active."); }
                  }
                  else if (message[1][1..].StartsWith("!welcomemessage")) // Check if the message starts with !welcomemessage key
                  {
                    temp = message[1][16..].Trim();
                    if (string.IsNullOrWhiteSpace(temp))
                    {
                      // Print out current welcome message
                      if (chatter.WelcomeMessage?.Length > 0) AddMessageToQueue($"@{chatter.Name} current welcome message: {chatter.WelcomeMessage}");
                      else AddMessageToQueue($"@{chatter.Name} your welcome message is empty peepoSad");
                    }
                    else
                    {
                      // Set new welcome message
                      chatter.SetWelcomeMessage(temp);
                      AddMessageToQueue($"@{chatter.Name} welcome message was updated peepoHappy");
                    }
                  }
                  else if (message[1][1..].StartsWith("!sounds")) // Check if the message starts with !sounds key
                  {
                    if (Notifications.AreSoundsAvailable())
                    {
                      temp = Notifications.GetSampleSoundsPaste();
                      AddMessageToQueue($"@{chatter.Name} {temp}");
                    }
                    else { AddMessageToQueue($"@{chatter.Name} there are no sounds to use peepoSad"); }
                  }
                  else if (message[1][1..].StartsWith("!previoussong")) // Check if the message starts with !previoussong key
                  {
                    if (Spotify.Working) { AddMessageToQueue($"@{chatter.Name} {Spotify.GetRecentlyPlayingTracks()}"); }
                    else { AddMessageToQueue($"@{chatter.Name} the Spotify connection is not working peepoSad"); }
                  }
                  else if (message[1][1..].StartsWith("!songrequest")) // Check if the message starts with !songrequest key
                  {
                    SongRequest(chatter, message[1][13..]);  // 13.. - without ":!songrequest"
                  }
                  else if (message[1][1..].StartsWith("!sr")) // Check if the message starts with !sr key
                  {
                    SongRequest(chatter, message[1][4..]); // 4.. - without ":!sr"
                  }
                  else if (message[1][1..].StartsWith("!skipsong")) // Check if the message starts with !skipsong key
                  {
                    SkipSong(chatter);
                  }
                  else if (message[1][1..].StartsWith("!songqueue")) // Check if the message starts with !songqueue key
                  {
                    if (Spotify.Working) { AddMessageToQueue($"@{chatter.Name} {Spotify.GetSongQueue()}"); }
                    else { AddMessageToQueue($"@{chatter.Name} the Spotify connection is not working peepoSad"); }
                  }
                  else if (message[1][1..].StartsWith("!song")) // Check if the message starts with !song key
                  {
                    if (Spotify.Working) { AddMessageToQueue($"@{chatter.Name} {Spotify.GetCurrentlyPlayingTrack()}"); }
                    else { AddMessageToQueue($"@{chatter.Name} the Spotify connection is not working peepoSad"); }
                  }
                  else if (ResponseMessages.Count > 0) // Check if message starts with key to get automatic response
                  {
                    currentIndex = message[1].IndexOf(' ', 1);
                    if (currentIndex < 0) currentIndex = message[1].Length - 1;
                    temp = message[1].Substring(1, currentIndex).Trim();
                    if (ResponseMessages.TryGetValue(temp, out dictionaryResponse))
                    {
                      // "!lang" response - "Please speak xxx in the chat...", only usable by the mods or the streamer
                      if (temp.Equals("!lang"))
                      {
                        if (userBadge.Equals("MOD") || userBadge.Equals("STR"))
                        {
                          if (message[1].Trim().Length - 1 > currentIndex)
                          {
                            temp = message[1][currentIndex..].Trim();
                            if (temp.StartsWith('@')) temp = temp[1..];
                            AddMessageToQueue($"@{temp} {dictionaryResponse.Item1}");
                          }
                          else { AddMessageToQueue($"@{userName} {dictionaryResponse.Item1}"); }
                        }
                        continue;
                      }
                      // Check if the same message was send not long ago
                      if (DateTime.Now - dictionaryResponse.Item2 >= CooldownBetweenTheSameMessage)
                      {
                        AddMessageToQueue($"@{userName} {dictionaryResponse.Item1}");
                        ResponseMessages[temp] = (ResponseMessages[temp].Item1, DateTime.Now);
                      }
                      else { MainWindow.ConsoleWarning($">> Not sending response for \"{temp}\" key. Cooldown active."); }
                    }
                  }
                }
                // Automated bot response
                else if (message[0].EndsWith("USERSTATE"))
                {
                  currentIndex = message[0].IndexOf("display-name=") + 13; // 13 == "display-name=".Length
                  nextIndex = message[0].IndexOf(';', currentIndex);
                  userName = message[0].Substring(currentIndex, nextIndex - currentIndex);

                  MainWindow.ConsoleWriteLine(string.Format(
                    "{0, -4}{1, 20}{2, 2}{3, -0}",
                    "BOT",
                    userName,
                    ": ",
                    "Automated bot response (message is not available)"));
                }
                // Notification visible in chat - sub, announcement, etc.
                else if (message[0].StartsWith("@") && message[0].EndsWith("USERNOTICE"))
                {
                  currentIndex = message[0].IndexOf("display-name=") + 13; // 13 == "display-name=".Length
                  nextIndex = message[0].IndexOf(';', currentIndex);
                  userName = message[0].Substring(currentIndex, nextIndex - currentIndex);
                  currentIndex = nextIndex;
                  currentIndex = message[0].IndexOf("msg-id=", currentIndex) + 7; // 13 == "msg-id=".Length
                  switch (message[0].Substring(currentIndex, message[0].IndexOf(';', currentIndex) - currentIndex))
                  {
                    case "sub":
                    case "resub":
                      currentIndex = message[0].IndexOf("system-msg=");
                      if (currentIndex > 0)
                      {
                        currentIndex += 11; // "system-msg=".Length
                        nextIndex = message[0].IndexOf(";", currentIndex);
                        MainWindow.ConsoleWriteLine(string.Concat(
                          "> ",
                          message[0].Substring(currentIndex, nextIndex - currentIndex).Replace("\\s", " "),
                          " ",
                          message[1].Length > 2 ? message[1][1..] : ""
                        ));
                      }
                      else
                      {
                        // If the message didn't contain system message part, try the old parser
                        MainWindow.ConsoleWriteLine(string.Concat(
                          "> ",
                          userName,
                          message[0].Contains("msg-param-was-gifted=true") ? " got gifted sub for " : " subscribed for ",
                          message[0].Substring(currentIndex = message[0].IndexOf("msg-param-cumulative-months=", currentIndex) + 28, message[0].IndexOf(';', currentIndex) - currentIndex),
                          " months. ",
                          message[1].Length > 2 ? message[1][1..] : ""
                        ));
                      }
                      break;
                    case "subgift":
                      currentIndex = message[0].IndexOf("msg-param-recipient-display-name=") + 33; // 33 == "msg-param-recipient-display-name=".Length
                      MainWindow.ConsoleWriteLine(string.Concat(
                        "> ",
                        userName,
                        " gifted a sub for ",
                        message[0].Substring(currentIndex, message[0].IndexOf(';', currentIndex) - currentIndex),
                        ". ",
                        message[1].Length > 2 ? message[1][1..] : ""
                      ));
                      break;
                    case "submysterygift":
                      currentIndex = message[0].IndexOf("msg-param-mass-gift-count=") + 26; // 26 == "msg-param-mass-gift-count=".Length
                      MainWindow.ConsoleWriteLine(string.Concat(
                        "> ",
                        userName,
                        " gifting ",
                        message[0].Substring(currentIndex, message[0].IndexOf(";", currentIndex) - currentIndex),
                        " subs for random viewers. ",
                        message[1].Length > 2 ? message[1][1..] : ""
                      ));
                      break;
                    case "primepaidupgrade":
                      MainWindow.ConsoleWriteLine(string.Concat(
                        "> ",
                        userName,
                        " converted prime sub to standard sub.",
                        message[1].Length > 2 ? message[1][1..] : ""
                      ));
                      break;
                    case "announcement":
                      MainWindow.ConsoleWriteLine(string.Concat(
                        "> ",
                        userName,
                        " announced that: ",
                        message[1].Length > 2 ? message[1][1..] : "no message :("
                      ));
                      break;
                    case "raid":
                      currentIndex = message[0].IndexOf("msg-param-viewerCount=") + 22; // 22 == "msg-param-viewerCount=".Length
                      if (!int.TryParse(message[0].Substring(currentIndex, message[0].IndexOf(';', currentIndex) - currentIndex), out temp2)) temp2 = -1;
                      currentIndex = message[0].LastIndexOf("user-id=") + 8; // 8 == "user-id=".Length
                      temp = message[0].Substring(currentIndex, message[0].IndexOf(';', currentIndex) - currentIndex);
                      MainWindow.ConsoleWriteLine(string.Concat(
                        "> ",
                        userName,
                        " raided the channel with ",
                        temp2,
                        " viewers."
                      ));
                      Notifications.CreateRaidNotification(userName, temp, temp2);

                      // TODO: Delete after testing, temporary event messages logging
                      {
                        try
                        {
                          File.AppendAllText($"eventlog_{DateTime.Now:d}.txt", $"{DateTime.Now:G}\r\n");
                          File.AppendAllText($"eventlog_{DateTime.Now:d}.txt", message[0]);
                          File.AppendAllText($"eventlog_{DateTime.Now:d}.txt", "\r\n\r\n");
                        }
                        catch { }
                      }
                      break;
                    default:
                      if (message[1].Equals(":")) MainWindow.ConsoleWriteLine(message[0]);
                      else MainWindow.ConsoleWriteLine(string.Join(" : ", message));
                      break;
                  }
                }
                // Timeout
                else if (message[0].StartsWith("@ban-duration="))
                {
                  userName = message[1][1..];
                  MainWindow.ConsoleWriteLine($"> User {userName} got timed out for {message[0].Substring(14, message[0].IndexOf(';') - 14)} sec.");
                }
                // Timeout?
                else if (message[0].StartsWith("@") && message[0].Contains("CLEARMSG"))
                {
                  userName = message[0].Substring(7, message[0].IndexOf(';') - 7);
                  MainWindow.ConsoleWriteLine($"> User {userName} got banned.");
                }
                // Different timeout?
                else if (message[0].StartsWith("@") && message[0].Contains("CLEARCHAT"))
                {
                  MainWindow.ConsoleWriteLine($"> Chat got cleared.");
                }
                // Emote only activated
                else if (message[0].StartsWith("@emote-only=1"))
                {
                  MainWindow.ConsoleWriteLine("> Emote only activated.");
                }
                // Emote only deactivated
                else if (message[0].StartsWith("@emote-only=0"))
                {
                  MainWindow.ConsoleWriteLine("> Emote only deactivated.");
                }
                // Emote only
                else if (message[0].StartsWith("@msg-id=emote_only_"))
                {
                  // Switching emote only sends 2 messages - this is a duplicate? Do nothing?
                  // if (message[0].Substring(19, message[0].IndexOf(" :") - 19) == "on") MainWindow.ConsoleWriteLine("> Emote only activated");
                  // else MainWindow.ConsoleWriteLine("> Emote only deactivated");
                }
                // Other message type
                else
                {
                  if (message[1].Equals(":")) MainWindow.ConsoleWriteLine(message[0]);
                  else MainWindow.ConsoleWriteLine(string.Join(" : ", message));
                }
              }
              zeroBytesReceivedCounter = 0;
            }
            else
            {
              MainWindow.ConsoleWarning(">> Chat bot received 0 bytes.");
              zeroBytesReceivedCounter++;
              if (zeroBytesReceivedCounter >= 5)
              {
                MainWindow.ConsoleWarning(">> Chat bot closing connection.");
                ChatSocket?.Close(); // Close connection if 5 times in a row received 0 bytes
              }
            }

            receiveEvent.Set();
          }), null);

          receiveEvent.WaitOne();
        }

        if (ChatSocket != null)
        {
          // Connection lost
          MainWindow.ConsoleWarning(">> Chat bot connection lost, waiting 2 sec to reconnect.");
          Thread.Sleep(2000);
        }

        // Try to connect
        MainWindow.ConsoleWarning(">> Chat bot connecting...");
        ChatSocket = new Socket(SocketType.Stream, ProtocolType.Tcp);
        try { ChatSocket.Connect("irc.chat.twitch.tv", 6667); }
        catch (Exception ex)
        {
          MainWindow.ConsoleWarning($">> Chat bot connection error: {ex.Message}");
          ChatSocket = null;
        }
        if (ChatSocket?.Connected == true)
        {
          MainWindow.ConsoleWarning(">> Chat bot connected.");
          ChatSocket.Send(Encoding.UTF8.GetBytes($"PASS oauth:{Secret.Data[Secret.Keys.OAuthToken]}\r\n"));
          ChatSocket.Send(Encoding.UTF8.GetBytes($"NICK {Secret.Data[Secret.Keys.Name]}\r\n"));
          ChatSocket.Send(Encoding.UTF8.GetBytes($"JOIN #{Config.Data[Config.Keys.ChannelName]},#{Config.Data[Config.Keys.ChannelName]}\r\n"));
          ChatSocket.Send(Encoding.UTF8.GetBytes("CAP REQ :twitch.tv/commands twitch.tv/tags\r\n")); // request extended chat messages
        }
      }
    }

    private static void UpdateSend()
    {
      while (true)
      {
        if (ChatSocket?.Connected == true)
        {
          if (MessageQueue.Count > 0)
          {
            lock (MessageQueue)
            {
              ChatSocket.Send(Encoding.UTF8.GetBytes($"PRIVMSG #{Config.Data[Config.Keys.ChannelName]} :{MessageQueue[0]}\r\n"));
              MessageQueue.RemoveAt(0);
            }
          }
          else if ((PeriodicMessages.Count > 0) && (DateTime.Now - LastPeriodicMessage >= PeriodicMessageInterval))
          {
            LastPeriodicMessage = DateTime.Now;
            PeriodicMessageIndex = (PeriodicMessageIndex + 1) % PeriodicMessages.Count;
            ChatSocket.Send(Encoding.UTF8.GetBytes($"PRIVMSG #{Config.Data[Config.Keys.ChannelName]} :{PeriodicMessages[PeriodicMessageIndex]}\r\n"));
          }
        }

        Thread.Sleep(200); // Minimum 200 ms between send messages
      }
    }

    public static void LoadResponseMessages(bool reload = false)
    {
      if (reload) { MainWindow.ConsoleWarning(">> Reloading response messages."); }
      else { MainWindow.ConsoleWarning(">> Loading response messages."); }

      FileInfo messagesFile = new(RESPONSEMESSAGESPATH);
      // The file doesn't exist - create new one
      if (messagesFile.Exists == false)
      {
        MainWindow.ConsoleWarning(">> ResponseMessages.csv file not found. Generating new one.");
        if (!messagesFile.Directory.Exists) messagesFile.Directory.Create();

        using (StreamWriter writer = new(messagesFile.FullName))
        {
          writer.WriteLine("key; message");
          writer.WriteLine("!bot; The bot is open source. Check it out at https://github.com/Abev08/TwitchBot");
          writer.WriteLine("!example; This is example response.");
          writer.WriteLine("//!example2; This is example response that is commented out - not active.");
        }
      }

      // Read the file
      uint responseCount = 0;

      string line;
      string[] text = new string[2];
      int lineIndex = 0, separatorIndex;
      // FileShare.ReadWrite needs to be used because it have to allow other processes to write into the file
      using FileStream fileStream = new(messagesFile.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
      using StreamReader reader = new(fileStream);
      while ((line = reader.ReadLine()) != null)
      {
        lineIndex++;
        if (string.IsNullOrWhiteSpace(line)) continue;
        separatorIndex = line.IndexOf(';');
        if (separatorIndex < 0)
        {
          MainWindow.ConsoleWarning($">> Broken response message in line {lineIndex}.");
          continue;
        }

        text[0] = line.Substring(0, separatorIndex).Trim();
        text[1] = line.Substring(separatorIndex + 1).Trim();

        if (text[0].StartsWith("//")) { continue; } // Commented out line - skip it
        else if ((text[0] == "key") && (text[1] == "message")) { continue; } // This is the header, skip it
        else
        {
          // Try to add response message
          if (reload)
          {
            if (ResponseMessages.ContainsKey(text[0])) { ResponseMessages[text[0]] = (text[1].Trim(), DateTime.MinValue); }
            else { ResponseMessages.Add(text[0], (text[1].Trim(), DateTime.MinValue)); }
          }
          else if (ResponseMessages.TryAdd(text[0], (text[1].Trim(), new DateTime())))
          {
            MainWindow.ConsoleWarning($">> Added respoonse to \"{text[0]}\" key.");
            responseCount++;
          }
          else MainWindow.ConsoleWarning($">> Redefiniton of \"{text[0]}\" key in line {lineIndex}."); // TryAdd returned false - probably a duplicate
        }
      }
      if (!reload) MainWindow.ConsoleWarning($">> Loaded {responseCount} automated response messages.");

      RespMsgFileTimestamp = messagesFile.LastWriteTime;
    }

    public static bool IsRespMsgFileUpdated()
    {
      FileInfo file = new(RESPONSEMESSAGESPATH);
      if (file.Exists)
      {
        return file.LastWriteTime != RespMsgFileTimestamp;
      }

      return false;
    }

    public static void AddMessageToQueue(string message)
    {
      if (!Started) return;
      if (string.IsNullOrWhiteSpace(message)) return;

      List<string> messages = new();
      if (message.Length <= MESSAGESENTMAXLEN) { messages.Add(message); }
      else
      {
        // Split the message into smaller messages
        messages.Add(message);
        int index;
        while (messages[0].Length > MESSAGESENTMAXLEN)
        {
          // Find a good breaking point of the message - a space
          index = messages[0].LastIndexOf(" ", int.Min(MESSAGESENTMAXLEN, messages[0].Length));
          if (index <= 0)
          {
            MainWindow.ConsoleWarning(">> Something went wrong when splitting response message.");
            return; // Something broke, don't send anything
          }
          messages.Add(messages[0][..index].Trim());
          messages[0] = messages[0].Remove(0, index);
        }

        messages[0] = messages[0].Trim();
        if (messages[0].Length > 0) messages.Add(messages[0]);
        messages.RemoveAt(0);
      }

      lock (MessageQueue)
      {
        foreach (string s in messages) MessageQueue.Add(s);
      }
    }

    public static List<(long id, string name)> GetChatters()
    {
      MainWindow.ConsoleWarning(">> Acquiring chatters.");
      List<(long, string)> chatters = new();

      string uri = $"https://api.twitch.tv/helix/chat/chatters?broadcaster_id={Config.Data[Config.Keys.ChannelID]}&moderator_id={Config.Data[Config.Keys.ChannelID]}&first=1000";
      using HttpRequestMessage request = new(HttpMethod.Get, uri);
      request.Headers.Add("Authorization", $"Bearer {Secret.Data[Secret.Keys.OAuthToken]}");
      request.Headers.Add("Client-Id", Secret.Data[Secret.Keys.CustomerID]);

      string resp = Client.Send(request).Content.ReadAsStringAsync().Result;
      GetChattersResponse response = GetChattersResponse.Deserialize(resp);
      if (response?.Data?.Length > 0)
      {
        for (int i = 0; i < response.Data.Length; i++)
        {
          if (long.TryParse(response.Data[i].UserID, out long id))
          {
            chatters.Add((id, response.Data[i].UserName));
          }
        }
        if (response.Data.Length > response.Total)
        {
          MainWindow.ConsoleWarning(">> There were too many chatters to acquire in one request. A loop needs to be implemented here.");
        }
      }
      else
      {
        MainWindow.ConsoleWarning(">> Couldn't acquire chatters.");
      }

      return chatters;
    }

    public static bool CheckIfChatterIsInChat(string userName)
    {
      var chatters = GetChatters();
      foreach (var chatter in chatters)
      {
        if (chatter.name.Equals(userName)) return true;
      }

      return false;
    }

    /// <summary> Bans a chatter. Duration == 0 seconds -> perma ban. </summary>
    public static void BanChatter(string message, long id, string userName = null, int durSeconds = 15)
    {
      if (id < 0 && (userName is null || userName.Length == 0)) return;

      Chatter c;
      if (id > 0) { c = Chatter.GetChatterByID(id, userName); }
      else { c = Chatter.GetChatterByName(userName); }
      BanChatter(message, c, durSeconds);
    }

    public static void BanChatter(string message, Chatter chatter, int durSeconds = 15)
    {
      if (chatter is null) return;

      MainWindow.ConsoleWarning($"> Banning {chatter.Name} from chat for {durSeconds} seconds. {message}");

      string uri = $"https://api.twitch.tv/helix/moderation/bans?broadcaster_id={Config.Data[Config.Keys.ChannelID]}&moderator_id={Config.Data[Config.Keys.ChannelID]}";
      using HttpRequestMessage request = new(HttpMethod.Post, uri);
      request.Content = new StringContent(new BanMessageRequest(chatter.ID, durSeconds, message).ToJsonString(), Encoding.UTF8, "application/json");
      request.Headers.Add("Authorization", $"Bearer {Secret.Data[Secret.Keys.OAuthToken]}");
      request.Headers.Add("Client-Id", Secret.Data[Secret.Keys.CustomerID]);

      Client.Send(request); // We don't really need the result, just assume that it worked
    }

    private static void Hug(string userName, string message)
    {
      AddMessageToQueue($"{userName} peepoHug {message.Trim()} HUGGIES");
    }

    private static void SendCommandsResponse(string userName)
    {
      StringBuilder sb = new();
      string s;
      sb.Append('@').Append(userName).Append(" ");
      if (Notifications.ChatTTSEnabled) sb.Append("!tts message, ");
      s = MinigameFight.GetCommands();
      if (s?.Length > 0) sb.Append(s).Append(", ");
      s = MinigameGamba.GetCommands();
      if (s?.Length > 0) sb.Append(s).Append(", ");
      s = MinigameBackseat.GetCommands();
      if (s?.Length > 0) sb.Append(s).Append(", ");
      s = MinigameRude.GetCommands();
      if (s?.Length > 0) sb.Append(s).Append(", ");
      sb.Append("!hug, ");
      if (Notifications.AreSoundsAvailable()) sb.Append("!sounds, ");
      if (Notifications.WelcomeMessagesEnabled) sb.Append("!welcomemessage <empty/message>, ");
      if (Spotify.Working)
      {
        sb.Append("!song, !previoussong, ");
        if (Spotify.RequestEnabled) sb.Append("!songrequest, !sr, ");
        if (Spotify.SkipEnabled) sb.Append("!skipsong, ");
      }

      foreach (string key in ResponseMessages.Keys)
      {
        sb.Append($"{key}, ");
      }

      s = sb.ToString().Trim();
      if (s.EndsWith(',')) s = s[..^1];

      AddMessageToQueue(s);
    }

    public static void Shoutout(string userID)
    {
      if (userID is null || userID.Length == 0) return;

      MainWindow.ConsoleWarning($"> Creating shoutout for {userID}.");

      string uri = $"https://api.twitch.tv/helix/chat/shoutouts?from_broadcaster_id={Config.Data[Config.Keys.ChannelID]}&to_broadcaster_id={userID}&moderator_id={Config.Data[Config.Keys.ChannelID]}";
      using HttpRequestMessage request = new(HttpMethod.Post, uri);
      request.Headers.Add("Authorization", $"Bearer {Secret.Data[Secret.Keys.OAuthToken]}");
      request.Headers.Add("Client-Id", Secret.Data[Secret.Keys.CustomerID]);

      Client.Send(request); // We don't really need the result, just assume that it worked
    }

    public static void SongRequest(Chatter chatter, string message, bool fromNotifications = false)
    {
      if (!Spotify.Working)
      {
        AddMessageToQueue($"@{chatter?.Name} the Spotify connection is not working peepoSad");
        return;
      }
      else if (!Spotify.RequestEnabled && !fromNotifications)
      {
        AddMessageToQueue($"@{chatter?.Name} song requests are disabled peepoSad");
        return;
      }
      else if (message is null || message.Length == 0)
      {
        AddMessageToQueue($"@{chatter?.Name} maybe provide a link to the song? WeirdDude");
        return;
      }

      // Check song request timeout
      if (chatter != null && !fromNotifications)
      {
        TimeSpan tempTimeSpan = DateTime.Now - chatter.LastSongRequest;
        if (tempTimeSpan < Spotify.SongRequestTimeout)
        {
          tempTimeSpan = Spotify.SongRequestTimeout - tempTimeSpan;
          AddMessageToQueue(string.Concat(
            "@", chatter.Name, " wait ",
            tempTimeSpan.TotalSeconds < 60 ?
              $"{Math.Ceiling(tempTimeSpan.TotalSeconds)} seconds" :
              $"{Math.Ceiling(tempTimeSpan.TotalMinutes)} minutes",
            " before requesting another song WeirdDude"
          ));
          return;
        }
      }

      int index = message.IndexOf("spotify.com");
      if (index < 0)
      {
        AddMessageToQueue($"@{chatter?.Name} the link is not recognized, only spotify links are supported");
        return;
      }
      index = message.IndexOf("/track/");
      if (index < 0)
      {
        AddMessageToQueue($"@{chatter?.Name} the link is not recognized, only spotify links are supported");
        return;
      }

      index += 7; // "/track/".Length;
      string uri = message[index..].Trim();
      index = uri.IndexOf('?');
      if (index > 0) uri = uri[..index];
      index = uri.IndexOf(" ");
      if (index > 0) uri = uri[..index];

      if (uri.Length == 0)
      {
        AddMessageToQueue($"@{chatter?.Name} the link is not recognized, only spotify links are supported");
        return;
      }

      if (Spotify.AddTrackToQueue(uri))
      {
        if (!fromNotifications) chatter.LastSongRequest = DateTime.Now;
        AddMessageToQueue($"@{chatter?.Name} track added to the queue peepoHappy");
      }
      else { AddMessageToQueue($"@{chatter?.Name} something went wrong when adding the track to the queue peepoSad"); }
    }

    public static void SkipSong(Chatter chatter)
    {
      if (!Spotify.SkipEnabled) { AddMessageToQueue($"@{chatter.Name} song skips are disabled peepoSad"); }

      bool handled = false; // Handled
      DateTime minTime = DateTime.Now - TimeSpan.FromMinutes(2); // Time to check if skip request was old
      for (int j = SkipSongChatters.Count - 1; j >= 0; j--)
      {
        if (SkipSongChatters[j].ChatterID == chatter.ID)
        {
          SkipSongChatters[j].TimeRequested = DateTime.Now;
          handled = true;
        }

        if (SkipSongChatters[j].TimeRequested < minTime) SkipSongChatters.RemoveAt(j);
      }
      if (!handled) SkipSongChatters.Add(new() { ChatterID = chatter.ID, TimeRequested = DateTime.Now });

      // 5 Users requested to skip the song, skip it
      if (SkipSongChatters.Count >= Spotify.REQUIREDSKIPS)
      {
        // AddMessageToQueue($"Song skip requested from @{chatter.Name}. Enough skips were requested. Skipping the song!");
        AddMessageToQueue($"Enough !skipsong were requested. Skipping the song!");
        SkipSongChatters.Clear();
        Spotify.SkipSong();
      }
      else
      {
        // Caused too much spam
        // AddMessageToQueue(string.Concat(
        //   "Song skip requested from @", userName, ". ",
        //   Spotify.REQUIREDSKIPS - SkipSong.Count, " more skips",
        //   " required to skip the song."
        // ));
      }
    }
  }
}
