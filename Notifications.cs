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

    public static NotificationsConfig ConfigFollow { get; set; } = new();
    public static NotificationsConfig ConfigSubscription { get; set; } = new();
    public static NotificationsConfig ConfigSubscriptionExt { get; set; } = new();
    public static NotificationsConfig ConfigSubscriptionGift { get; set; } = new();
    public static NotificationsConfig ConfigSubscriptionGiftReceived { get; set; } = new();
    public static NotificationsConfig ConfigCheer { get; set; } = new();
    private static readonly string[] NotificationData = new string[8];
    public static readonly List<ChannelRedemption> ChannelRedemptions = new();

    public enum TextPosition { TOP, MIDDLE, BOTTOM }

    public static void Start()
    {
      if (Started) return;
      Started = true;
      MainWindow.ConsoleWarning(">> Starting notifications thread.");

      CreateVoicesResponse();

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

      string chatter = userName.Trim();
      if (string.IsNullOrWhiteSpace(chatter)) chatter = "Anonymous";

      Array.Clear(NotificationData);
      NotificationData[0] = chatter;

      Chat.AddMessageToQueue(string.Format(ConfigFollow.ChatMessage, NotificationData));
      AddNotification(new Notification(ConfigFollow, NotificationData));
    }

    /// <summary> Creates and adds to queue Subscription notification. </summary>
    public static void CreateSubscriptionNotification(string userName, string tier, string message)
    {
      if (!ConfigSubscription.Enable) return;

      string chatter = userName.Trim();
      if (string.IsNullOrWhiteSpace(chatter)) chatter = "Anonymous";

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

      string chatter = userName.Trim();
      if (string.IsNullOrWhiteSpace(chatter)) chatter = "Anonymous";

      Array.Clear(NotificationData);
      NotificationData[0] = chatter;
      NotificationData[1] = tier[..1];
      NotificationData[2] = duration.ToString();
      NotificationData[3] = streak.ToString();
      NotificationData[5] = cumulative.ToString();
      NotificationData[7] = message.Text; // TODO: Create message to read - remove emotes from the message using message.Emotes[], don't read them

      Chat.AddMessageToQueue(string.Format(ConfigSubscriptionExt.ChatMessage, NotificationData));
      AddNotification(new Notification(ConfigSubscriptionExt, NotificationData));
    }

    /// <summary> Creates and adds to queue Received Gifted Subscription notification. </summary>
    public static void CreateGiftSubscriptionNotification(string userName, string tier, int count, string message)
    {
      if (!ConfigSubscriptionGift.Enable) return;

      string chatter = userName.Trim();
      if (string.IsNullOrWhiteSpace(chatter)) chatter = "Anonymous";

      Array.Clear(NotificationData);
      NotificationData[0] = chatter;
      NotificationData[1] = tier[..1];
      NotificationData[4] = count.ToString();
      NotificationData[7] = message;

      Chat.AddMessageToQueue(string.Format(ConfigSubscriptionGift.ChatMessage, NotificationData));
      AddNotification(new Notification(ConfigSubscriptionGift, NotificationData));
    }

    /// <summary> Creates and adds to queue Gifted Subscription notification. </summary>
    public static void CreateReceiveGiftSubscriptionNotification(string userName)
    {
      if (!ConfigSubscriptionGiftReceived.Enable) return;

      string chatter = userName.Trim();
      if (string.IsNullOrWhiteSpace(chatter)) chatter = "Anonymous";

      Array.Clear(NotificationData);
      NotificationData[0] = chatter;

      Chat.AddMessageToQueue(string.Format(ConfigSubscriptionGiftReceived.ChatMessage, NotificationData));
      AddNotification(new Notification(ConfigSubscriptionGiftReceived, NotificationData));
    }

    /// <summary> Creates and adds to queue Cheer notification. </summary>
    public static void CreateCheerNotification(string userName, int count, string message)
    {
      if (!ConfigCheer.Enable) return;

      string chatter = userName.Trim();
      if (string.IsNullOrWhiteSpace(chatter)) chatter = "Anonymous";

      Array.Clear(NotificationData);
      NotificationData[0] = chatter;
      NotificationData[4] = count.ToString();
      NotificationData[7] = message;

      Chat.AddMessageToQueue(string.Format(ConfigCheer.ChatMessage, NotificationData));
      AddNotification(new Notification(ConfigCheer, NotificationData));
    }

    /// <summary> Creates and adds to queue Channel Points Redemption notification. </summary>
    public static void CreateRedemptionNotificaiton(string userName, string id, string message)
    {
      // TODO: check id and do something
      if (id.Equals(""))
      {

      }
      else if (Config.Data[Config.Keys.ChannelPoints_RandomVideo].Equals(id))
      {
        CreateRandomVideoNotification();
      }
      else
      {
        // Look throught channel redemptions list
        foreach (var redemption in ChannelRedemptions)
        {
          if (redemption.ID.Equals(id))
          {
            // Create notification
            Array.Clear(NotificationData);
            Chat.AddMessageToQueue(redemption.Config.ChatMessage);
            AddNotification(new Notification(redemption.Config, NotificationData));

            // Press keys
            if (redemption.KeysToPress.Count > 0)
            {
              for (int i = 0; i < redemption.KeysToPress.Count; i++) Simulation.Keyboard.Press(redemption.KeysToPress[i]);
              for (int i = redemption.KeysToPress.Count - 1; i >= 0; i--) Simulation.Keyboard.Release(redemption.KeysToPress[i]);
            }

            return;
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
        TextToRead = text,
        TTSVolume = Config.VolumeTTS
      });
    }

    /// <summary> Creates and adds to queue Random Video notification. </summary>
    public static void CreateRandomVideoNotification()
    {
      var videos = GetRandomVideos();
      if (videos is null || videos.Count == 0) return;

      AddNotification(new Notification()
      {
        VideoPath = videos[Random.Shared.Next(0, videos.Count)].FullName,
        VideoVolume = 0.8f
      });
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
      sb.Append(StreamElements.GetVoices());
      sb.AppendLine();
      sb.Append(TikTok.GetVoices());
      sb.AppendLine();

      GlotPaste paste = new() { Language = "plaintext", Title = "TTS Voices" };
      paste.Files.Add(new GlotFile() { Name = "TTS Voices", Content = sb.ToString() });

      using HttpRequestMessage request = new(HttpMethod.Post, "https://glot.io/api/snippets");
      request.Content = new StringContent(paste.ToJsonString(), Encoding.UTF8, "application/json");
      GlotResponse response = GlotResponse.Deserialize(Client.Send(request).Content.ReadAsStringAsync().Result);
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
            if (file.Extension.Equals(".mp4"))
            {
              RandomVideos.Add(file);
            }
          }
        }

        if (RandomVideos.Count == 0) RandomVideos.Add(new FileInfo(Guid.NewGuid().ToString())); // Add dummy random video for the top if not to be checked every time
      }

      return RandomVideos;
    }
  }

  public class NotificationsConfig
  {
    public bool Enable { get; set; }
    public string ChatMessage { get; set; } = string.Empty;
    public string TextToDisplay { get; set; } = string.Empty;
    public Notifications.TextPosition TextPosition { get; set; } = Notifications.TextPosition.MIDDLE;
    public string TextToSpeech { get; set; } = string.Empty;
    public string SoundToPlay { get; set; } = string.Empty;
    public string VideoToPlay { get; set; } = string.Empty;
  }

  public class ChannelRedemption
  {
    public string ID { get; set; }
    public NotificationsConfig Config { get; set; } = new();
    public List<Key> KeysToPress { get; set; } = new();
  }
}
