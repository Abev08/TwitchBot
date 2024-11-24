using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

using Serilog;

namespace AbevBot
{
  public static class Notifications
  {
    /// <summary> Path to random videos directory. </summary>
    public const string RANDOM_VIDEO_PATH = "Resources/Videos";
    /// <summary> Path to directory with sound samples. </summary>
    public const string SOUNDS_PATH = "Resources/Sounds";
    /// <summary> Notifications thread started. </summary>
    public static bool Started { get; private set; }
    /// <summary> Are notifications paused? </summary>
    public static bool NotificationsPaused { get; set; }
    /// <summary> Is "stop follow bots" method active? </summary>
    public static bool StopFollowBotsActive { get; set; }
    /// <summary> Is notification pause active from "stop follow bots" method? </summary>
    public static bool StopFollowBotsPause { get; set; }
    public static bool SkipNotification { get; set; }
    public static bool ChatTTSEnabled { get; set; }
    public static bool WelcomeMessagesEnabled { get; set; }
    /// <summary> Queue of notifiactions. </summary>
    public static readonly List<Notification> NotificationQueue = new();
    /// <summary> Collection of past notifications.
    /// When notification ends it should be added to this collection and cleared from it after some time. </summary>
    static readonly List<Notification> PastNotifications = new();
    /// <summary> Collection of maybe notifications - notifications that come from different sources than events.
    /// After some time (if correct event message doesn't appear) notifications from this list are moved to correct notification queue. </summary>
    static readonly List<Notification> MaybeNotificationQueue = new();
    private static Thread NotificationsThread;
    public static readonly HttpClient Client = new();
    public static string VoicesLink { get; private set; }
    private static readonly Dictionary<string, FileInfo> SampleSounds = new();
    /// <summary> Recently played list of random videos. </summary>
    private static readonly List<string> RandomVideosRecentlyPlayed = new();
    private static bool SoundsAvailable;
    private static string s_soundsSamplePasteLink;
    /// <summary> Supported video formats by WPF MediaElement. </summary>
    public static readonly string[] SupportedVideoFormats = new[] { ".avi", ".gif", ".mkv", ".mov", ".mp4", ".wmv" };
    public static readonly string[] SupportedAudioFormats = new[] { ".wav", ".mp3", ".ogg" };
    public static readonly string[] SupportedImageFormats = new[] { ".png", ".jpg", ".bmp" };

    /// <summary> Configurations of different notification types. </summary>
    public static readonly Dictionary<string, NotificationsConfig> Configs = new()
    {
      {"Follow", new(NotificationType.FOLLOW)},
      {"Subscription", new(NotificationType.SUBSCRIPTION)},
      {"SubscriptionExt", new(NotificationType.SUBSCRIPTION)},
      {"SubscriptionGift", new(NotificationType.SUBSCRIPTIONGIFT)},
      {"SubscriptionGiftReceived", new(NotificationType.SUBSCRIPTION)},
      {"Cheer", new(NotificationType.CHEER)},
      {"CheerRange", new(NotificationType.CHEERRANGE)},
      {"Raid", new(NotificationType.RAID)},
      {"Timeout", new(NotificationType.TIMEOUT)},
      {"Ban", new(NotificationType.BAN)},
      {"OnScreenCelebration", new(NotificationType.ONSCREENCELEBRATION)},
      {"MessageEffect", new(NotificationType.MESSAGEEFFECT)},
      {"GigantifyEmote", new(NotificationType.GIGANTIFYEMOTE)},
      {"Other", new(NotificationType.OTHER)}
    };
    private static readonly string[] NotificationData = new string[14];
    public static readonly List<ChannelRedemption> ChannelRedemptions = new();
    private static readonly List<(DateTime time, string name)> GiftedSubs = new();
    private static readonly TimeSpan GiftSubMaxTimeout = new(0, 0, 10);
    public static VideoParameters RandomVideoParameters;
    public static readonly List<NotificationsConfig> CheerRangeNotifications = new();

    public enum TextPosition { TOPLEFT, TOP, TOPRIGHT, LEFT, CENTER, RIGHT, BOTTOMLEFT, BOTTOM, BOTTOMRIGHT, VIDEOABOVE, VIDEOCENTER, VIDEOBELOW }

    public static void Start()
    {
      if (Started) return;
      Started = true;
      Log.Information("Starting notifications thread.");

      CreateVoicesPaste();
      // CreateVoicesResponse();

      NotificationsThread = new Thread(Update)
      {
        Name = "Notifications Thread",
        IsBackground = true
      };
      NotificationsThread.Start();
    }

    private static void Update()
    {
      bool notificationEnded = false;
      while (true)
      {
        if (MainWindow.CloseRequested) { return; }

        // Clean up past notifications, keep only 20 newest
        if (PastNotifications.Count > 20)
        {
          MainWindow.I.Dispatcher.Invoke(new Action(() =>
          {
            lock (PastNotifications)
            {
              while (PastNotifications.Count > 20)
              {
                var n = PastNotifications[0];
                MainWindow.I.UpdatePastNotificationQueue(n, true);
                PastNotifications.RemoveAt(0);
                n = null;
              }
            }
          }));
        }

        // During "stop follow bots" don't update notifications
        if (StopFollowBotsPause)
        {
          Thread.Sleep(500);
          continue;
        }

        // Check maybe notifications
        if (MaybeNotificationQueue.Count > 0)
        {
          if (DateTime.Now >= MaybeNotificationQueue[0].StartAfter)
          {
            lock (MaybeNotificationQueue)
            {
              lock (NotificationQueue)
              {
                // Maybe notification should start - move it to the real queue
                var n = MaybeNotificationQueue[0];
                NotificationQueue.Add(n);
                MaybeNotificationQueue.RemoveAt(0);
                MainWindow.I.UpdateNotificationQueue(NotificationQueue.Count, MaybeNotificationQueue.Count, n, false);
              }
            }
          }
          else if (NotificationQueue.Count == 0 && SkipNotification)
          {
            // No real notifications to skip - skip queued maybe notification
            lock (MaybeNotificationQueue)
            {
              SkipNotification = false;
              MaybeNotificationQueue.RemoveAt(0);
              MainWindow.I.SetNotificationQueueCount(NotificationQueue.Count, MaybeNotificationQueue.Count);
            }
          }
        }

        if ((NotificationQueue.Count > 0) && (!NotificationsPaused || NotificationQueue[0].Started || NotificationQueue[0].NotPausable))
        {
          lock (NotificationQueue)
          {
            if (NotificationQueue[0].Started == false)
            {
              SkipNotification = false;
              NotificationQueue[0].Start();
              NotificationQueue[0].UpdateControl();
            }
            if (NotificationQueue[0].Update())
            {
              SkipNotification = false;
              // Update returned true == notificaion has ended, remove it from queue
              var n = NotificationQueue[0];
              NotificationQueue.RemoveAt(0);
              MainWindow.I.UpdateNotificationQueue(NotificationQueue.Count, MaybeNotificationQueue.Count, n, true);
              AddPastNotification(n);
              notificationEnded = true;
            }
          }

          if (notificationEnded)
          {
            if (NotificationQueue.Count > 0) Thread.Sleep(500); // 500 ms delay between notifications
            notificationEnded = false;
          }
          else { Thread.Sleep(10); } // Slow down the loop while notification is playing
        }
        else
        {
          Thread.Sleep(500); // Check for new notification every 500 ms
        }
      }
    }

    /// <summary> Adds notification to the queue. </summary>
    public static void AddNotification(Notification notification)
    {
      Task.Run(() =>
      {
        lock (NotificationQueue)
        {
          // Check if maybe notification was created with the same data
          lock (MaybeNotificationQueue)
          {
            for (int i = MaybeNotificationQueue.Count - 1; i >= 0; i--)
            {
              var n = MaybeNotificationQueue[i];
              if (n.Type == notification.Type && n.Sender == notification.Sender)
              {
                // Notification type and sender name is the same - it has to be the same notification?
                MaybeNotificationQueue.RemoveAt(i);
                break;
              }
            }
          }

          NotificationQueue.Add(notification);
          MainWindow.I.UpdateNotificationQueue(NotificationQueue.Count, MaybeNotificationQueue.Count, notification, false);
        }
      });
    }

    /// <summary> Adds notification to the queue. </summary>
    public static void AddMaybeNotification(Notification notification, double startAfter = 3)
    {
      Task.Run(() =>
      {
        // Check previous / current notifications - maybe the notification was or already is being played
        lock (NotificationQueue)
        {
          for (int i = 0; i < NotificationQueue.Count; i++)
          {
            var n = NotificationQueue[i];
            // Notification type and sender name is the same - it has to be the same notification?
            if (n.Type == notification.Type && n.Sender == notification.Sender) { return; }
          }
          lock (PastNotifications)
          {
            for (int i = 0; i < PastNotifications.Count; i++)
            {
              var n = PastNotifications[i];
              // Notification type and sender name is the same - it has to be the same notification?
              if (n.Type == notification.Type && n.Sender == notification.Sender) { return; }
            }
          }
        }

        lock (MaybeNotificationQueue)
        {
          notification.StartAfter = DateTime.Now.AddSeconds(startAfter); // Lets give it 5 sec, maybe propper event message will come?
          MaybeNotificationQueue.Add(notification);
          MainWindow.I.SetNotificationQueueCount(NotificationQueue.Count, MaybeNotificationQueue.Count);
        }
      });
    }

    /// <summary> Creates and adds to queue Follow notification. </summary>
    public static void CreateFollowNotification(string userName)
    {
      var config = Configs["Follow"];
      if (!config.Enable) return;

      string chatter;
      if (string.IsNullOrWhiteSpace(userName)) chatter = "Anonymous";
      else chatter = userName?.Trim();

      Array.Clear(NotificationData);
      NotificationData[0] = chatter;

      Chat.AddMessageToQueue(string.Format(config.ChatMessage, NotificationData));
      AddNotification(new Notification(config, NotificationData));
    }

    /// <summary> Creates and adds to queue Subscription notification. </summary>
    public static void CreateSubscriptionNotification(string userName, string tier, string message)
    {
      var config = Configs["Subscription"];
      if (!config.Enable) return;

      string chatter;
      if (string.IsNullOrWhiteSpace(userName)) chatter = "Anonymous";
      else chatter = userName?.Trim();

      Array.Clear(NotificationData);
      NotificationData[0] = chatter;
      NotificationData[1] = tier[..1];
      NotificationData[7] = message;

      Chat.AddMessageToQueue(string.Format(config.ChatMessage, NotificationData));
      AddNotification(new Notification(config, NotificationData));
    }

    /// <summary> This is more advanced CreateSubscriptionNotification version - more info in message variable. </summary>
    public static void CreateSubscriptionNotification(string userName, string tier, int duration, int streak, int cumulative, EventPayloadMessage message)
    {
      var config = Configs["SubscriptionExt"];
      if (!config.Enable) return;

      string chatter;
      if (string.IsNullOrWhiteSpace(userName)) chatter = "Anonymous";
      else chatter = userName?.Trim();

      Array.Clear(NotificationData);
      NotificationData[0] = chatter;
      NotificationData[1] = tier[..1];
      NotificationData[2] = duration.ToString();
      NotificationData[3] = streak.ToString();
      NotificationData[5] = cumulative.ToString();
      NotificationData[7] = message.Text; // TODO: Create message to read - remove emotes from the message using message.Emotes[], don't read them
      NotificationData[10] = duration > 1 ? "s" : string.Empty;
      NotificationData[11] = streak > 1 ? "s" : string.Empty;
      NotificationData[13] = cumulative > 1 ? "s" : string.Empty;

      Chat.AddMessageToQueue(string.Format(config.ChatMessage, NotificationData));
      AddNotification(new Notification(config, NotificationData));
    }

    /// <summary> This is more advanced CreateSubscriptionNotification version - more info in message variable. </summary>
    public static void CreateSubscriptionNotification(string userName, string tier, int duration, int streak, int cumulative, JsonNode message)
    {
      var config = Configs["SubscriptionExt"];
      if (!config.Enable) return;

      string chatter;
      if (string.IsNullOrWhiteSpace(userName)) chatter = "Anonymous";
      else chatter = userName?.Trim();

      Array.Clear(NotificationData);
      NotificationData[0] = chatter;
      NotificationData[1] = tier[..1];
      NotificationData[2] = duration.ToString();
      NotificationData[3] = streak.ToString();
      NotificationData[5] = cumulative.ToString();
      NotificationData[7] = message["text"]?.ToString(); // TODO: Create message to read - remove emotes from the message using message.Emotes[], don't read them
      NotificationData[10] = duration > 1 ? "s" : string.Empty;
      NotificationData[11] = streak > 1 ? "s" : string.Empty;
      NotificationData[13] = cumulative > 1 ? "s" : string.Empty;

      Chat.AddMessageToQueue(string.Format(config.ChatMessage, NotificationData));
      AddNotification(new Notification(config, NotificationData));
    }

    /// <summary> Creates and adds to queue Received Gifted Subscription notification. </summary>
    public static void CreateGiftSubscriptionNotification(string userName, string tier, int count, string message, string timeStamp)
    {
      var config = Configs["SubscriptionGift"];
      if (!config.Enable) return;

      // Check the gift sub list
      if (DateTime.TryParse(timeStamp, out DateTime time))
      {
        for (int i = GiftedSubs.Count - 1; i >= 0; i--)
        {
          if (GiftedSubs[i].time - time >= GiftSubMaxTimeout)
          {
            GiftedSubs.RemoveAt(i);
          }
        }
      }

      StringBuilder sb = new();
      if (GiftedSubs.Count > 0)
      {
        for (int i = 0; i < GiftedSubs.Count; i++)
        {
          sb.Append(GiftedSubs[i].name);
          if (i != GiftedSubs.Count - 1) sb.Append(", ");
        }
      }

      string chatter;
      if (string.IsNullOrWhiteSpace(userName)) chatter = "Anonymous";
      else chatter = userName?.Trim();

      Array.Clear(NotificationData);
      NotificationData[0] = chatter;
      NotificationData[1] = tier[..1];
      NotificationData[4] = count.ToString();
      NotificationData[6] = sb.ToString();
      NotificationData[7] = message;
      NotificationData[12] = count > 1 ? "s" : string.Empty;

      Chat.AddMessageToQueue(string.Format(config.ChatMessage, NotificationData));
      AddNotification(new Notification(config, NotificationData));

      GiftedSubs.Clear();
    }

    /// <summary> Creates and adds to queue Gifted Subscription notification. </summary>
    public static void CreateReceiveGiftSubscriptionNotification(string userName, string timeStamp)
    {
      var config = Configs["SubscriptionGiftReceived"];
      // Add gifted sub to the list
      if (userName?.Length > 0 && DateTime.TryParse(timeStamp, out DateTime time))
      {
        GiftedSubs.Add((time, userName));
      }

      if (!config.Enable) return;

      string chatter;
      if (string.IsNullOrWhiteSpace(userName)) chatter = "Anonymous";
      else chatter = userName?.Trim();

      Array.Clear(NotificationData);
      NotificationData[0] = chatter;

      Chat.AddMessageToQueue(string.Format(config.ChatMessage, NotificationData));
      AddNotification(new Notification(config, NotificationData));
    }

    /// <summary> Creates and adds to queue Cheer notification. </summary>
    public static void CreateCheerNotification(string userName, int count, string message)
    {
      // Notifications.CheerRangeNotifications
      NotificationsConfig config = null;
      foreach (var c in CheerRangeNotifications)
      {
        if (!c.Enable) { continue; }
        if (count >= c.BitsRange[0] && count <= c.BitsRange[1])
        {
          config = c;
          break;
        }
      }
      if (config is null) { config = Configs["Cheer"]; }
      if (!config.Enable) return;

      string chatter;
      if (string.IsNullOrWhiteSpace(userName)) chatter = "Anonymous";
      else chatter = userName?.Trim();

      Array.Clear(NotificationData);
      NotificationData[0] = chatter;
      NotificationData[4] = count.ToString();
      NotificationData[7] = message;
      NotificationData[12] = count > 1 ? "s" : string.Empty;

      Chat.AddMessageToQueue(string.Format(config.ChatMessage, NotificationData));
      AddNotification(new Notification(config, NotificationData));
    }

    /// <summary> Creates and adds to queue Channel Points Redemption notification. </summary>
    public static void CreateRedemptionNotificaiton(string userName, string redemptionID, string messageID, string message)
    {
      string msgID = messageID;
      var chatter = Chatter.GetChatterByName(userName);
      var chatterName = "Anonymous";
      if (chatter != null && !string.IsNullOrEmpty(chatter.Name)) { chatterName = chatter.Name; }
      if (Config.Data[Config.Keys.ChannelRedemption_RandomVideo_ID].Equals(redemptionID)) { CreateRandomVideoNotification(msgID, chatterName); }
      else if (Config.Data[Config.Keys.ChannelRedemption_SongRequest_ID].Equals(redemptionID))
      {
        Chat.SongRequest(chatter, message, null, true);
        if (Config.Data[Config.Keys.ChannelRedemption_SongRequest_MarkAsFulfilled].Equals("True")) { MarkRedemptionAsFulfilled(redemptionID, msgID); }
      }
      else if (Config.Data[Config.Keys.ChannelRedemption_SongSkip_ID].Equals(redemptionID))
      {
        Spotify.SkipSong();
        if (Config.Data[Config.Keys.ChannelRedemption_SongSkip_MarkAsFulfilled].Equals("True")) { MarkRedemptionAsFulfilled(redemptionID, msgID); }
      }
      else
      {
        // Look through channel redemptions list
        foreach (var redemption in ChannelRedemptions)
        {
          if (redemption.ID.Equals(redemptionID))
          {
            // Create notification
            Array.Clear(NotificationData);
            NotificationData[0] = chatterName;

            Chat.AddMessageToQueue(redemption.Config.ChatMessage);
            AddNotification(new Notification(redemption.Config, NotificationData, redemption)
            {
              ExtraActionAtStartup = () => { if (redemption.MarkAsFulfilled) MarkRedemptionAsFulfilled(redemption.ID, msgID); }
            });
            break;
          }
        }
      }
    }

    /// <summary> Creates and adds to queue TTS notification (mainly for chat messages). </summary>
    public static void CreateTTSNotification(string text, string userName)
    {
      if (string.IsNullOrWhiteSpace(text)) return;

      var n = new Notification()
      {
        Type = NotificationType.OTHER,
        SubType = "TTS",
        Sender = userName,
        TextToRead = text.Replace("#", ""), // Remove '#' symbols - they are not allowed in TTS request messages
      };
      n.UpdateControl();
      AddNotification(n);
    }

    public static void CreateRaidNotification(string userName, string userID, int count)
    {
      var config = Configs["Raid"];
      if (!config.Enable) return;
      if (count < config.MinimumRaiders) return;

      string chatter;
      if (string.IsNullOrWhiteSpace(userName)) chatter = "Anonymous";
      else chatter = userName?.Trim();

      Array.Clear(NotificationData);
      NotificationData[0] = chatter;
      NotificationData[4] = count.ToString();
      NotificationData[12] = count > 1 ? "s" : string.Empty;

      Chat.AddMessageToQueue(string.Format(config.ChatMessage, NotificationData));
      if (config.DoShoutout) Chat.Shoutout(userID);
      AddNotification(new Notification(config, NotificationData));
    }

    /// <summary> Creates and adds to queue Random Video notification. </summary>
    public static void CreateRandomVideoNotification(string messageID, string sender)
    {
      var video = GetRandomVideoNotPlayedRecently();
      if (video is null || video.Length == 0) return;
      string msgID = messageID;

      var n = new Notification()
      {
        Type = NotificationType.OTHER,
        SubType = "Random video",
        Sender = sender,
        VideoPath = video,
        VideoParams = RandomVideoParameters,

        ExtraActionAtStartup = () =>
        {
          if (Config.Data[Config.Keys.ChannelRedemption_RandomVideo_MarkAsFulfilled].Equals("True"))
          {
            MarkRedemptionAsFulfilled(Config.Data[Config.Keys.ChannelRedemption_RandomVideo_ID], msgID);
          }
        }
      };
      n.UpdateControl();
      AddNotification(n);
    }

    /// <summary> Creates and adds to queue Chatter Timeout notification. </summary>
    /// <param name="userName">Chatter name that got the timeout</param>
    /// <param name="duration">Timeout duration</param>
    /// <param name="reason">Timeout reason</param>
    public static void CreateTimeoutNotification(string userName, TimeSpan duration, string reason)
    {
      if (StopFollowBotsActive) { return; } // While stop follow bots is active, don't play these notifications
      var config = Configs["Timeout"];
      if (!config.Enable) { return; }
      if (duration < config.MinimumTime) { return; }

      Array.Clear(NotificationData);
      NotificationData[0] = userName;
      NotificationData[7] = reason;

      Chat.AddMessageToQueue(string.Format(config.ChatMessage, NotificationData));
      AddNotification(new Notification(config, NotificationData));
    }

    /// <summary> Creates and adds to queue Chatter ban notification. </summary>
    /// <param name="userName">Chatter name that got banned</param>
    /// <param name="reason">Ban reason</param>
    public static void CreateBanNotification(string userName, string reason)
    {
      if (StopFollowBotsActive) { return; } // While stop follow bots is active, don't play these notifications
      var config = Configs["Ban"];
      if (!config.Enable) { return; }

      Array.Clear(NotificationData);
      NotificationData[0] = userName;
      NotificationData[7] = reason;

      Chat.AddMessageToQueue(string.Format(config.ChatMessage, NotificationData));
      AddNotification(new Notification(config, NotificationData));
    }

    /// <summary> Creates and adds to queue Subscription notification. </summary>
    public static void CreateMaybeSubscriptionNotification(string userName, string tier, string durationInAdvance, string streak, string cumulativeMonths, string message)
    {
      var config = Configs["SubscriptionExt"];
      if (!config.Enable) return;

      string chatter;
      if (string.IsNullOrWhiteSpace(userName)) chatter = "Anonymous";
      else chatter = userName?.Trim();

      int temp;
      Array.Clear(NotificationData);
      NotificationData[0] = chatter;
      NotificationData[1] = tier;
      NotificationData[2] = durationInAdvance;
      NotificationData[3] = streak;
      NotificationData[5] = cumulativeMonths;
      NotificationData[7] = message;
      if (int.TryParse(durationInAdvance, out temp) && temp > 1) { NotificationData[10] = "s"; }
      if (int.TryParse(streak, out temp) && temp > 1) { NotificationData[11] = "s"; }
      if (int.TryParse(cumulativeMonths, out temp) && temp > 1) { NotificationData[13] = "s"; }

      // Chat.AddMessageToQueue(string.Format(config.ChatMessage, NotificationData));
      AddMaybeNotification(new Notification(config, NotificationData));
    }

    /// <summary> Creates and adds to queue on screen celebration notification. </summary>
    /// <param name="userName">Chatter name that fired up a celebration</param>
    /// <param name="msg">Attached message</param>
    public static void CreateOnScreenCelebrationNotification(string userName, string msg)
    {
      var config = Configs["OnScreenCelebration"];
      if (!config.Enable) return;

      Array.Clear(NotificationData);
      NotificationData[0] = userName;
      NotificationData[7] = msg;

      Chat.AddMessageToQueue(string.Format(config.ChatMessage, NotificationData));
      AddNotification(new Notification(config, NotificationData));
    }

    /// <summary> Creates and adds to queue chat message effect notification. </summary>
    /// <param name="userName">Chatter name that fired up the event</param>
    /// <param name="msg">Attached message</param>
    public static void CreateMessageEffectNotification(string userName, string msg)
    {
      var config = Configs["MessageEffect"];
      if (!config.Enable) return;

      Array.Clear(NotificationData);
      NotificationData[0] = userName;
      NotificationData[7] = msg;

      Chat.AddMessageToQueue(string.Format(config.ChatMessage, NotificationData));
      AddNotification(new Notification(config, NotificationData));
    }

    /// <summary> Creates and adds to queue chat gigantify emote effect notification. </summary>
    /// <param name="userName">Chatter name that fired up the event</param>
    /// <param name="msg">Attached message</param>
    public static void CreateGigantifyEmoteNotification(string userName, string msg)
    {
      var config = Configs["GigantifyEmote"];
      if (!config.Enable) return;

      Array.Clear(NotificationData);
      NotificationData[0] = userName;
      NotificationData[7] = msg;

      Chat.AddMessageToQueue(string.Format(config.ChatMessage, NotificationData));
      AddNotification(new Notification(config, NotificationData));
    }

    private static void CreateVoicesPaste()
    {
      if (Chat.ResponseMessages.ContainsKey("!voices"))
      {
        Log.Warning("Couldn't add respoonse to \"{key}\" key - the key is already present in response messages.", "!voices");
        return;
      }

      StringBuilder sb = new();
      sb.AppendLine("StreamElements voices:");
      StreamElements.AppendVoices(ref sb, 70);
      sb.AppendLine();
      sb.AppendLine();
      sb.AppendLine("TikTok voices:");
      TikTok.AppendVoices(ref sb, 70);

      GlotPaste paste = new() { Language = "assembly", Title = "TTS Voices" };
      paste.Files.Add(new GlotFile() { Name = "TTS Voices", Content = sb.ToString() });

      using HttpRequestMessage request = new(HttpMethod.Post, "https://glot.io/api/snippets");
      request.Content = new StringContent(paste.ToJsonString(), Encoding.UTF8, "application/json");

      string resp;
      try { resp = Client.Send(request).Content.ReadAsStringAsync().Result; }
      catch (HttpRequestException ex) { Log.Error("{key} response creation failed. {ex}", "!voices", ex); return; }
      var response = GlotResponse.Deserialize(resp);
      if (response?.Url?.Length > 0)
      {
        VoicesLink = response.Url.Replace("api/", ""); // Remove "api/" part
      }

      Chat.ResponseMessages.Add("!voices", ($"TTS Voices: {VoicesLink}", false, new DateTime()));
      Log.Information("Added respoonse to \"{key}\" key.", "!voices");
    }

    private static void CreateVoicesResponse()
    {
      // Printing all available voices is too much for IRC chat (it would be few messages with word limit)
      // For now we can response with links to StreamElements and TikTok classes on GitHub.
      // Instead of links to GitHub CreateVoicesPaste() method can be used to generate paste link

      if (!Chat.ResponseMessages.ContainsKey("!voices"))
      {
        Chat.ResponseMessages.Add("!voices", (string.Concat(
          "TTS Voices: ",
          "StreamElements: ", "https://github.com/Abev08/TwitchBot/blob/main/StreamElements.cs ",
          "TikTok: ", "https://github.com/Abev08/TwitchBot/blob/main/TikTok.cs"
        ), false, new DateTime()));
        Log.Information("Added respoonse to \"{key}\" key.", "!voices");
      }
      else
      {
        Log.Warning("Couldn't add respoonse to \"{key}\" key - the key is already present in response messages.", "!voices");
      }
    }

    /// <summary> Removes all follow notifications from the queue. </summary>
    public static void CleanFollowNotifications()
    {
      lock (NotificationQueue)
      {
        MainWindow.I.Dispatcher.Invoke(new Action(() =>
        {
          for (int i = NotificationQueue.Count - 1; i >= 0; i--)
          {
            if (NotificationQueue[i].Type == NotificationType.FOLLOW)
            {
              var n = NotificationQueue[i];
              n.Stop(); // Stop the notification if it's currently playing
              NotificationQueue.RemoveAt(i);
              MainWindow.I.UpdateNotificationQueue(NotificationQueue.Count, MaybeNotificationQueue.Count, n, true);
              n = null;
            }
          }
        }));
      }
    }

    public static Dictionary<string, FileInfo> GetSampleSounds()
    {
      // Get all sound files
      Dictionary<string, FileInfo> sounds = new();
      DirectoryInfo dir = new(SOUNDS_PATH);
      if (dir.Exists)
      {
        foreach (var file in dir.GetFiles())
        {
          if (Array.IndexOf(SupportedAudioFormats, file.Extension) >= 0)
          {
            sounds.Add(file.Name.Replace(file.Extension, "").ToLower(), file);
          }
        }
      }

      lock (SampleSounds)
      {
        bool newSounds = false;
        // Check if new sounds are available
        // comparasion is needed to know when to update !sounds response paste link
        if (sounds.Count != SampleSounds.Count) { newSounds = true; }
        else
        {
          // Check file by file
          foreach (var sound in sounds)
          {
            if (!SampleSounds.TryGetValue(sound.Key, out var val))
            {
              newSounds = true;
              break;
            }
            else
            {
              if (sound.Value.Length != val.Length)
              {
                newSounds = true;
                break;
              }
            }
          }
        }

        if (newSounds)
        {
          SampleSounds.Clear();
          foreach (var sound in sounds) { SampleSounds.Add(sound.Key, sound.Value); }
          SoundsAvailable = SampleSounds.Count > 0;
          s_soundsSamplePasteLink = null;
        }
      }

      return SampleSounds;
    }

    public static string GetSampleSoundsResponse()
    {
      StringBuilder sb = new();
      var samples = GetSampleSounds();
      foreach (var sample in samples)
      {
        sb.Append(sample.Key).Append(", ");
      }

      string result = sb.ToString().Trim();
      if (result.EndsWith(',')) return result[..^1];
      return result;
    }

    /// <summary> Creates paste link with sound samples. </summary>
    /// <returns>Link to the paste.</returns>
    public static string GetSampleSoundsPaste()
    {
      var samples = GetSampleSounds();

      if (s_soundsSamplePasteLink?.Length > 0) return s_soundsSamplePasteLink;

      int lineLength = 0;
      StringBuilder sb = new();
      sb.AppendLine("Sound samples that can be used in TTS messages like: \"!tts -fart\".");
      sb.AppendLine("Every sound sample can also be used with \"up\" or \"down\" prefix.");
      foreach (var sample in samples)
      {
        sb.Append(sample.Key).Append(", ");
        lineLength += sample.Key.Length + 2;
        if (lineLength > 70)
        {
          sb.AppendLine();
          lineLength = 0;
        }
      }

      string result = sb.ToString().Trim();
      if (result.EndsWith(',')) result = result[..^1];

      GlotPaste paste = new() { Language = "assembly", Title = "Sound samples" };
      paste.Files.Add(new GlotFile() { Name = "Sound samples", Content = result });

      using HttpRequestMessage request = new(HttpMethod.Post, "https://glot.io/api/snippets");
      request.Content = new StringContent(paste.ToJsonString(), Encoding.UTF8, "application/json");

      string resp;
      try { resp = Client.Send(request).Content.ReadAsStringAsync().Result; }
      catch (HttpRequestException ex) { Log.Error("{key} response creation failed. {ex}", "!sounds", ex); return string.Empty; }
      var response = GlotResponse.Deserialize(resp);
      if (response?.Url?.Length > 0)
      {
        s_soundsSamplePasteLink = response.Url.Replace("api/", ""); // Remove "api/" part
        Log.Information("Created respoonse link to \"{key}\" key.", "!sounds");
        return s_soundsSamplePasteLink;
      }
      else
      {
        return "something went wrong peepoSad";
      }
    }

    public static bool AreSoundsAvailable()
    {
      if (!SoundsAvailable) GetSampleSounds(); // try to load the sounds

      return SoundsAvailable;
    }

    /// <summary> Gets all of the random videos in RANDOM_VIDEO_PATH directory and Discord channel. </summary>
    /// <returns>List of paths to random videos</returns>
    public static List<string> GetRandomVideosAll()
    {
      // Load random videos
      var videos = new List<string>();
      DirectoryInfo dir = new(RANDOM_VIDEO_PATH);
      if (dir.Exists)
      {
        foreach (FileInfo file in dir.GetFiles())
        {
          if (Array.IndexOf(SupportedVideoFormats, file.Extension) >= 0)
          {
            videos.Add(string.Concat(RANDOM_VIDEO_PATH, "\\", file.Name));
          }
        }
      }

      // Get random videos in discord channel
      var discordVideos = Discord.GetRandomVideos();
      videos.AddRange(discordVideos);

      return videos;
    }

    /// <summary> Gets random videos not played recently. </summary>
    /// <returns>List of FileInfo objects describing random videos</returns>
    public static List<string> GetRandomVideosToPlay()
    {
      var videosAll = GetRandomVideosAll();
      var videosToPlay = new List<string>();
      videosToPlay.AddRange(videosAll);

      foreach (var v in RandomVideosRecentlyPlayed)
      {
        for (int i = videosToPlay.Count - 1; i >= 0; i--)
        {
          if (v == videosToPlay[i])
          {
            videosToPlay.RemoveAt(i);
            break;
          }
        }
      }

      if (videosToPlay.Count == 0)
      {
        videosToPlay.AddRange(videosAll);
        RandomVideosRecentlyPlayed.Clear();
      }

      return videosToPlay;
    }

    /// <summary> Gets random video from a pool of videos not played recently. </summary>
    /// <returns>Path to random video</returns>
    public static string GetRandomVideoNotPlayedRecently()
    {
      var videos = GetRandomVideosToPlay();
      int index = Random.Shared.Next(videos.Count);
      if (index < 0 || videos.Count == 0) return null;

      var video = videos[index];
      RandomVideosRecentlyPlayed.Add(video);
      videos.RemoveAt(index);
      return video;
    }

    /// <summary> Marks provided redemption from provided message ID as fulfilled. </summary>
    /// <param name="redemptionID">Twitch redemption ID.</param>
    /// <param name="messageID">Twitch message ID that created the redemption.</param>
    public static void MarkRedemptionAsFulfilled(string redemptionID, string messageID)
    {
      string uri = string.Concat(
        "https://api.twitch.tv/helix/channel_points/custom_rewards/redemptions",
        "?broadcaster_id=", Config.Data[Config.Keys.ChannelID],
        "&reward_id=", redemptionID,
        "&id=", messageID
      );
      using HttpRequestMessage request = new(HttpMethod.Patch, uri);
      request.Content = new StringContent("""{ "status": "FULFILLED" }""", Encoding.UTF8, "application/json");
      request.Headers.Add("Authorization", $"Bearer {Secret.Data[Secret.Keys.TwitchOAuthToken]}");
      request.Headers.Add("Client-Id", Secret.Data[Secret.Keys.TwitchClientID]);

      string resp;
      try { resp = Client.Send(request).Content.ReadAsStringAsync().Result; }      // Assume that it worked
      catch (HttpRequestException ex) { Log.Error("Marking redemption as fulfilled failed. {ex}", ex); }
    }

    /// <summary> Activates shield mode. </summary>
    public static void ActivateShieldMode()
    {
      string uri = string.Concat(
        "https://api.twitch.tv/helix/moderation/shield_mode",
        "?broadcaster_id=", Config.Data[Config.Keys.ChannelID],
        "&moderator_id=", Config.Data[Config.Keys.ChannelID]
      );
      using HttpRequestMessage request = new(HttpMethod.Put, uri);
      request.Content = new StringContent("""{"is_active":true}""", Encoding.UTF8, "application/json");
      request.Headers.Add("Authorization", $"Bearer {Secret.Data[Secret.Keys.TwitchOAuthToken]}");
      request.Headers.Add("Client-Id", Secret.Data[Secret.Keys.TwitchClientID]);

      try
      {
        var resp = Client.Send(request);
        if ((int)resp.StatusCode >= 200 && (int)resp.StatusCode < 300) { } // OK
        else { Log.Error("Activating shield mode failed. Received error code: {code} ({status})", (int)resp.StatusCode, resp.StatusCode); }
      }
      catch (HttpRequestException ex) { Log.Error("Activating shield mode failed. {ex}", ex); }
    }

    public static void MoveToTop(Notification notif)
    {
      if (notif is null) { return; }
      lock (NotificationQueue)
      {
        notif.NotPausable = true;

        // If the queue count is 1, I assume that provided notification is the only one in the queue
        if (NotificationQueue.Count == 1) { return; }

        // Find the notification
        for (int i = NotificationQueue.Count - 1; i >= 0; i--)
        {
          if (NotificationQueue[i] == notif)
          {
            NotificationQueue.RemoveAt(i);
            if (NotificationQueue[0].Started)
            {
              // The first one is already playing, put the provided one after it
              if (NotificationQueue.Count == 1) { NotificationQueue.Add(notif); }
              else { NotificationQueue.Insert(1, notif); }
            }
            else
            {
              NotificationQueue.Insert(0, notif);
            }
            break;
          }
        }

        MainWindow.I.RecreateNotificationQueue();
      }
    }

    public static void ReplayNotification(Notification notif)
    {
      notif.Reset();
      lock (PastNotifications)
      {
        PastNotifications.Remove(notif);
        MainWindow.I.UpdatePastNotificationQueue(notif, true);
      }
      AddNotification(notif);
    }

    public static void Skip(Notification notif)
    {
      if (notif is null) { return; }
      lock (NotificationQueue)
      {
        if (NotificationQueue[0] == notif)
        {
          if (NotificationQueue[0].Started) { SkipNotification = true; }
          else
          {
            NotificationQueue.RemoveAt(0);
            MainWindow.I.UpdateNotificationQueue(NotificationQueue.Count, MaybeNotificationQueue.Count, notif, true);
          }

          return;
        }

        for (int i = NotificationQueue.Count - 1; i >= 0; i--)
        {
          if (NotificationQueue[i] == notif)
          {
            NotificationQueue.RemoveAt(i);
            MainWindow.I.UpdateNotificationQueue(NotificationQueue.Count, MaybeNotificationQueue.Count, notif, true);
            break;
          }
        }
      }
    }

    public static void AddPastNotification(Notification notif)
    {
      lock (PastNotifications)
      {
        notif.UpdateControl();
        PastNotifications.Add(notif);
        MainWindow.I.UpdatePastNotificationQueue(notif, false);
      }
    }
  }

  public enum NotificationType
  {
    FOLLOW,
    SUBSCRIPTION, SUBSCRIPTIONGIFT,
    CHEER, CHEERRANGE,
    RAID,
    REDEMPTION,
    TIMEOUT, BAN,
    ONSCREENCELEBRATION, MESSAGEEFFECT, GIGANTIFYEMOTE,
    OTHER
  }

  public class NotificationsConfig
  {
    public bool Enable { get; set; } = false;
    public string ChatMessage { get; set; } = string.Empty;
    public string TextToDisplay { get; set; } = string.Empty;
    public Notifications.TextPosition TextPosition { get; set; } = Notifications.TextPosition.TOP;
    public double TextSize { get; set; }
    public string TextToSpeech { get; set; } = string.Empty;
    public string SoundToPlay { get; set; } = string.Empty;
    public string VideoToPlay { get; set; } = string.Empty;
    public VideoParameters VideoParams { get; set; }
    public int MinimumRaiders { get; set; } = 10;
    public int MinimumBits { get; set; } = 10;
    public bool DoShoutout { get; set; }
    public NotificationType Type { get; }
    public TimeSpan MinimumTime { get; set; } = TimeSpan.MinValue;
    public int[] BitsRange { get; set; } = new int[] { 0, 0 };

    public NotificationsConfig(NotificationType type) { Type = type; }

    public void Reset()
    {
      Enable = false;
      ChatMessage = string.Empty;
      TextToDisplay = string.Empty;
      TextPosition = Notifications.TextPosition.TOP;
      TextSize = 48;
      TextToSpeech = string.Empty;
      SoundToPlay = string.Empty;
      VideoToPlay = string.Empty;
      VideoParams?.Reset();
      MinimumRaiders = 10;
      MinimumBits = 10;
      DoShoutout = false;
      MinimumTime = TimeSpan.MinValue;
    }
  }

  public class ChannelRedemption
  {
    public string ID { get; set; }
    public NotificationsConfig Config { get; set; } = new(NotificationType.REDEMPTION);
    public List<Key> KeysToPress { get; set; } = new();
    public KeyActionType KeysToPressType { get; set; } = KeyActionType.PRESS;
    public TimeSpan TimeToPressSecondAction { get; set; } = new TimeSpan();
    public List<Key> KeysToPressAfterTime { get; set; } = new();
    public KeyActionType KeysToPressAfterTimeType { get; set; } = KeyActionType.PRESS;
    public bool MarkAsFulfilled { get; set; }
  }

  public enum KeyActionType { PRESS, TYPE }
}
