using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AbevBot
{
  public class Notifications
  {
    /// <summary> Notifications thread started. </summary>
    public static bool Started { get; private set; }
    public static bool NotificationsPaused { get; set; }
    public static bool SkipNotification { get; set; }
    public static bool ChatTTSEnabled { get; set; }
    static readonly List<Notification> NotificationQueue = new();
    private static Thread NotificationsThread;
    public static readonly HttpClient Client = new();
    public static string VoicesLink { get; private set; }

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
              MainWindow.SetNotificationQueueCount(NotificationQueue.Count);
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

    public static void AddNotification(Notification notification)
    {
      Task.Run(() =>
      {
        lock (NotificationQueue)
        {
          NotificationQueue.Add(notification);
          MainWindow.SetNotificationQueueCount(NotificationQueue.Count);
        }
      });
    }

    public static void CreateFollowNotification(string userName)
    {
      Chat.AddMessageToQueue($"@{userName} thank you for the follow!");
      AddNotification(new Notification()
      {
        TextToDisplay = $"New follower {userName}!",
        SoundPath = "Resources/tone1.wav",
        SoundVolume = 0.3f
      });
    }

    public static void CreateSubscriptionNotification(string userName, string tier, string message)
    {
      AddNotification(new Notification()
      {
        TextToDisplay = $"Thank you {userName}!\n{message}",
        TextToRead = $"Thank you {userName} for tier {tier} sub! {message}",
        TTSVolume = 0.4f,
        VideoPath = "Resources/peepoHey.mp4",
        SoundVolume = 0.8f
      });
    }

    public static void CreateReceiveGiftSubscriptionNotification(string userName)
    {
      AddNotification(new Notification()
      {
        TextToDisplay = $"New subscriber {userName}!",
        SoundPath = "Resources/tone1.wav",
        SoundVolume = 0.3f
      });
    }

    public static void CreateGiftSubscriptionNotification(string userName, string tier, int count, string message)
    {
      AddNotification(new Notification()
      {
        TextToDisplay = $"Thank you {userName} for {count} subs!\n{message}",
        TextToRead = $"Thank you {userName} for gifting {count} tier {tier} subs! {message}",
        TTSVolume = 0.4f,
        VideoPath = "Resources/peepoHey.mp4",
        SoundVolume = 0.8f
      });
    }

    public static void CreateSubscriptionNotification(string userName, string tier, int duration, int streak, EventPayloadMessage message)
    {
      // TODO: Create message to read - remove emotes from the message using message.Emotes[], don't read them

      AddNotification(new Notification()
      {
        TextToDisplay = $"Thank you {userName}!\n{message.Text}",
        TextToRead = string.Concat(
          "Thank you ", userName, " for ",
          duration > 1 ? $"{duration} months in advance" : "",
          " tier ", tier, " sub!",
          streak > 1 ? $" It's your {streak} month in a row!" : "",
          " ", message.Text
          ),
        TTSVolume = 0.4f,
        VideoPath = "Resources/peepoHey.mp4",
        SoundVolume = 0.8f
      });
    }

    public static void CreateCheerNotification(string userName, int count, string message)
    {
      AddNotification(new Notification()
      {
        TextToDisplay = $"Thank you {userName} for {count} bits!\n{message}",
        TextToRead = $"Thank you {userName} for {count} bits! {message}",
        TTSVolume = 0.4f,
        SoundPath = "Resources/tone1.wav",
        SoundVolume = 0.3f
      });
    }

    public static void CreateRedemptionNotificaiton(string userName, string id, string message)
    {
      // TODO: check id and do something
      if (id.Equals(""))
      {

      }
      else if (id.Equals(""))
      {
        // Random video
        DirectoryInfo dir = new("Resources/Videos");
        if (dir.Exists)
        {
          List<string> videos = new();
          foreach (FileInfo file in dir.GetFiles())
          {
            if (file.Exists && file.Extension == ".mp4")
            {
              videos.Add(file.FullName);
            }
          }

          AddNotification(new Notification()
          {
            VideoPath = videos[Random.Shared.Next(0, videos.Count)],
            VideoVolume = 0.8f
          });
        }
      }
    }

    public static void CreateTTSNotification(string text)
    {
      if (!ChatTTSEnabled) return;

      AddNotification(new Notification()
      {
        TextToRead = text,
        TTSVolume = 0.4f
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
  }
}
