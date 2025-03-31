using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;

using Serilog;

namespace AbevBot;

public static class Chat
{
  /// <summary> Path to .csv file with response messages. </summary>
  private const string RESPONSE_MESSAGES_PATH = "Resources/ResponseMessages.csv";
  /// <summary> Maximum number of characters in one message. 500 characters Twitch limit, -40 characters as a buffer </summary>
  private const int MESSAGE_SENT_MAX_LEN = 460;
  /// <summary> Minimum time between messages sent. </summary>
  private static readonly TimeSpan MESSAGE_SEND_COOLDOWN = TimeSpan.FromMilliseconds(200);
  /// <summary> Minimum time between the same response message. </summary>
  private static readonly TimeSpan COOLDOWN_BETWEEN_THE_SAME_MESSAGE = new(0, 0, 10);

  /// <summary> Is chat bot started? </summary>
  public static bool IsStarted { get; private set; }
  /// <summary> Chat bot thread. </summary>
  private static readonly Thread ChatThread = new Thread(Update);
  /// <summary> Collection of response messages. </summary>
  public static readonly Dictionary<string, (string response, bool fromFile, DateTime lastUsed)> ResponseMessages = new();
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
  /// <summary> Print chat messages to stdout? </summary>
  public static bool PrintChatMessages { get; set; } = true;

  /// <summary> Starts the chat bot. </summary>
  public static void Start()
  {
    if (IsStarted) return;
    IsStarted = true;

    Log.Information("Starting chat bot.");
    LoadResponseMessages();
    if (!ResponseMessages.TryAdd("!commands", (string.Empty, false, DateTime.MinValue)))
    {
      Log.Warning("Couldn't add {command} response. Maybe something already declared it?", "!commands");
    }
    if (!ResponseMessages.TryAdd("!lang", ("Please speak English in the chat, thank you ❤", false, DateTime.MinValue)))
    {
      Log.Warning("Couldn't add {command} response. Maybe something already declared it?", "!lang");
    }

    ChatThread.Name = "Chat thread";
    ChatThread.IsBackground = true;
    ChatThread.Start();
  }

  private static void Update()
  {
    int sleepErrorDur = 5000;
    var receiveBuffer = new byte[16384]; // Max IRC message is 4096 bytes? let's allocate 4 times that, 2 times max message length wasn't enaugh for really fast chats
    var remainingData = new byte[16384];
    int remainingDataLen = 0, zeroBytesReceivedCounter;
    string header, body, msg;
    MessageMetadata metadata = new();
    DateTime lastMessageSent = DateTime.Now;
    var messageStart = Encoding.UTF8.GetBytes("@badge");
    var messageEnd = Encoding.UTF8.GetBytes("\r\n");

    while (true)
    {
      if (MainWindow.CloseRequested) { return; }

      // Try to connect
      Log.Information("Chat bot connecting...");

      var conn = new Socket(SocketType.Stream, ProtocolType.Tcp);
      try { conn.Connect("irc.chat.twitch.tv", 6667); }
      catch (Exception ex)
      {
        Log.Error("Chat bot connection error: {ex}", ex);
        Thread.Sleep(sleepErrorDur);
        continue;
      }

      // Connected! Send authentication data
      Log.Information("Chat bot connected.");
      try
      {
        if (Secret.Data[Secret.Keys.TwitchUseTwoAccounts] == "1" && Config.Data[Config.Keys.BotID].Length > 0 &&
          Secret.Data[Secret.Keys.TwitchSubOAuthToken].Length > 0)
        {
          conn.Send(Encoding.UTF8.GetBytes(string.Concat(
            $"PASS oauth:{Secret.Data[Secret.Keys.TwitchSubOAuthToken]}\r\n",
            $"NICK {Secret.Data[Secret.Keys.TwitchName]}\r\n",
            $"JOIN #{Config.Data[Config.Keys.ChannelName]},#{Config.Data[Config.Keys.ChannelName]}\r\n",
            "CAP REQ :twitch.tv/commands twitch.tv/tags\r\n" // request extended chat messages
          )));
        }
        else
        {
          conn.Send(Encoding.UTF8.GetBytes(string.Concat(
            $"PASS oauth:{Secret.Data[Secret.Keys.TwitchOAuthToken]}\r\n",
            $"NICK {Config.Data[Config.Keys.ChannelName]}\r\n",
            $"JOIN #{Config.Data[Config.Keys.ChannelName]},#{Config.Data[Config.Keys.ChannelName]}\r\n",
            "CAP REQ :twitch.tv/commands twitch.tv/tags\r\n" // request extended chat messages
          )));
        }
      }
      catch (SocketException ex)
      {
        Log.Error("Chat bot connection error: {ex}", ex);
        conn.Close();
        continue;
      }
      conn.ReceiveTimeout = 10; // This timeout slows down entire loop, so no additional sleep is necessary
      zeroBytesReceivedCounter = 0;

      // Update loop
      while (true)
      {
        if (MainWindow.CloseRequested) { return; }
        if (!conn.Connected)
        {
          Log.Error("Chat bot connection closed.");
          break;
        }

        // Receive messages
        int n = -1;
        try { n = conn.Receive(receiveBuffer); }
        catch (SocketException ex)
        {
          if (ex.ErrorCode == 10060) { } // Read timeout exceeded, nothing to do
          else
          {
            Log.Error("Chat bot connection error: {ex}", ex);
            break;
          }
        }

        if (n == 0)
        {
          zeroBytesReceivedCounter++;
          if (zeroBytesReceivedCounter > 5)
          {
            Log.Warning("Chat bot received 0 bytes multiple times, reconnecting!");
            break;
          }
        }
        else if (n > 0)
        {
          zeroBytesReceivedCounter = 0;
          // Check if received data starts with message start
          var newMessage = true;
          for (int i = 0; i < messageStart.Length; i++)
          {
            if (receiveBuffer[i] != messageStart[i])
            {
              newMessage = false;
              break;
            }
          }

          // Look through received data and look for message end
          int start = 0, end = 0, endIndex = 0;
          var endFound = false;
          for (int i = 0; i < n; i++)
          {
            if (receiveBuffer[i] == messageEnd[endIndex])
            {
              endIndex++;
              if (endIndex >= messageEnd.Length) { endFound = true; }
            }
            else { endIndex = 0; }

            if (endFound)
            {
              i++;
              endFound = false;
              endIndex = 0;
              end = i;

              if ((end - start) <= 2) { }// Just an empty "\r\n", skip
              else if (newMessage)
              {
                // Is there left over data? Just try to parse it?
                if (remainingDataLen > 0)
                {
                  (header, body, msg) = ParseMessage(remainingData[..remainingDataLen], ref metadata);
                  ProcessMessage(header, body, msg, ref metadata);
                  remainingDataLen = 0;
                }

                // Parse new message
                (header, body, msg) = ParseMessage(receiveBuffer[start..end], ref metadata);
                ProcessMessage(header, body, msg, ref metadata);
              }
              else
              {
                // Append start of a message to left over data and parse it
                for (int k = 0; k < end; k++) { remainingData[remainingDataLen + k] = receiveBuffer[k]; }
                (header, body, msg) = ParseMessage(remainingData[..(remainingDataLen + end)], ref metadata);
                ProcessMessage(header, body, msg, ref metadata);
                remainingDataLen = 0;
              }

              newMessage = true; // After parsing at least 1 message, parse the rest like new messages
              start = end;
            }
          }

          if (end < n)
          {
            // Data is missing message end
            remainingDataLen = n - end;
            for (int i = 0; i < remainingDataLen; i++) { remainingData[i] = receiveBuffer[end + i]; }
          }
        }

        // Send messages
        if (MessageQueue.Count > 0 && DateTime.Now - lastMessageSent >= MESSAGE_SEND_COOLDOWN)
        {
          lock (MessageQueue)
          {
            try { conn.Send(Encoding.UTF8.GetBytes(MessageQueue[0])); }
            catch (SocketException ex)
            {
              Log.Error("Chat bot connection error: {ex}", ex);
              break;
            }
            MessageQueue.RemoveAt(0);
            lastMessageSent = DateTime.Now;
          }
        }

        // Periodic messages
        if ((PeriodicMessages.Count > 0) && (ChatMessagesSinceLastPeriodicMessage >= PeriodicMessageMinChatMessages) && (DateTime.Now - LastPeriodicMessage >= PeriodicMessageInterval))
        {
          ChatMessagesSinceLastPeriodicMessage = 0;
          LastPeriodicMessage = DateTime.Now;
          PeriodicMessageIndex = (PeriodicMessageIndex + 1) % PeriodicMessages.Count;
          AddMessageToQueue(PeriodicMessages[PeriodicMessageIndex]);
        }
      }

      conn.Close();
      Thread.Sleep(sleepErrorDur);
    }
  }

  /// <summary> Loads response messages from the file. </summary>
  /// <param name="reload">Is the file being reloaded?</param>
  public static void LoadResponseMessages(bool reload = false)
  {
    if (reload) { Log.Information("Reloading response messages."); }
    else { Log.Information("Loading response messages."); }

    var messagesFile = new FileInfo(RESPONSE_MESSAGES_PATH);
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

    // Cleanup old response messages
    foreach (var r in ResponseMessages)
    {
      if (r.Value.fromFile) { ResponseMessages.Remove(r.Key); }
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
          if (ResponseMessages.ContainsKey(text[0])) { ResponseMessages[text[0]] = (text[1].Trim(), true, DateTime.MinValue); }
          else { ResponseMessages.Add(text[0], (text[1].Trim(), true, DateTime.MinValue)); }
          responseCount++;
        }
        else if (ResponseMessages.TryAdd(text[0], (text[1].Trim(), true, new DateTime())))
        {
          Log.Information("Added respoonse to \"{key}\" key.", text[0]);
          responseCount++;
        }
        else { Log.Warning("Redefiniton of \"{key}\" key in line {index}.", text[0], lineIndex); } // TryAdd returned false - probably a duplicate
      }
    }

    Log.Information("Loaded {count} automated response messages.", responseCount);

    RespMsgFileTimestamp = messagesFile.LastWriteTime;
  }

  /// <summary> Checks if response messages file was updated. </summary>
  /// <returns><value>true</value> if the file was updated, otherwise <value>false</value></returns>
  public static bool IsRespMsgFileUpdated()
  {
    FileInfo file = new(RESPONSE_MESSAGES_PATH);
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
    AddMessageResponseToQueue(message, string.Empty);
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
    int end;

    lock (MessageQueue)
    {
      do
      {
        // Find message end or place to split the message
        end = message.Length;
        if ((end - start) > MESSAGE_SENT_MAX_LEN)
        {
          int idx = message.LastIndexOf(' ', start + MESSAGE_SENT_MAX_LEN);
          end = idx == -1 ? MESSAGE_SENT_MAX_LEN : idx;
        }

        // Create the message
        sb.Clear();
        if (messageID?.Length > 0)
        {
          sb.Append("@reply-parent-msg-id=");
          sb.Append(messageID);
          sb.Append(' ');
        }
        sb.Append("PRIVMSG #");
        sb.Append(Config.Data[Config.Keys.ChannelName]);
        sb.Append(" :");
        sb.Append(message[start..end]);
        sb.Append("\r\n");
        MessageQueue.Add(sb.ToString());

        start = end;
      } while (end < message.Length);
    }
  }

  /// <summary> Adds multiple chat messages to the queue. </summary>
  /// <param name="messages">Messages to be sent</param>
  public static void AddMessagesToQueue(List<string> messages)
  {
    if (!IsStarted) return;

    var sb = new StringBuilder();
    var channelName = Config.Data[Config.Keys.ChannelName];

    lock (MessageQueue)
    {
      foreach (var msg in messages)
      {
        if (msg is null || msg.Length == 0 || msg.Length > MESSAGE_SENT_MAX_LEN) { continue; } // Skip

        sb.Clear();
        sb.Append("PRIVMSG #");
        sb.Append(channelName);
        sb.Append(" :");
        sb.Append(msg);
        sb.Append("\r\n");
        MessageQueue.Add(sb.ToString());
      }
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
    request.Headers.Add("Authorization", $"Bearer {Secret.Data[Secret.Keys.TwitchOAuthToken]}");
    request.Headers.Add("Client-Id", Secret.Data[Secret.Keys.TwitchClientID]);

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
    if (chatter.Name == "testFromUser") return; // Debug chatter

    Log.Information("Banning {userName} from chat for {duration} seconds. {msg}", chatter.Name, durSeconds, message);

    string uri = $"https://api.twitch.tv/helix/moderation/bans?broadcaster_id={Config.Data[Config.Keys.ChannelID]}&moderator_id={Config.Data[Config.Keys.ChannelID]}";
    using HttpRequestMessage request = new(HttpMethod.Post, uri);
    request.Content = new StringContent(new BanMessageRequest(chatter.ID, durSeconds, message).ToJsonString(), Encoding.UTF8, "application/json");
    request.Headers.Add("Authorization", $"Bearer {Secret.Data[Secret.Keys.TwitchOAuthToken]}");
    request.Headers.Add("Client-Id", Secret.Data[Secret.Keys.TwitchClientID]);

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
    // sb.Append('@').Append(userName).Append(' ');
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
    if (Counter.IsStarted) { sb.Append("!counter, "); }

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
    request.Headers.Add("Authorization", $"Bearer {Secret.Data[Secret.Keys.TwitchOAuthToken]}");
    request.Headers.Add("Client-Id", Secret.Data[Secret.Keys.TwitchClientID]);

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
      AddMessageResponseToQueue("Spotify connection is not working peepoSad", messageID);
      return;
    }
    else if (!Spotify.RequestEnabled && !fromNotifications)
    {
      AddMessageResponseToQueue("Song requests are disabled peepoSad", messageID);
      return;
    }
    else if (message is null || message.Length == 0)
    {
      AddMessageResponseToQueue("Maybe provide a link to the song? WeirdDude", messageID);
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
          "Wait ",
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
      AddMessageResponseToQueue("The link is not recognized, only spotify links are supported", messageID);
      return;
    }
    index = message.IndexOf("/track/");
    if (index < 0)
    {
      AddMessageResponseToQueue("The link is not recognized, only spotify links are supported", messageID);
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
      AddMessageResponseToQueue("The link is not recognized, only spotify links are supported", messageID);
      return;
    }

    if (Spotify.AddTrackToQueue(uri, chatter.Name))
    {
      if (!fromNotifications) chatter.LastSongRequest = DateTime.Now;
      AddMessageResponseToQueue("Track added to the queue peepoHappy", messageID);
    }
    else { AddMessageResponseToQueue("Something went wrong when adding the track to the queue peepoSad", messageID); }
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
    if (!handled)
    {
      SkipSongChatters.Add(new() { ChatterID = chatter.ID, TimeRequested = DateTime.Now });
      Log.Information("{userName} requested song skip!", chatter.Name);
    }

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
  /// <param name="data">Raw chat message</param>
  /// <param name="metadata">Metadata of the message to be updated</param>
  /// <returns>Tuple of the header, body parts and the whole message</returns>
  private static (string header, string body, string msg) ParseMessage(byte[] data, ref MessageMetadata metadata)
  {
    metadata.Clear();
    string header = string.Empty, body = string.Empty;
    int temp, temp2, temp3;
    var msg = Encoding.UTF8.GetString(data);
    msg = msg.TrimEnd(new char[] { '\r', '\n' });

    if (msg.StartsWith("PING"))
    {
      header = msg[..];
      return (header, body, msg);
    }

    // Find header <-> body "separator"
    temp = msg.IndexOf("tmi.twitch.tv");
    if (temp < 0)
    {
      Log.Error("Chat message not parsed correctly\n{msg}", msg);
      return (header, body, msg);
    }

    // Get message header
    header = msg[..temp];
    temp += 14; // 14 == "tmi.twitch.tv ".len()

    // Get message type
    temp2 = msg.IndexOf(' ', temp);
    if (temp2 < 0) { temp2 = msg.Length; }
    metadata.MessageType = msg[temp..temp2];
    temp2++;

    // Get message body
    temp = msg.IndexOf(':', temp2);
    if (temp > 0) { body = msg[(temp + 1)..]; }

    // Get header data
    temp = 0;  // start
    temp2 = 0; // end
    temp3 = 0; // '=' index
    for (int i = 0; i < header.Length; i++)
    {
      char v = header[i];
      if (v == ';' || v == ' ') { temp2 = i; }
      else if (v == '=') { temp3 = i + 1; }

      if (temp2 > temp)
      {
        var s = header[temp3..temp2];
        switch (header[temp..(temp3 - 1)])
        {
          case "id":
            metadata.MessageID = s;
            break;
          case "badges":
            if (s.StartsWith("broadcaster")) { metadata.Badge = "STR"; }
            else if (s.StartsWith("moderator")) { metadata.Badge = "MOD"; }
            else if (s.StartsWith("subscriber")) { metadata.Badge = "SUB"; }
            else if (s.StartsWith("vip")) { metadata.Badge = "VIP"; }
            break;
          case "display-name":
            metadata.UserName = s;
            break;
          case "user-id":
            if (long.TryParse(s, out var val)) { metadata.UserID = val; }
            break;
          case "custom-reward-id":
            metadata.CustomRewardID = s;
            break;
          case "bits":
            metadata.Bits = s;
            break;
          case "@msg-id":
          case "msg-id":
            metadata.MsgID = s;
            break;
          case "msg-param-recipient-display-name":
            metadata.Receipent = s;
            break;
          case "msg-param-sub-plan":
            metadata.Sub.Tier = s.ToLower() switch
            {
              "1000" => "1",
              "2000" => "2",
              "3000" => "3",
              _ => s
            };
            break;
          case "msg-param-multimonth-duration":
            metadata.Sub.DurationInAdvance = s;
            break;
          case "msg-param-streak-months":
            metadata.Sub.Streak = s;
            break;
          case "msg-param-cumulative-months":
            metadata.Sub.CumulativeMonths = s;
            break;
        }
        temp = temp2 + 1;

        temp3 = temp;
      }
    }
    return (header, body, msg);
  }

  private static void ProcessMessage(string header, string body, string msg, ref MessageMetadata metadata)
  {
    // PING - keepalive message
    if (header.StartsWith("PING"))
    {
      lock (MessageQueue)
      {
        MessageQueue.Add("PONG :tmi.twitch.tv\r\n");
      }
      return;
    }

    switch (metadata.MessageType)
    {
      case "PRIVMSG":
        if (metadata.CustomRewardID.Length > 0)
        {
          Log.Information("{userName} redeemed custom reward with ID: {customRewardID}. {msg}",
            metadata.UserName,
            metadata.CustomRewardID,
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
          if (PrintChatMessages)
          {
            MainWindow.ConsoleWriteLine(string.Format(
              "{0, -4}{1, 22}{2, 2}{3, -0}",
              metadata.Badge,
              metadata.UserName,
              ": ",
              body));
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
            Notifications.CreateMaybeSubscriptionNotification(metadata.UserName,
              metadata.Sub.Tier, metadata.Sub.DurationInAdvance, metadata.Sub.Streak, metadata.Sub.CumulativeMonths,
              body);
            Events.LogEventToFile(msg); // I need few sub messages to test it out
            break;
          case "resub":
            Log.Information("{userName} resubscribed! {msg}",
              metadata.UserName,
              body);
            Notifications.CreateMaybeSubscriptionNotification(metadata.UserName,
              metadata.Sub.Tier, metadata.Sub.DurationInAdvance, metadata.Sub.Streak, metadata.Sub.CumulativeMonths,
              body);
            Events.LogEventToFile(msg); // I need few sub messages to test it out
            break;
          case "subgift":
            Log.Information("{userName} gifted sub to {userName2}! {msg}",
              metadata.UserName,
              metadata.Receipent,
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
            Notifications.CreateMaybeSubscriptionNotification(metadata.UserName,
              metadata.Sub.Tier, metadata.Sub.DurationInAdvance, metadata.Sub.Streak, metadata.Sub.CumulativeMonths,
              body);
            Events.LogEventToFile(msg); // I need few sub messages to test it out
            break;
          case "giftpaidupgrade":
            Log.Information("{userName} continuing sub gifted by another chatter! {msg}",
              metadata.UserName,
              body);
            Notifications.CreateMaybeSubscriptionNotification(metadata.UserName,
              metadata.Sub.Tier, metadata.Sub.DurationInAdvance, metadata.Sub.Streak, metadata.Sub.CumulativeMonths,
              body);
            Events.LogEventToFile(msg); // I need few sub messages to test it out
            break;
          case "communitypayforward":
            Log.Information("{userName} is paying forward sub gifted by another chatter! {msg}",
              metadata.UserName,
              body);
            Notifications.CreateMaybeSubscriptionNotification(metadata.UserName,
              metadata.Sub.Tier, metadata.Sub.DurationInAdvance, metadata.Sub.Streak, metadata.Sub.CumulativeMonths,
              body);
            Events.LogEventToFile(msg); // I need few sub messages to test it out
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
          int temp = msg.LastIndexOf(':');
          temp = temp > 0 ? temp + 1 : msg.Length;
          Log.Information("{userName} got banned!", msg[temp..]);
        }
        else if (body.Length > 0) { Log.Information("{userName} chat messages got cleared", body); }
        else { Log.Information("Chat got cleared"); }
        break;

      case "CLEARMSG":
        if (msg.StartsWith("@login="))
        {
          int temp = msg.IndexOf(';');
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
        if (PrintChatMessages) { MainWindow.ConsoleWriteLine($"> Bot message from {metadata.UserName}"); }
        break;

      default:
        // Not recognized message
        MainWindow.ConsoleWriteLine(msg);
        break;
    }
  }

  /// <summary> Checks chat message for commands. </summary>
  /// <param name="metadata">Metadata of the chat message</param>
  /// <param name="msg">Message body to be checked</param>
  static void CheckForChatCommands(ref MessageMetadata metadata, string msg)
  {
    var chatter = Chatter.GetChatterByID(metadata.UserID, metadata.UserName);
    chatter.LastChatted = DateTime.Now;
    if (Notifications.WelcomeMessagesEnabled && Config.BroadcasterOnline && chatter.WelcomeMessage?.Length > 0 && chatter.LastWelcomeMessage.Date != DateTime.Now.Date)
    {
      Log.Information("Creating {userName} welcome message TTS.", metadata.UserName);
      chatter.SetLastWelcomeMessageToNow();
      Notifications.CreateTTSNotification(chatter.WelcomeMessage, chatter.Name);
    }

    if (msg.StartsWith("!tts")) // Check if the message starts with !tts key
    {
      if (msg.Length <= 5) { Log.Warning("!tts command without a message Susge"); } // No message to read, do nothing
      else if (Notifications.ChatTTSEnabled || Chatter.AlwaysReadTTSFromThem.Contains(metadata.UserName)) { Notifications.CreateTTSNotification(msg[5..], chatter.Name); } // 5.. - without "!tts "
      else { AddMessageResponseToQueue("TTS disabled peepoSad", metadata.MessageID); }
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
      else AddMessageResponseToQueue("That's for the streamer, you shouldn't be using it Madge", metadata.MessageID);
    }
    else if (msg.StartsWith("!unpoint")) // Check if the message starts with !unpoint key
    {
      if (metadata.Badge.Equals("STR")) MinigameBackseat.AddBackseatPoint(msg[8..], -1); // 8.. - without "!unpoint"
      else AddMessageResponseToQueue("That's for the streamer, you shouldn't be using it Madge", metadata.MessageID);
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
      if (ResponseMessages.TryGetValue("!commands", out var rm) && (DateTime.Now - rm.lastUsed) >= COOLDOWN_BETWEEN_THE_SAME_MESSAGE)
      {
        SendCommandsResponse(metadata.UserName, metadata.MessageID);
        ResponseMessages["!commands"] = (rm.response, rm.fromFile, DateTime.Now);
      }
      else { Log.Warning("Not sending response for {command} key. Cooldown active.", "!commands"); }
    }
    else if (msg.StartsWith("!welcomemessageclear")) // Check if the message starts with !welcomemessageclear key
    {
      // Clear current welcome message
      if (chatter.WelcomeMessage?.Length > 0)
      {
        chatter.SetWelcomeMessage(null);
        AddMessageResponseToQueue("Welcome message was cleared", metadata.MessageID);
      }
      else { AddMessageResponseToQueue("Your welcome message is empty WeirdDude", metadata.MessageID); }
    }
    else if (msg.StartsWith("!welcomemessage")) // Check if the message starts with !welcomemessage key
    {
      string newMsg = msg[15..].Trim();
      if (newMsg.Length == 0)
      {
        // Print out current welcome message
        if (chatter.WelcomeMessage?.Length > 0) AddMessageResponseToQueue($"Current welcome message: {chatter.WelcomeMessage}", metadata.MessageID);
        else AddMessageResponseToQueue("Your welcome message is empty peepoSad", metadata.MessageID);
      }
      else
      {
        // Set new welcome message
        chatter.SetWelcomeMessage(newMsg);
        AddMessageResponseToQueue("Welcome message was updated peepoHappy", metadata.MessageID);
      }
    }
    else if (msg.StartsWith("!sounds")) // Check if the message starts with !sounds key
    {
      if (Notifications.AreSoundsAvailable())
      {
        string paste = Notifications.GetSampleSoundsPaste();
        AddMessageResponseToQueue(paste, metadata.MessageID);
      }
      else { AddMessageResponseToQueue("There are no sounds to use peepoSad", metadata.MessageID); }
    }
    else if (msg.StartsWith("!previoussong")) // Check if the message starts with !previoussong key
    {
      if (Spotify.Working) { AddMessageResponseToQueue(Spotify.GetRecentlyPlayingTracks(), metadata.MessageID); }
      else { AddMessageResponseToQueue("Spotify connection is not working peepoSad", metadata.MessageID); }
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
      if (Spotify.Working) { AddMessageResponseToQueue(Spotify.GetSongQueue(), metadata.MessageID); }
      else { AddMessageResponseToQueue("Spotify connection is not working peepoSad", metadata.MessageID); }
    }
    else if (msg.StartsWith("!song")) // Check if the message starts with !song key
    {
      if (Spotify.Working) { AddMessageResponseToQueue(Spotify.GetCurrentlyPlayingTrack(), metadata.MessageID); }
      else { AddMessageResponseToQueue("Spotify connection is not working peepoSad", metadata.MessageID); }
    }
    else if (msg.StartsWith("!counter")) // Check if message starts with !counter key
    {
      if (Counter.IsStarted)
      {
        if (metadata.Badge == "STR" || metadata.Badge == "MOD") { Counter.ParseChatMessage(msg[8..].Trim(), metadata.MessageID); }
        else { AddMessageResponseToQueue("Only the streamer or mods can update counters!", metadata.MessageID); }
      }
      else { AddMessageResponseToQueue("Counters are not working peepoSad", metadata.MessageID); }
    }
    else if (msg.StartsWith("!friendly"))
    {
      AddMessageResponseToQueue("PepeLaugh", metadata.MessageID);
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
            else { AddMessageResponseToQueue(rm.response, metadata.MessageID); }
          }
        }
        // Check if the same message was send not long ago
        else if (DateTime.Now - rm.lastUsed >= COOLDOWN_BETWEEN_THE_SAME_MESSAGE)
        {
          AddMessageResponseToQueue(rm.response, metadata.MessageID);
          ResponseMessages[command] = (rm.response, rm.fromFile, DateTime.Now);
        }
        else { Log.Warning("Not sending response for \"{command}\" key. Cooldown active.", command); }
      }
    }
  }

  public static void TestMessageSend()
  {
    // Sending messages via new Twitch API
    string token, id;
    if (Secret.Data[Secret.Keys.TwitchUseTwoAccounts] == "1")
    {
      token = Secret.Data[Secret.Keys.TwitchSubOAuthToken];
      id = Config.Data[Config.Keys.BotID];
    }
    else
    {
      token = Secret.Data[Secret.Keys.TwitchOAuthToken];
      id = Config.Data[Config.Keys.ChannelID];
    }

    using HttpRequestMessage request = new(HttpMethod.Post, "https://api.twitch.tv/helix/chat/messages");
    request.Headers.Add("Authorization", $"Bearer {token}");
    request.Headers.Add("Client-Id", Secret.Data[Secret.Keys.TwitchClientID]);
    using StringContent requestContent = new(new JsonObject() {
      { "broadcaster_id", Config.Data[Config.Keys.ChannelID] },
      { "sender_id", id },
      { "message", string.Concat("Current time: ", DateTime.Now.ToString("t")) }
      }.ToString(), Encoding.UTF8, "application/json");
    request.Content = requestContent;
    var response = Notifications.Client.Send(request).Content.ReadAsStringAsync().Result;
    Console.WriteLine(response);
  }
}

/// <summary> Chat message metadata. </summary>
class MessageMetadata
{
  /// <summary> Chatter ID. </summary>
  public long UserID = -1;
  /// <summary> Type of the chat message. </summary>
  public string MessageType = string.Empty;
  /// <summary> Badge of the chatter. </summary>
  public string Badge = string.Empty;
  /// <summary> Name of the chatter. </summary>
  public string UserName = string.Empty;
  /// <summary> Message ID. </summary>
  public string MessageID = string.Empty;
  /// <summary> Custom reward ID that created the chat message. </summary>
  public string CustomRewardID = string.Empty;
  /// <summary> Amount of bits. </summary>
  public string Bits = string.Empty;
  /// <summary> Type of special chat message (like "sub", "emote_only_on"). </summary>
  public string MsgID = string.Empty;
  public string Receipent = string.Empty;
  /// <summary> Information about sub message received in chat. </summary>
  public (string Tier, string DurationInAdvance, string Streak, string CumulativeMonths) Sub = (string.Empty, string.Empty, string.Empty, string.Empty);

  /// <summary> Resets current metadata to default state. </summary>
  public void Clear()
  {
    MessageType = Badge = UserName = MessageID = CustomRewardID = Bits = MsgID = Receipent = string.Empty;
    UserID = -1;

    Sub.Tier = Sub.DurationInAdvance = Sub.Streak = Sub.CumulativeMonths = string.Empty;
  }
}
