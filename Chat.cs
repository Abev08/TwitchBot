using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;

using Serilog;

namespace AbevBot;

public static class Chat
{
  /// <summary> Maximum number of characters in one message. 500 characters Twitch limit, -40 characters as a buffer </summary>
  private const int MESSAGESENTMAXLEN = 460;
  /// <summary> Path to .csv file with response messages. </summary>
  private const string RESPONSEMESSAGESPATH = "Resources/ResponseMessages.csv";

  /// <summary> Is chat bot started? </summary>
  public static bool IsStarted { get; private set; }
  /// <summary> TCP Socket connection to Twitch server. </summary>
  private static Socket ChatSocket = null;
  /// <summary> Minimum time between the same response message. </summary>
  private static readonly TimeSpan CooldownBetweenTheSameMessage = new(0, 0, 10);
  /// <summary> Collection of response messages. </summary>
  public static readonly Dictionary<string, (string response, DateTime lastUsed)> ResponseMessages = new();
  /// <summary> Chat bot receive messages thread. </summary>
  private static Thread ChatReceiveThread;
  /// <summary> Chat bot send messages thread. </summary>
  private static Thread ChatSendThread;
  /// <summary> Collection of periodic messages. </summary>
  public static readonly List<string> PeriodicMessages = new();
  /// <summary> Index of last send periodic message. </summary>
  private static int PeriodicMessageIndex = -1;
  /// <summary> Time of last send periodic message. </summary>
  private static DateTime LastPeriodicMessage = DateTime.Now;
  /// <summary> Minimum time interval between periodic messages. </summary>
  public static TimeSpan PeriodicMessageInterval = new(0, 10, 0);
  /// <summary> Minimum amount of chat messages between periodic message can be sent. </summary>
  public static int PeriodicMessageMinChatMessages = 10;
  /// <summary> Count of chat messages from the last periodic message. </summary>
  private static int ChatMessagesSinceLastPeriodicMessage;
  /// <summary> Queue of messages to be sent to the chat. </summary>
  private static readonly List<string> MessageQueue = new();
  /// <summary> Additional HTTP client used for some Twitch commands. </summary>
  private static readonly HttpClient Client = new();
  /// <summary> Last modified date and time of response messages file (used for hot reloading). </summary>
  private static DateTime RespMsgFileTimestamp;
  /// <summary> List of chatters that requested song skip. </summary>
  private static readonly List<SkipSongChatter> SkipSongChatters = new();
  /// <summary> !vanish chat command enabled </summary>
  public static bool VanishEnabled { get; set; }

  /// <summary> Starts the chat bot. </summary>
  public static void Start()
  {
    if (IsStarted) return;
    IsStarted = true;

    Log.Information("Starting chat bot.");
    LoadResponseMessages();
    if (!ResponseMessages.TryAdd("!commands", (string.Empty, DateTime.MinValue)))
    {
      Log.Warning("Couldn't add {command} response. Maybe something already declared it?", "!commands");
    }
    if (!ResponseMessages.TryAdd("!lang", ("Please speak English in the chat, thank you ❤", DateTime.MinValue)))
    {
      Log.Warning("Couldn't add {command} response. Maybe something already declared it?", "!lang");
    }

    ChatReceiveThread = new Thread(UpdateReceive)
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

  /// <summary> Main update method used by receive thread. </summary>
  private static void UpdateReceive()
  {
    List<string> messages = new();
    byte[] receiveBuffer = new byte[16384]; // Max IRC message is 4096 bytes? let's allocate 4 times that, 2 times max message length wasn't enaugh for really fast chats
    int messageStartOffset = 0, bytesReceived = 0;
    int zeroBytesReceivedCounter = 0;
    int temp, temp2;

    string header = string.Empty;
    string body = string.Empty;
    MessageMetadata metadata = new();

    IAsyncResult receiveResult = null;

    while (true)
    {
      if (MainWindow.CloseRequested) { return; }

      while (ChatSocket?.Connected == true)
      {
        if (MainWindow.CloseRequested) { return; }

        if (receiveResult is null)
        {
          receiveResult = ChatSocket.BeginReceive(receiveBuffer, messageStartOffset, receiveBuffer.Length - messageStartOffset, SocketFlags.None, new AsyncCallback((IAsyncResult ar) =>
          {
            try
            {
              bytesReceived = ChatSocket.Connected ? ChatSocket.EndReceive(ar) : -1;
            }
            catch (Exception ex)
            {
              Log.Error("Chat bot error: {ex}", ex);
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
                  messages.Insert(1, messages[0][messageStartOffset..]);
                  messages[0] = messages[0][..messageStartOffset];
                }
              }

              for (int i = 0; i < messages.Count; i++)
              {
                string msg = messages[i];

                // PING - keepalive message
                if (msg.StartsWith("PING"))
                {
                  string response = msg.Replace("PING", "PONG");
                  ChatSocket.Send(Encoding.UTF8.GetBytes(response + "\r\n"));
                  continue;
                }

                (header, body) = ParseMessage(msg, ref metadata);

                switch (metadata.MessageType)
                {
                  case "PRIVMSG":
                    if (metadata.CustromRewardID.Length > 0)
                    {
                      Log.Information("{userName} redeemed custom reward with ID: {customRewardID}. {msg}",
                        metadata.UserName,
                        metadata.CustromRewardID,
                        body);
                    }
                    else if (metadata.Bits.Length > 0)
                    {
                      Log.Information("{userName} cheered with {bits} bits. {msg}",
                        metadata.UserName,
                        metadata.Bits,
                        body);
                    }
                    else
                    {
                      ChatMessagesSinceLastPeriodicMessage += 1;
                      if (Config.PrintChatMessages)
                      {
                        MainWindow.ConsoleWriteLine(string.Format(
                          "{0, -4}{1, 22}{2, 2}{3, -0}",
                          metadata.Badge,
                        metadata.UserName,
                        ": ",
                        body
                      ));
                      }
                      CheckForChatCommands(ref metadata, body);
                    }
                    break;

                  case "USERNOTICE":
                    switch (metadata.MsgID)
                    {
                      case "sub":
                        Log.Information("{userName} subscribed! {msg}",
                          metadata.UserName,
                          body);
                        break;
                      case "resub":
                        Log.Information("{userName} resubscribed! {msg}",
                          metadata.UserName,
                          body);
                        break;
                      case "subgift":
                        string receipent = string.Empty;
                        temp = header.IndexOf("msg-param-recipient-display-name=");
                        if (temp >= 0)
                        {
                          temp += 33; // 33 == "msg-param-recipient-display-name=".len()
                          temp2 = header.IndexOf(';', temp);
                          if (temp2 >= 0) { receipent = header[temp..temp2]; }
                        }
                        Log.Information("{userName} gifted sub to {userName2}! {msg}",
                          metadata.UserName,
                          receipent,
                          body);
                        break;
                      case "submysterygift":
                        Log.Information("{userName} gifted some subs to random viewers! {msg}",
                          metadata.UserName,
                          body);
                        break;
                      case "primepaidupgrade":
                        Log.Information("{userName} converted prime sub to standard sub! {msg}",
                          metadata.UserName,
                          body);
                        break;
                      case "giftpaidupgrade":
                        Log.Information("{userName} continuing sub gifted by another chatter! {msg}",
                          metadata.UserName,
                          body);
                        break;
                      case "communitypayforward":
                        Log.Information("{userName} is paying forward sub gifted by another chatter! {msg}",
                          metadata.UserName,
                          body);
                        break;
                      case "announcement":
                        Log.Information("{userName} announced that! {msg}",
                          metadata.UserName,
                          body);
                        break;
                      case "raid":
                        Log.Information("{userName} raided the channel! {msg}",
                          metadata.UserName,
                          body);
                        break;
                      case "viewermilestone":
                        Log.Information("{userName} did something that fired viewer milestone! {msg}",
                          metadata.UserName,
                          body);
                        break;
                      default:
                        // Message type not recognized - print the whole message
                        MainWindow.ConsoleWriteLine(msg);
                        break;
                    }
                    break;

                  case "CLEARCHAT":
                    if (msg.StartsWith("@ban-duration"))
                    {
                      temp = msg.LastIndexOf(':');
                      temp = temp > 0 ? temp + 1 : msg.Length;
                      Log.Information("{userName} got banned!", msg[temp..]);
                    }
                    else if (body.Length > 0) { Log.Information("{userName} chat messages got cleared", body); }
                    else { Log.Information("Chat got cleared"); }
                    break;

                  case "CLEARMSG":
                    if (msg.StartsWith("@login="))
                    {
                      temp = msg.IndexOf(';');
                      if (temp < 0) temp = msg.Length;
                      Log.Information("{userName} perma banned!", msg[7..temp]);
                    }
                    else { Log.Information("Someones messages got cleared"); }
                    break;

                  case "NOTICE":
                    switch (metadata.MsgID)
                    {
                      case "emote_only_on":
                        Log.Information("This room is now in emote-only mode.");
                        break;
                      case "emote_only_off":
                        Log.Information("This room is no longer in emote-only mode.");
                        break;
                      case "subs_on":
                        Log.Information("This room is now in subscribers-only mode.");
                        break;
                      case "subs_off":
                        Log.Information("This room is no longer in subscribers-only mode.");
                        break;
                      case "followers_on":
                      case "followers_on_zero":
                        Log.Information("This room is now in followers-only mode.");
                        break;
                      case "followers_off":
                        Log.Information("This room is no longer in followers-only mode.");
                        break;
                      case "msg_followersonly":
                        Log.Information("This room is in 10 minutes followers-only mode.");
                        break;
                      case "slow_on":
                        Log.Information("This room is now in slow mode.");
                        break;
                      case "slow_off":
                        Log.Information("This room is no longer in slow mode.");
                        break;
                      default:
                        // Message type not recognized - print the whole message
                        MainWindow.ConsoleWriteLine(msg);
                        break;
                    }
                    break;

                  case "ROOMSTATE":
                    // Room state changed - do nothing? This message is always send with another one?
                    break;

                  case "USERSTATE":
                    if (Config.PrintChatMessages)
                    {
                      MainWindow.ConsoleWriteLine($"> Bot message from {metadata.UserName}");
                    }
                    break;

                  default:
                    // Not recognized message
                    MainWindow.ConsoleWriteLine(msg);
                    break;
                }
              }
              zeroBytesReceivedCounter = 0;
            }
            else
            {
              Log.Warning("Chat bot received {amount} bytes", 0);
              zeroBytesReceivedCounter++;
              if (zeroBytesReceivedCounter >= 5)
              {
                Log.Error("Chat bot closing connection.");
                ChatSocket?.Close(); // Close connection if 5 times in a row received 0 bytes
              }
            }

            receiveResult = null;
          }), null);
        }

        Thread.Sleep(10);
      }

      if (ChatSocket != null)
      {
        // Connection lost
        Log.Warning("Chat bot connection lost, waiting 2 sec to reconnect.");
        Thread.Sleep(2000);
      }

      // Try to connect
      Log.Information("Chat bot connecting...");
      ChatSocket = new Socket(SocketType.Stream, ProtocolType.Tcp);
      try { ChatSocket.Connect("irc.chat.twitch.tv", 6667); }
      catch (Exception ex)
      {
        Log.Error("Chat bot connection error: {ex}", ex);
        ChatSocket = null;
        Thread.Sleep(2000);
      }
      if (ChatSocket?.Connected == true)
      {
        Log.Information("Chat bot connected.");
        ChatSocket.Send(Encoding.UTF8.GetBytes($"PASS oauth:{Secret.Data[Secret.Keys.OAuthToken]}\r\n"));
        ChatSocket.Send(Encoding.UTF8.GetBytes($"NICK {Secret.Data[Secret.Keys.Name]}\r\n"));
        ChatSocket.Send(Encoding.UTF8.GetBytes($"JOIN #{Config.Data[Config.Keys.ChannelName]},#{Config.Data[Config.Keys.ChannelName]}\r\n"));
        ChatSocket.Send(Encoding.UTF8.GetBytes("CAP REQ :twitch.tv/commands twitch.tv/tags\r\n")); // request extended chat messages

        ChatSocket.ReceiveTimeout = 100;
      }
    }
  }

  /// <summary> Main update method used by the send thread. </summary>
  private static void UpdateSend()
  {
    while (true)
    {
      if (MainWindow.CloseRequested) { return; }

      if (ChatSocket?.Connected == true)
      {
        if (MessageQueue.Count > 0)
        {
          lock (MessageQueue)
          {
            ChatSocket.Send(Encoding.UTF8.GetBytes(MessageQueue[0]));
            MessageQueue.RemoveAt(0);
          }
        }
        else if ((PeriodicMessages.Count > 0) && (ChatMessagesSinceLastPeriodicMessage >= PeriodicMessageMinChatMessages) && (DateTime.Now - LastPeriodicMessage >= PeriodicMessageInterval))
        {
          ChatMessagesSinceLastPeriodicMessage = 0;
          LastPeriodicMessage = DateTime.Now;
          PeriodicMessageIndex = (PeriodicMessageIndex + 1) % PeriodicMessages.Count;
          AddMessageToQueue(PeriodicMessages[PeriodicMessageIndex]);
        }
      }

      Thread.Sleep(200); // Minimum 200 ms between send messages
    }
  }

  /// <summary> Loads response messages from the file. </summary>
  /// <param name="reload">Is the file being reloaded?</param>
  public static void LoadResponseMessages(bool reload = false)
  {
    if (reload) { Log.Information("Reloading response messages."); }
    else { Log.Information("Loading response messages."); }

    FileInfo messagesFile = new(RESPONSEMESSAGESPATH);
    // The file doesn't exist - create new one
    if (messagesFile.Exists == false)
    {
      Log.Warning("{file} file not found. Generating new one.", "ResponseMessages.csv");
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
        Log.Warning("Broken response message in line {index}.", lineIndex);
        continue;
      }

      text[0] = line[..separatorIndex].Trim();
      text[1] = line[(separatorIndex + 1)..].Trim();

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
          Log.Information("Added respoonse to \"{key}\" key.", text[0]);
          responseCount++;
        }
        else { Log.Warning("Redefiniton of \"{key}\" key in line {index}.", text[0], lineIndex); } // TryAdd returned false - probably a duplicate
      }
    }
    if (!reload) { Log.Information("Loaded {count} automated response messages.", responseCount); }

    RespMsgFileTimestamp = messagesFile.LastWriteTime;
  }

  /// <summary> Checks if response messages file was updated. </summary>
  /// <returns><value>true</value> if the file was updated, otherwise <value>false</value></returns>
  public static bool IsRespMsgFileUpdated()
  {
    FileInfo file = new(RESPONSEMESSAGESPATH);
    if (file.Exists)
    {
      return file.LastWriteTime != RespMsgFileTimestamp;
    }

    return false;
  }

  /// <summary> Adds chat message to the queue. </summary>
  /// <param name="message">Message to be sent</param>
  public static void AddMessageToQueue(string message)
  {
    if (!IsStarted) return;
    if (string.IsNullOrWhiteSpace(message)) return;

    StringBuilder sb = new();
    int start = 0;
    int end = message.Length > MESSAGESENTMAXLEN ? MESSAGESENTMAXLEN : message.Length;

    while (true)
    {
      sb.Clear();
      sb.Append("PRIVMSG #");
      sb.Append(Config.Data[Config.Keys.ChannelName]);
      sb.Append(" :");
      sb.Append(message[start..end]);
      sb.Append("\r\n");

      lock (MessageQueue)
      {
        MessageQueue.Add(sb.ToString());
      }

      if (end == message.Length) break;
      start = end + 1;
      end += MESSAGESENTMAXLEN;
      if (end > message.Length) end = message.Length;
    }
    return;
  }

  /// <summary> Adds chat message response to the queue. </summary>
  /// <param name="message">Message to be sent in response</param>
  /// <param name="messageID">Message ID of the message we are responding to</param>
  public static void AddMessageResponseToQueue(string message, string messageID)
  {
    if (!IsStarted) return;
    if (string.IsNullOrWhiteSpace(message)) return;

    StringBuilder sb = new();
    int start = 0;
    int end = message.Length > MESSAGESENTMAXLEN ? MESSAGESENTMAXLEN : message.Length;

    while (true)
    {
      sb.Clear();
      sb.Append("@reply-parent-msg-id=");
      sb.Append(messageID);
      sb.Append(" PRIVMSG #");
      sb.Append(Config.Data[Config.Keys.ChannelName]);
      sb.Append(" :");
      sb.Append(message[start..end]);
      sb.Append("\r\n");

      lock (MessageQueue)
      {
        MessageQueue.Add(sb.ToString());
      }

      if (end == message.Length) break;
      start = end + 1;
      end += MESSAGESENTMAXLEN;
      if (end > message.Length) end = message.Length;
    }
  }

  /// <summary> Gets current chatters. </summary>
  /// <returns>List of chatters that are connected to the chat</returns>
  public static List<(long id, string name)> GetChatters()
  {
    Log.Information("Acquiring chatters.");
    List<(long, string)> chatters = new();

    string uri = $"https://api.twitch.tv/helix/chat/chatters?broadcaster_id={Config.Data[Config.Keys.ChannelID]}&moderator_id={Config.Data[Config.Keys.ChannelID]}&first=1000";
    using HttpRequestMessage request = new(HttpMethod.Get, uri);
    request.Headers.Add("Authorization", $"Bearer {Secret.Data[Secret.Keys.OAuthToken]}");
    request.Headers.Add("Client-Id", Secret.Data[Secret.Keys.CustomerID]);

    string resp;
    try { resp = Client.Send(request).Content.ReadAsStringAsync().Result; }
    catch (HttpRequestException ex) { Log.Error("Acquiring chatters failed. {ex}", ex); return chatters; }
    var response = GetChattersResponse.Deserialize(resp);
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
        Log.Warning("There were too many chatters to acquire in one request. A loop needs to be implemented here.");
      }
    }
    else { Log.Warning("Couldn't acquire chatters."); }

    return chatters;
  }

  /// <summary> Checks if provided chatter name is currently in the chat. </summary>
  /// <param name="userName">Chatter name to be checked</param>
  /// <returns><value>true</value> if the chatter is currently in the chat, otherwise <value>false</value></returns>
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
  /// <param name="message">Ban message to be displayed (reason)</param>
  /// <param name="id">ID of the chatter that should be banned</param>
  /// <param name="userName">(optional) chatter name</param>
  /// <param name="durSeconds">Duration of the ban (0 sec - perma ban)</param>
  public static void BanChatter(string message, long id, string userName = null, int durSeconds = 15)
  {
    if (id < 0 && (userName is null || userName.Length == 0)) return;

    Chatter c;
    if (id > 0) { c = Chatter.GetChatterByID(id, userName); }
    else { c = Chatter.GetChatterByName(userName); }
    BanChatter(message, c, durSeconds);
  }

  /// <summary> Bans a chatter. Duration == 0 seconds -> perma ban. </summary>
  /// <param name="message">Ban message to be displayed (reason)</param>
  /// <param name="chatter">Chatter to be banned</param>
  /// <param name="durSeconds">Duration of the ban (0 sec - perma ban)</param>
  public static void BanChatter(string message, Chatter chatter, int durSeconds = 15)
  {
    if (chatter is null) return;

    Log.Information("Banning {userName} from chat for {duration} seconds. {msg}", chatter.Name, durSeconds, message);

    string uri = $"https://api.twitch.tv/helix/moderation/bans?broadcaster_id={Config.Data[Config.Keys.ChannelID]}&moderator_id={Config.Data[Config.Keys.ChannelID]}";
    using HttpRequestMessage request = new(HttpMethod.Post, uri);
    request.Content = new StringContent(new BanMessageRequest(chatter.ID, durSeconds, message).ToJsonString(), Encoding.UTF8, "application/json");
    request.Headers.Add("Authorization", $"Bearer {Secret.Data[Secret.Keys.OAuthToken]}");
    request.Headers.Add("Client-Id", Secret.Data[Secret.Keys.CustomerID]);

    try { Client.Send(request); } // We don't really need the result, just assume that it worked
    catch (HttpRequestException ex) { Log.Error("Banning chatter failed. {ex}", ex); }
  }

  /// <summary> Creates hug message. </summary>
  /// <param name="userName">Chatter name that is hugging something</param>
  /// <param name="message">Name of something that is being hugged</param>
  private static void Hug(string userName, string message)
  {
    AddMessageToQueue($"{userName} peepoHug {message.Trim()} HUGGIES");
  }

  /// <summary> Creates `!commands` response and sends it to the chat. </summary>
  /// <param name="userName">Chatter name that requested the response</param>
  /// <param name="messageID">Message ID of the message we are responding to</param>
  private static void SendCommandsResponse(string userName, string messageID)
  {
    StringBuilder sb = new();
    string s;
    sb.Append('@').Append(userName).Append(' ');
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
    if (Notifications.WelcomeMessagesEnabled) sb.Append("!welcomemessage <empty/message>, !welcomemessageclear, ");
    if (Spotify.Working)
    {
      sb.Append("!song, !previoussong, !songqueue, ");
      if (Spotify.RequestEnabled) sb.Append("!songrequest, !sr, ");
      if (Spotify.SkipEnabled) sb.Append("!skipsong, ");
    }
    if (VanishEnabled) { sb.Append("!vanish, "); }

    foreach (string key in ResponseMessages.Keys)
    {
      sb.Append($"{key}, ");
    }

    s = sb.ToString().Trim();
    if (s.EndsWith(',')) s = s[..^1];

    AddMessageResponseToQueue(s, messageID);
  }

  /// <summary> Creates chat shoutout of the provided chatter. </summary>
  /// <param name="userID">Chatter ID for which the shoutout should be created</param>
  public static void Shoutout(string userID)
  {
    if (userID is null || userID.Length == 0) return;

    Log.Information("Creating shoutout for {userID}.", userID);

    string uri = $"https://api.twitch.tv/helix/chat/shoutouts?from_broadcaster_id={Config.Data[Config.Keys.ChannelID]}&to_broadcaster_id={userID}&moderator_id={Config.Data[Config.Keys.ChannelID]}";
    using HttpRequestMessage request = new(HttpMethod.Post, uri);
    request.Headers.Add("Authorization", $"Bearer {Secret.Data[Secret.Keys.OAuthToken]}");
    request.Headers.Add("Client-Id", Secret.Data[Secret.Keys.CustomerID]);

    try { Client.Send(request); } // We don't really need the result, just assume that it worked
    catch (HttpRequestException ex) { Log.Error("Creating shoutout failed. {ex}", ex); }
  }

  /// <summary> Handles song request chat command. </summary>
  /// <param name="chatter">Chatter that requested the command</param>
  /// <param name="message">Chat message</param>
  /// <param name="messageID">Message ID of the message we are responding to</param>
  /// <param name="fromNotifications">Is the request from notifiactions?</param>
  public static void SongRequest(Chatter chatter, string message, string messageID, bool fromNotifications = false)
  {
    if (!Spotify.Working)
    {
      AddMessageResponseToQueue($"@{chatter?.Name} the Spotify connection is not working peepoSad", messageID);
      return;
    }
    else if (!Spotify.RequestEnabled && !fromNotifications)
    {
      AddMessageResponseToQueue($"@{chatter?.Name} song requests are disabled peepoSad", messageID);
      return;
    }
    else if (message is null || message.Length == 0)
    {
      AddMessageResponseToQueue($"@{chatter?.Name} maybe provide a link to the song? WeirdDude", messageID);
      return;
    }

    // Check song request timeout
    if (chatter != null && !fromNotifications)
    {
      TimeSpan tempTimeSpan = DateTime.Now - chatter.LastSongRequest;
      if (tempTimeSpan < Spotify.SongRequestTimeout)
      {
        tempTimeSpan = Spotify.SongRequestTimeout - tempTimeSpan;
        AddMessageResponseToQueue(string.Concat(
          "@", chatter.Name, " wait ",
          tempTimeSpan.TotalSeconds < 60 ?
            $"{Math.Ceiling(tempTimeSpan.TotalSeconds)} seconds" :
            $"{Math.Ceiling(tempTimeSpan.TotalMinutes)} minutes",
          " before requesting another song WeirdDude"
        ), messageID);
        return;
      }
    }

    int index = message.IndexOf("spotify.com");
    if (index < 0)
    {
      AddMessageResponseToQueue($"@{chatter?.Name} the link is not recognized, only spotify links are supported", messageID);
      return;
    }
    index = message.IndexOf("/track/");
    if (index < 0)
    {
      AddMessageResponseToQueue($"@{chatter?.Name} the link is not recognized, only spotify links are supported", messageID);
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
      AddMessageResponseToQueue($"@{chatter?.Name} the link is not recognized, only spotify links are supported", messageID);
      return;
    }

    if (Spotify.AddTrackToQueue(uri))
    {
      if (!fromNotifications) chatter.LastSongRequest = DateTime.Now;
      AddMessageResponseToQueue($"@{chatter?.Name} track added to the queue peepoHappy", messageID);
    }
    else { AddMessageResponseToQueue($"@{chatter?.Name} something went wrong when adding the track to the queue peepoSad", messageID); }
  }

  /// <summary> Handles song skip command. </summary>
  /// <param name="chatter">Chatter that requested the song skip</param>
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

  /// <summary> Parses received raw chat message. </summary>
  /// <param name="msg">Raw chat message</param>
  /// <param name="metadata">Metadata of the message to be updated</param>
  /// <returns>Tuple of the header and body parts of the message</returns>
  static (string header, string body) ParseMessage(string msg, ref MessageMetadata metadata)
  {
    metadata.Clear();
    string header = string.Empty;
    string body = string.Empty;

    int temp, temp2;

    // Find header <-> body "separator"
    temp = msg.IndexOf("tmi.twitch.tv");
    if (temp < 0)
    {
      Log.Error("Chat message not parsed correctly\n{msg}", msg);
      return (header, body);
    }

    // Get message header
    header = msg[..temp];
    temp += 14; // 14 == "tmi.twitch.tv ".len()

    // Get message type
    temp2 = msg[temp..].IndexOf(' ');
    if (temp2 < 0) { temp2 = msg.Length - temp; }
    metadata.MessageType = msg[temp..(temp + temp2)];
    temp2 += 1 + temp;

    // Get message body
    temp = msg[temp2..].IndexOf(':');
    if (temp > 0) body = msg[(temp + 1 + temp2)..];

    // Get header data
    var headerData = header.Split(';', ' ');
    foreach (var data in headerData)
    {
      if (data.StartsWith("id=")) { metadata.MessageID = data[3..]; } // 3 == "id=".len()
      else if (data.StartsWith("badges="))
      {
        // Badge
        var badge = data[7..]; // 7 == "badges=".len()
        if (badge.StartsWith("broadcaster")) { metadata.Badge = "STR"; }
        else if (badge.StartsWith("moderator")) { metadata.Badge = "MOD"; }
        else if (badge.StartsWith("subscriber")) { metadata.Badge = "SUB"; }
        else if (badge.StartsWith("vip")) { metadata.Badge = "VIP"; }
      }
      else if (data.StartsWith("display-name=")) { metadata.UserName = data[13..]; } // 13 == "display-name=".len()
      else if (data.StartsWith("user-id=")) { _ = long.TryParse(data[8..], out metadata.UserID); } // 8 == "user-id=".len()
      else if (data.StartsWith("custom-reward-id=")) { metadata.CustromRewardID = data[17..]; } // 17 == "custom-reward-id=".len()
      else if (data.StartsWith("bits=")) { metadata.Bits = data[5..]; } // 5 == "bits=".len()
      else if (data.StartsWith("@msg-id=")) { metadata.MsgID = data[8..]; } // 8 == "@msg-id=".len(), msg_id - special message type, being it's own message
      else if (data.StartsWith("msg-id=")) { metadata.MsgID = data[7..]; } // 7 == "msg-id=".len(), msg_id - special message type, attached to normal message
    }

    return (header, body);
  }

  /// <summary> Checks chat message for commands. </summary>
  /// <param name="metadata">Metadata of the chat message</param>
  /// <param name="msg">Message body to be checked</param>
  static void CheckForChatCommands(ref MessageMetadata metadata, string msg)
  {
    var chatter = Chatter.GetChatterByID(metadata.UserID, metadata.UserName);
    chatter.LastChatted = DateTime.Now;
    if (Notifications.WelcomeMessagesEnabled && chatter.WelcomeMessage?.Length > 0 && chatter.LastWelcomeMessage.Date != DateTime.Now.Date)
    {
      Log.Information("Creating {userName} welcome message TTS.", metadata.UserName);
      chatter.SetLastWelcomeMessageToNow();
      Notifications.CreateTTSNotification(chatter.WelcomeMessage);
    }

    if (msg.StartsWith("!tts")) // Check if the message starts with !tts key
    {
      if (msg.Length <= 5) { Log.Warning("!tts command without a message Susge"); } // No message to read, do nothing
      else if (Notifications.ChatTTSEnabled || Chatter.AlwaysReadTTSFromThem.Contains(metadata.UserName)) { Notifications.CreateTTSNotification(msg[5..]); } // 5.. - without "!tts "
      else { AddMessageResponseToQueue($"@{metadata.UserName} TTS disabled peepoSad", metadata.MessageID); }
    }
    else if (msg.StartsWith("!gamba")) // Check if the message starts with !gamba key
    {
      MinigameGamba.NewGamba(metadata.UserID, metadata.UserName, msg[6..]); // 6.. - without "!gamba"
    }
    else if (msg.StartsWith("!fight")) // Check if the message starts with !fight key
    {
      MinigameFight.NewFight(metadata.UserID, metadata.UserName, msg[6..]); // 6.. - without "!fight"
    }
    else if (msg.StartsWith("!rude")) // Check if the message starts with !rude key
    {
      MinigameRude.AddRudePoint(metadata.UserName, msg[5..]); // 5.. - without "!rude"
    }
    else if (msg.StartsWith("!point")) // Check if the message starts with !point key
    {
      if (metadata.Badge.Equals("STR") || msg.Length == 6) MinigameBackseat.AddBackseatPoint(msg[6..], 1); // 6.. - without "!point"
      else AddMessageResponseToQueue($"@{metadata.UserName} That's for the streamer, you shouldn't be using it Madge", metadata.MessageID);
    }
    else if (msg.StartsWith("!unpoint")) // Check if the message starts with !unpoint key
    {
      if (metadata.Badge.Equals("STR")) MinigameBackseat.AddBackseatPoint(msg[8..], -1); // 8.. - without "!unpoint"
      else AddMessageResponseToQueue($"@{metadata.UserName} That's for the streamer, you shouldn't be using it Madge", metadata.MessageID);
    }
    else if (msg.StartsWith("!vanish")) // Check if the message starts with !vanish key
    {
      if (!VanishEnabled) { } // Disabled - do nothing
      else if (msg.Length == 7)
      {
        // Vanish the command user
        if (!metadata.Badge.Equals("STR")) BanChatter("!vanish command", metadata.UserID, metadata.UserName);
      }
      else
      {
        // Vanish other user, only available for streamer and moderators
        if (metadata.Badge.Equals("STR") || metadata.Badge.Equals("MOD"))
        {
          BanChatter("!vanish command", -1, msg[7..]); // 7.. - without "!vanish"
        }
      }
    }
    else if (msg.StartsWith("!hug")) // Check if the message starts with !hug key
    {
      Hug(metadata.UserName, msg[4..]); // 4.. - without "!hug"
    }
    else if (msg.StartsWith("!commands")) // Check if the message starts with !commands key
    {
      // Check the timeout
      if (ResponseMessages.TryGetValue("!commands", out var rm) && (DateTime.Now - rm.lastUsed) >= CooldownBetweenTheSameMessage)
      {
        SendCommandsResponse(metadata.UserName, metadata.MessageID);
        ResponseMessages["!commands"] = (rm.response, DateTime.Now);
      }
      else { Log.Warning("Not sending response for {command} key. Cooldown active.", "!commands"); }
    }
    else if (msg.StartsWith("!welcomemessageclear")) // Check if the message starts with !welcomemessageclear key
    {
      // Clear current welcome message
      if (chatter.WelcomeMessage?.Length > 0)
      {
        chatter.SetWelcomeMessage(null);
        AddMessageResponseToQueue($"@{metadata.UserName} welcome message was cleared", metadata.MessageID);
      }
      else { AddMessageResponseToQueue($"@{metadata.UserName} your welcome message is empty WeirdDude", metadata.MessageID); }
    }
    else if (msg.StartsWith("!welcomemessage")) // Check if the message starts with !welcomemessage key
    {
      string newMsg = msg[15..].Trim();
      if (newMsg.Length == 0)
      {
        // Print out current welcome message
        if (chatter.WelcomeMessage?.Length > 0) AddMessageResponseToQueue($"@{metadata.UserName} current welcome message: {chatter.WelcomeMessage}", metadata.MessageID);
        else AddMessageResponseToQueue($"@{metadata.UserName} your welcome message is empty peepoSad", metadata.MessageID);
      }
      else
      {
        // Set new welcome message
        chatter.SetWelcomeMessage(newMsg);
        AddMessageResponseToQueue($"@{metadata.UserName} welcome message was updated peepoHappy", metadata.MessageID);
      }
    }
    else if (msg.StartsWith("!sounds")) // Check if the message starts with !sounds key
    {
      if (Notifications.AreSoundsAvailable())
      {
        string paste = Notifications.GetSampleSoundsPaste();
        AddMessageResponseToQueue($"@{metadata.UserName} {paste}", metadata.MessageID);
      }
      else { AddMessageResponseToQueue($"@{metadata.UserName} there are no sounds to use peepoSad", metadata.MessageID); }
    }
    else if (msg.StartsWith("!previoussong")) // Check if the message starts with !previoussong key
    {
      if (Spotify.Working) { AddMessageResponseToQueue($"@{metadata.UserName} {Spotify.GetRecentlyPlayingTracks()}", metadata.MessageID); }
      else { AddMessageResponseToQueue($"@{metadata.UserName} the Spotify connection is not working peepoSad", metadata.MessageID); }
    }
    else if (msg.StartsWith("!songrequest")) // Check if the message starts with !songrequest key
    {
      SongRequest(chatter, msg[12..], metadata.MessageID);  // 12.. - without "!songrequest"
    }
    else if (msg.StartsWith("!sr")) // Check if the message starts with !sr key
    {
      SongRequest(chatter, msg[3..], metadata.MessageID); // 3.. - without "!sr"
    }
    else if (msg.StartsWith("!skipsong")) // Check if the message starts with !skipsong key
    {
      SkipSong(chatter);
    }
    else if (msg.StartsWith("!songqueue")) // Check if the message starts with !songqueue key
    {
      if (Spotify.Working) { AddMessageResponseToQueue($"@{metadata.UserName} {Spotify.GetSongQueue()}", metadata.MessageID); }
      else { AddMessageResponseToQueue($"@{metadata.UserName} the Spotify connection is not working peepoSad", metadata.MessageID); }
    }
    else if (msg.StartsWith("!song")) // Check if the message starts with !song key
    {
      if (Spotify.Working) { AddMessageResponseToQueue($"@{metadata.UserName} {Spotify.GetCurrentlyPlayingTrack()}", metadata.MessageID); }
      else { AddMessageResponseToQueue($"@{metadata.UserName} the Spotify connection is not working peepoSad", metadata.MessageID); }
    }
    else if (ResponseMessages.Count > 0) // Check if message starts with key to get automatic response
    {
      int temp = msg.IndexOf(' ');
      string command = temp > 0 ? msg[..temp] : msg;
      if (ResponseMessages.TryGetValue(command, out var rm))
      {
        // "!lang" response - "Please speak xxx in the chat...", only usable by the mods or the streamer
        if (command.Equals("!lang"))
        {
          if (metadata.Badge.Equals("MOD") || metadata.Badge.Equals("STR"))
          {
            if (temp > 0)
            {
              string name = msg[temp..].Trim();
              if (name.StartsWith('@')) name = name[1..];
              AddMessageToQueue($"@{name} {rm.response}");
            }
            else { AddMessageResponseToQueue($"@{metadata.UserName} {rm.response}", metadata.MessageID); }
          }
        }
        // Check if the same message was send not long ago
        else if (DateTime.Now - rm.lastUsed >= CooldownBetweenTheSameMessage)
        {
          AddMessageResponseToQueue($"@{metadata.UserName} {rm.response}", metadata.MessageID);
          ResponseMessages[command] = (rm.response, DateTime.Now);
        }
        else { Log.Warning("Not sending response for \"{command}\" key. Cooldown active.", command); }
      }
    }
  }
}

/// <summary> Chat message metadata. </summary>
class MessageMetadata
{
  /// <summary> Type of the chat message. </summary>
  public string MessageType = string.Empty;
  /// <summary> Badge of the chatter. </summary>
  public string Badge = string.Empty;
  /// <summary> Name of the chatter. </summary>
  public string UserName = string.Empty;
  /// <summary> Chatter ID. </summary>
  public long UserID = -1;
  /// <summary> Message ID. </summary>
  public string MessageID = string.Empty;
  /// <summary> Custom reward ID that created the chat message. </summary>
  public string CustromRewardID = string.Empty;
  /// <summary> Amount of bits. </summary>
  public string Bits = string.Empty;
  /// <summary> Type of special chat message (like "sub", "emote_only_on"). </summary>
  public string MsgID = string.Empty;

  /// <summary> Resets current metadata to default state. </summary>
  public void Clear()
  {
    MessageType = Badge = UserName = MessageID = CustromRewardID = Bits = MsgID = string.Empty;
    UserID = -1;
  }
}
