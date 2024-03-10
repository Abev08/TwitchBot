using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace AbevBot
{
  public static class Notifications
  {
    /// <summary> Notifications thread started. </summary>
    public static bool Started { get; private set; }
    public static bool NotificationsPaused { get; set; }
    public static bool SkipNotification { get; set; }
    public static bool ChatTTSEnabled { get; set; }
    public static bool WelcomeMessagesEnabled { get; set; }
    static readonly List<Notification> NotificationQueue = new();
    private static Thread NotificationsThread;
    public static readonly HttpClient Client = new();
    public static string VoicesLink { get; private set; }
    private static readonly Dictionary<string, FileInfo> SampleSounds = new();
    private static readonly List<FileInfo> RandomVideos = new();
    private static bool SoundsAvailable;
    private static string s_soundsSamplePasteLink;
    /// <summary> Supported video formats by WPF MediaElement. </summary>
    private static readonly string[] SupportedVideoFormats = new[] { ".avi", ".gif", ".mkv", ".mov", ".mp4", ".wmv" };

    public static NotificationsConfig ConfigFollow { get; set; } = new(NotificationType.FOLLOW);
    public static NotificationsConfig ConfigSubscription { get; set; } = new(NotificationType.SUBSCRIPTION);
    public static NotificationsConfig ConfigSubscriptionExt { get; set; } = new(NotificationType.SUBSCRIPTION);
    public static NotificationsConfig ConfigSubscriptionGift { get; set; } = new(NotificationType.SUBSCRIPTIONGIFT);
    public static NotificationsConfig ConfigSubscriptionGiftReceived { get; set; } = new(NotificationType.SUBSCRIPTION);
    public static NotificationsConfig ConfigCheer { get; set; } = new(NotificationType.CHEER);
    public static NotificationsConfig ConfigRaid { get; set; } = new(NotificationType.RAID);
    public static NotificationsConfig ConfigTimeout { get; set; } = new(NotificationType.TIMEOUT);
    public static NotificationsConfig ConfigBan { get; set; } = new(NotificationType.BAN);
    private static readonly string[] NotificationData = new string[14];
    public static readonly List<ChannelRedemption> ChannelRedemptions = new();
    private static readonly List<(DateTime time, string name)> GiftedSubs = new();
    private static readonly TimeSpan GiftSubMaxTimeout = new(0, 0, 10);
    public static VideoParameters RandomVideoParameters;

    public enum TextPosition { TOPLEFT, TOP, TOPRIGHT, LEFT, CENTER, RIGHT, BOTTOMLEFT, BOTTOM, BOTTOMRIGHT, VIDEOABOVE, VIDEOCENTER, VIDEOBELOW }

    public static void Start()
    {
      if (Started) return;
      Started = true;
      MainWindow.ConsoleWarning(">> Starting notifications thread.");

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
        if ((NotificationQueue.Count > 0) && (!NotificationsPaused || NotificationQueue[0].Started))
        {
          lock (NotificationQueue)
          {
            if (NotificationQueue[0].Started == false)
            {
              SkipNotification = false;
              NotificationQueue[0].Start();
            }
            if (NotificationQueue[0].Update())
            {
              // Update returned true == notificaion has ended, remove it from queue
              NotificationQueue.RemoveAt(0);
              notificationEnded = true;
              MainWindow.I.SetNotificationQueueCount(NotificationQueue.Count);
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
          NotificationQueue.Add(notification);
          MainWindow.I.SetNotificationQueueCount(NotificationQueue.Count);
        }
      });
    }

    /// <summary> Creates and adds to queue Follow notification. </summary>
    public static void CreateFollowNotification(string userName)
    {
      if (!ConfigFollow.Enable) return;

      string chatter;
      if (string.IsNullOrWhiteSpace(userName)) chatter = "Anonymous";
      else chatter = userName?.Trim();

      Array.Clear(NotificationData);
      NotificationData[0] = chatter;

      Chat.AddMessageToQueue(string.Format(ConfigFollow.ChatMessage, NotificationData));
      AddNotification(new Notification(ConfigFollow, NotificationData));
    }

    /// <summary> Creates and adds to queue Subscription notification. </summary>
    public static void CreateSubscriptionNotification(string userName, string tier, string message)
    {
      if (!ConfigSubscription.Enable) return;

      string chatter;
      if (string.IsNullOrWhiteSpace(userName)) chatter = "Anonymous";
      else chatter = userName?.Trim();

      Array.Clear(NotificationData);
      NotificationData[0] = chatter;
      NotificationData[1] = tier[..1];
      NotificationData[7] = message;

      Chat.AddMessageToQueue(string.Format(ConfigSubscription.ChatMessage, NotificationData));
      AddNotification(new Notification(ConfigSubscription, NotificationData));
    }

    /// <summary> This is more advanced CreateSubscriptionNotification version - more info in message variable. </summary>
    public static void CreateSubscriptionNotification(string userName, string tier, int duration, int streak, int cumulative, EventPayloadMessage message)
    {
      if (!ConfigSubscriptionExt.Enable) return;

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

      Chat.AddMessageToQueue(string.Format(ConfigSubscriptionExt.ChatMessage, NotificationData));
      AddNotification(new Notification(ConfigSubscriptionExt, NotificationData));
    }

    /// <summary> Creates and adds to queue Received Gifted Subscription notification. </summary>
    public static void CreateGiftSubscriptionNotification(string userName, string tier, int count, string message, string timeStamp)
    {
      if (!ConfigSubscriptionGift.Enable) return;

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

      Chat.AddMessageToQueue(string.Format(ConfigSubscriptionGift.ChatMessage, NotificationData));
      AddNotification(new Notification(ConfigSubscriptionGift, NotificationData));

      GiftedSubs.Clear();
    }

    /// <summary> Creates and adds to queue Gifted Subscription notification. </summary>
    public static void CreateReceiveGiftSubscriptionNotification(string userName, string timeStamp)
    {
      // Add gifted sub to the list
      if (userName?.Length > 0 && DateTime.TryParse(timeStamp, out DateTime time))
      {
        GiftedSubs.Add((time, userName));
      }

      if (!ConfigSubscriptionGiftReceived.Enable) return;

      string chatter;
      if (string.IsNullOrWhiteSpace(userName)) chatter = "Anonymous";
      else chatter = userName?.Trim();

      Array.Clear(NotificationData);
      NotificationData[0] = chatter;

      Chat.AddMessageToQueue(string.Format(ConfigSubscriptionGiftReceived.ChatMessage, NotificationData));
      AddNotification(new Notification(ConfigSubscriptionGiftReceived, NotificationData));
    }

    /// <summary> Creates and adds to queue Cheer notification. </summary>
    public static void CreateCheerNotification(string userName, int count, string message)
    {
      if (!ConfigCheer.Enable) return;

      string chatter;
      if (string.IsNullOrWhiteSpace(userName)) chatter = "Anonymous";
      else chatter = userName?.Trim();

      Array.Clear(NotificationData);
      NotificationData[0] = chatter;
      NotificationData[4] = count.ToString();
      NotificationData[7] = message;
      NotificationData[12] = count > 1 ? "s" : string.Empty;

      Chat.AddMessageToQueue(string.Format(ConfigCheer.ChatMessage, NotificationData));
      AddNotification(new Notification(ConfigCheer, NotificationData));
    }

    /// <summary> Creates and adds to queue Channel Points Redemption notification. </summary>
    public static void CreateRedemptionNotificaiton(string userName, string redemptionID, string messageID, string message)
    {
      string msgID = messageID;
      if (Config.Data[Config.Keys.ChannelRedemption_RandomVideo_ID].Equals(redemptionID)) { CreateRandomVideoNotification(msgID); }
      else if (Config.Data[Config.Keys.ChannelRedemption_SongRequest_ID].Equals(redemptionID))
      {
        Chat.SongRequest(Chatter.GetChatterByName(userName), message, true);
        if (Config.Data[Config.Keys.ChannelRedemption_SongRequest_MarkAsFulfilled].Equals("True")) MarkRedemptionAsFulfilled(redemptionID, msgID);
      }
      else if (Config.Data[Config.Keys.ChannelRedemption_SongSkip_ID].Equals(redemptionID))
      {
        Spotify.SkipSong();
        if (Config.Data[Config.Keys.ChannelRedemption_SongSkip_MarkAsFulfilled].Equals("True")) MarkRedemptionAsFulfilled(redemptionID, msgID);
      }
      else
      {
        // Look throught channel redemptions list
        foreach (var redemption in ChannelRedemptions)
        {
          if (redemption.ID.Equals(redemptionID))
          {
            // Create notification
            Array.Clear(NotificationData);

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
    public static void CreateTTSNotification(string text)
    {
      if (string.IsNullOrWhiteSpace(text)) return;

      AddNotification(new Notification()
      {
        TextToRead = text.Replace("#", ""), // Remove '#' symbols - they are not allowed in TTS request messages
        TTSVolume = Config.VolumeTTS
      });
    }

    public static void CreateRaidNotification(string userName, string userID, int count)
    {
      if (!ConfigRaid.Enable) return;
      if (count < ConfigRaid.MinimumRaiders) return;

      string chatter;
      if (string.IsNullOrWhiteSpace(userName)) chatter = "Anonymous";
      else chatter = userName?.Trim();

      Array.Clear(NotificationData);
      NotificationData[0] = chatter;
      NotificationData[4] = count.ToString();
      NotificationData[12] = count > 1 ? "s" : string.Empty;

      Chat.AddMessageToQueue(string.Format(ConfigRaid.ChatMessage, NotificationData));
      if (ConfigRaid.DoShoutout) Chat.Shoutout(userID);
      AddNotification(new Notification(ConfigRaid, NotificationData));
    }

    /// <summary> Creates and adds to queue Random Video notification. </summary>
    public static void CreateRandomVideoNotification(string messageID)
    {
      var videos = GetRandomVideos();
      if (videos is null || videos.Count == 0) return;
      string msgID = messageID;

      AddNotification(new Notification()
      {
        VideoPath = videos[Random.Shared.Next(0, videos.Count)].FullName,
        VideoVolume = Config.VolumeVideos,
        VideoParams = RandomVideoParameters,

        ExtraActionAtStartup = () =>
        {
          if (Config.Data[Config.Keys.ChannelRedemption_RandomVideo_MarkAsFulfilled].Equals("True"))
          {
            MarkRedemptionAsFulfilled(Config.Data[Config.Keys.ChannelRedemption_RandomVideo_ID], msgID);
          }
        }
      });
    }

    /// <summary> Creates and adds to queue Chatter Timeout notification. </summary>
    /// <param name="userName">Chatter name that got the timeout</param>
    /// <param name="duration">Timeout duration</param>
    /// <param name="reason">Timeout reason</param>
    public static void CreateTimeoutNotification(string userName, TimeSpan duration, string reason)
    {
      if (!ConfigTimeout.Enable) return;
      if (duration < ConfigTimeout.MinimumTime) return;

      Array.Clear(NotificationData);
      NotificationData[0] = userName;
      NotificationData[7] = reason;

      Chat.AddMessageToQueue(string.Format(ConfigTimeout.ChatMessage, NotificationData));
      AddNotification(new Notification(ConfigTimeout, NotificationData));
    }

    /// <summary> Creates and adds to queue Chatter ban notification. </summary>
    /// <param name="userName">Chatter name that got banned</param>
    /// <param name="reason">Ban reason</param>
    public static void CreateBanNotification(string userName, string reason)
    {
      if (!ConfigBan.Enable) return;

      Array.Clear(NotificationData);
      NotificationData[0] = userName;
      NotificationData[7] = reason;

      Chat.AddMessageToQueue(string.Format(ConfigBan.ChatMessage, NotificationData));
      AddNotification(new Notification(ConfigBan, NotificationData));
    }

    private static void CreateVoicesPaste()
    {
      if (Chat.ResponseMessages.ContainsKey("!voices"))
      {
        MainWindow.ConsoleWarning(">> Couldn't add respoonse to \"!voices\" key - the key is already present in response messages.");
        return;
      }

      StringBuilder sb = new();
      sb.AppendLine("StreamElements voices:");
      StreamElements.AppendVoices(ref sb, 70);
      sb.AppendLine();
      sb.AppendLine();
      sb.AppendLine("TikTok voices:");
      TikTok.AppendVoices(ref sb, 70);

      GlotPaste paste = new() { Language = "plaintext", Title = "TTS Voices" };
      paste.Files.Add(new GlotFile() { Name = "TTS Voices", Content = sb.ToString() });

      using HttpRequestMessage request = new(HttpMethod.Post, "https://glot.io/api/snippets");
      request.Content = new StringContent(paste.ToJsonString(), Encoding.UTF8, "application/json");

      string resp;
      try { resp = Client.Send(request).Content.ReadAsStringAsync().Result; }
      catch (HttpRequestException ex) { MainWindow.ConsoleWarning($">> !voices response creation failed. {ex.Message}"); return; }
      var response = GlotResponse.Deserialize(resp);
      if (response?.Url?.Length > 0)
      {
        VoicesLink = response.Url.Replace("api/", ""); // Remove "api/" part
      }

      Chat.ResponseMessages.Add("!voices", ($"TTS Voices: {VoicesLink}", new DateTime()));
      MainWindow.ConsoleWarning(">> Added respoonse to \"!voices\" key.");
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
        ), new DateTime()));
        MainWindow.ConsoleWarning(">> Added respoonse to \"!voices\" key.");
      }
      else
      {
        MainWindow.ConsoleWarning(">> Couldn't add respoonse to \"!voices\" key - the key is already present in response messages.");
      }
    }

    /// <summary> Removes all follow notifications from the queue. </summary>
    public static void CleanFollowNotifications()
    {
      lock (NotificationQueue)
      {
        for (int i = NotificationQueue.Count - 1; i >= 0; i--)
        {
          if (NotificationQueue[i].Type == NotificationType.FOLLOW)
          {
            NotificationQueue.RemoveAt(i);
          }
        }
        MainWindow.I.SetNotificationQueueCount(NotificationQueue.Count);
      }
    }

    public static Dictionary<string, FileInfo> GetSampleSounds()
    {
      // Load sounds
      if (SampleSounds.Count == 0)
      {
        DirectoryInfo dir = new("Resources/Sounds");
        if (dir.Exists)
        {
          foreach (FileInfo file in dir.GetFiles())
          {
            if (file.Extension.Equals(".mp3") || file.Extension.Equals(".wav"))
            {
              SampleSounds.Add(file.Name.Replace(file.Extension, "").ToLower(), file);
              SoundsAvailable = true;
            }
          }
        }

        if (SampleSounds.Count == 0) SampleSounds.Add("___", null); // Add dummy sound for the top if not to be checked every time
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
      if (s_soundsSamplePasteLink?.Length > 0) return s_soundsSamplePasteLink;

      var samples = GetSampleSounds();
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

      GlotPaste paste = new() { Language = "plaintext", Title = "Sound samples" };
      paste.Files.Add(new GlotFile() { Name = "Sound samples", Content = result });

      using HttpRequestMessage request = new(HttpMethod.Post, "https://glot.io/api/snippets");
      request.Content = new StringContent(paste.ToJsonString(), Encoding.UTF8, "application/json");

      string resp;
      try { resp = Client.Send(request).Content.ReadAsStringAsync().Result; }
      catch (HttpRequestException ex) { MainWindow.ConsoleWarning($">> !voices response creation failed. {ex.Message}"); return string.Empty; }
      var response = GlotResponse.Deserialize(resp);
      if (response?.Url?.Length > 0)
      {
        s_soundsSamplePasteLink = response.Url.Replace("api/", ""); // Remove "api/" part
        MainWindow.ConsoleWarning(">> Created respoonse link to \"!sounds\" key.");
        return s_soundsSamplePasteLink;
      }
      else
      {
        return "something went wrong peepoSad";
      }
    }

    public static bool AreSoundsAvailable()
    {
      if (SampleSounds.Count == 0) GetSampleSounds();

      return SoundsAvailable;
    }

    public static List<FileInfo> GetRandomVideos()
    {
      // Load random videos
      if (RandomVideos.Count == 0)
      {
        DirectoryInfo dir = new("Resources/Videos");
        if (dir.Exists)
        {
          foreach (FileInfo file in dir.GetFiles())
          {
            if (Array.IndexOf(SupportedVideoFormats, file.Extension) >= 0)
            {
              RandomVideos.Add(file);
            }
          }
        }

        if (RandomVideos.Count == 0) RandomVideos.Add(new FileInfo(Guid.NewGuid().ToString())); // Add dummy random video for the top if not to be checked every time
      }

      return RandomVideos;
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
      request.Headers.Add("Authorization", $"Bearer {Secret.Data[Secret.Keys.OAuthToken]}");
      request.Headers.Add("Client-Id", Secret.Data[Secret.Keys.CustomerID]);

      string resp;
      try { resp = Client.Send(request).Content.ReadAsStringAsync().Result; }      // Assume that it worked
      catch (HttpRequestException ex) { MainWindow.ConsoleWarning($">> Marking redemption as fulfilled failed. {ex.Message}"); }
    }
  }

  public enum NotificationType { FOLLOW, SUBSCRIPTION, SUBSCRIPTIONGIFT, CHEER, RAID, REDEMPTION, TIMEOUT, BAN }

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
