using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace AbevBot
{
  public static class Chat
  {
    /// <summary> Chat bot started. </summary>
    public static bool Started { get; private set; }
    private static Socket ChatSocket = null;
    private static readonly TimeSpan CooldownBetweenTheSameMessage = new(0, 0, 10);
    private static readonly Dictionary<string, (string, DateTime)> ResponseMessages = new();
    private static Thread ChatReceiveThread;
    private static Thread ChatSendThread;
    public static List<string> PeriodicMessages { get; set; } = new();
    private static int PeriodicMessageIndex = 0;
    private static DateTime LastPeriodicMessage = DateTime.Now;
    public static TimeSpan PeriodicMessageInterval = new(0, 10, 0);
    private static List<string> MessageQueue = new();

    public static void Start()
    {
      if (Started) return;
      Started = true;

      MainWindow.ConsoleWarning(">> Starting chat bot.");
      LoadResponseMessages();

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
      int zeroBytesReceivedCounter = 0, currentIndex, nextIndex, bytesReceived, messageStartOffset = 0;
      string userBadge, userName, customRewardID, temp;
      long userID;
      List<string> messages = new();
      /// <summary> message[0] - header, message[1] - body </summary>
      string[] message = new string[2];
      (string, DateTime) dictionaryResponse;
      ManualResetEvent receiveEvent = new(false);
      while (true)
      {
        while (ChatSocket?.Connected == true)
        {
          receiveEvent.Reset();

          ChatSocket.BeginReceive(receiveBuffer, messageStartOffset, receiveBuffer.Length - messageStartOffset, SocketFlags.None, new AsyncCallback((IAsyncResult ar) =>
          {
            if (ChatSocket.Connected) bytesReceived = ChatSocket.EndReceive(ar);
            else bytesReceived = -1;

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

                  if (message[1][1..].StartsWith("!tts")) // Check if the message starts with !tts key
                  {
                    if (Notifications.ChatTTSEnabled) { Notifications.CreateTTSNotification(message[1][6..]); } // 6.. - without ":!tts "
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
                    MinigameRude.AddRudePoint(message[1][6..]); // 6.. - without ":!rude"
                  }
                  else if (message[1][1..].StartsWith("!point")) // Check if the message starts with !point key
                  {
                    if (userBadge.Equals("STR")) MinigameBackseat.AddBackseatPoint(message[1][7..], 1); // 7.. - without ":!point"
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
                      if (!userBadge.Equals("STR")) VanishChatter(userID, userName);
                    }
                    else
                    {
                      // Vanish other user, only available for streamer and moderators
                      if (userBadge.Equals("STR") || userBadge.Equals("MOD"))
                      {
                        VanishChatter(-1, message[1][8..]); // 8.. - without ":!vanish"
                      }
                    }
                  }
                  else if (ResponseMessages.Count > 0) // Check if message starts with key to get automatic response
                  {
                    currentIndex = message[1].IndexOf(' ', 1);
                    if (currentIndex < 0) currentIndex = message[1].Length - 1;
                    temp = message[1].Substring(1, currentIndex).Trim();
                    if (ResponseMessages.TryGetValue(temp, out dictionaryResponse))
                    {
                      // Check if the same message was send not long ago
                      if (DateTime.Now - dictionaryResponse.Item2 >= CooldownBetweenTheSameMessage)
                      {
                        AddMessageToQueue($"@{userName} {dictionaryResponse.Item1}");
                        ResponseMessages[temp] = (ResponseMessages[temp].Item1, DateTime.Now);
                      }
                      else MainWindow.ConsoleWarning($">> Not sending response for \"{temp}\" key. Cooldown active.");
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
                        " gifted a sub for",
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
                      currentIndex = message[0].IndexOf("msg-param-viewerCount=") + 22; // 26 == "msg-param-viewerCount=".Length
                      MainWindow.ConsoleWriteLine(string.Concat(
                        "> ",
                        userName,
                        " raided the channel with ",
                        message[0].Substring(currentIndex, message[0].IndexOf(';', currentIndex) - currentIndex),
                        " viewers."
                      ));
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
                ChatSocket.Close(); // Close connection if 5 times in a row received 0 bytes
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
          MainWindow.ConsoleWarning($"Chat bot connection error: {ex.Message}");
          ChatSocket = null;
        }
        if (ChatSocket?.Connected == true)
        {
          MainWindow.ConsoleWarning(">> Chat bot connected.");
          ChatSocket.Send(Encoding.UTF8.GetBytes($"PASS oauth:{Config.Data[Config.Keys.BotOAuthToken]}\r\n"));
          ChatSocket.Send(Encoding.UTF8.GetBytes($"NICK {Config.Data[Config.Keys.BotNick]}\r\n"));
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
            ChatSocket.Send(Encoding.UTF8.GetBytes($"PRIVMSG #{Config.Data[Config.Keys.ChannelName]} :{PeriodicMessages[PeriodicMessageIndex]}\r\n"));
            PeriodicMessageIndex = (PeriodicMessageIndex + 1) % PeriodicMessages.Count;
          }
        }

        Thread.Sleep(200); // Minimum 200 ms between send messages
      }
    }

    private static void LoadResponseMessages()
    {
      MainWindow.ConsoleWarning(">> Loading response messages.");

      FileInfo messagesFile = new(@"Resources/ResponseMessages.csv");
      // The file doesn't exist - create new one
      if (messagesFile.Exists == false)
      {
        MainWindow.ConsoleWarning(">> ResponseMessages.csv file not found. Generating new one.");
        if (!messagesFile.Directory.Exists) messagesFile.Directory.Create();

        using (StreamWriter writer = new(messagesFile.FullName))
        {
          writer.WriteLine("key; message");
          writer.WriteLine("!example; This is example response.");
          writer.WriteLine("//!example2; This is example response that is commented out - not active.");
        }
      }

      // Read the file
      uint responseCount = 0;
      using (StreamReader reader = new(messagesFile.FullName))
      {
        string line;
        string[] text = new string[2];
        int lineIndex = 0, separatorIndex;
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
            if (ResponseMessages.TryAdd(text[0], (text[1], new DateTime())))
            {
              MainWindow.ConsoleWarning($">> Added respoonse to \"{text[0]}\" key.");
              responseCount++;
            }
            else MainWindow.ConsoleWarning($">> Redefiniton of \"{text[0]}\" key in line {lineIndex}."); // TryAdd returned false - probably a duplicate
          }
        }
        MainWindow.ConsoleWarning($">> Loaded {responseCount} automated response messages.");
      }
    }

    public static void AddMessageToQueue(string message)
    {
      if (!Started) return;
      if (string.IsNullOrWhiteSpace(message)) return;

      lock (MessageQueue)
      {
        MessageQueue.Add(message);
      }
    }

    public static List<(long id, string name)> GetChatters()
    {
      MainWindow.ConsoleWarning(">> Acquiring chatters.");
      List<(long, string)> chatters = new();

      string uri = $"https://api.twitch.tv/helix/chat/chatters?broadcaster_id={Config.Data[Config.Keys.ChannelID]}&moderator_id={Config.Data[Config.Keys.ChannelID]}&first=1000";
      using (HttpRequestMessage request = new(new HttpMethod("GET"), uri))
      {
        request.Headers.Add("Authorization", $"Bearer {Config.Data[Config.Keys.BotOAuthToken]}");
        request.Headers.Add("Client-Id", Config.Data[Config.Keys.BotClientID]);

        using (HttpClient client = new())
        {
          GetChattersResponse response = GetChattersResponse.Deserialize(client.Send(request).Content.ReadAsStringAsync().Result);
          if (response?.Data?.Length > 0)
          {
            long id;
            for (int i = 0; i < response.Data.Length; i++)
            {
              if (long.TryParse(response.Data[i].UserID, out id))
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
        }
      }

      return chatters;
    }

    public static void VanishChatter(long id, string userName = null)
    {
      if (id < 0 && (userName is null || userName.Length == 0)) return;

      Chatter c;
      if (id > 0) { c = Chatter.GetChatterByID(id, userName); }
      else { c = Chatter.GetChatterByName(userName); }

      if (c is null) return;

      MainWindow.ConsoleWarning($"> Vanishing {c.Name} from chat.");

      string uri = $"https://api.twitch.tv/helix/moderation/bans?broadcaster_id={Config.Data[Config.Keys.ChannelID]}&moderator_id={Config.Data[Config.Keys.ChannelID]}";
      using (HttpRequestMessage request = new(new HttpMethod("POST"), uri))
      {
        request.Content = new StringContent("{\"data\": {\"user_id\":\"" + c.ID + "\",\"reason\":\"!vanish command\",\"duration\":15}}");
        request.Headers.Add("Authorization", $"Bearer {Config.Data[Config.Keys.BotOAuthToken]}");
        request.Headers.Add("Client-Id", Config.Data[Config.Keys.BotClientID]);
        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");

        using (HttpClient client = new())
        {
          client.Send(request); // We don't really need the result, just assume that it worked
        }
      }
    }
  }
}
