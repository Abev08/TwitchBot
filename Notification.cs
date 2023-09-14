using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;

namespace AbevBot
{
  public class Notifications
  {
    /// <summary> Notifications thread started. </summary>
    public static bool Started { get; private set; }
    public static bool NotificationsPaused { get; set; }
    public static bool SkipNotification { get; set; }
    public static bool ChatTTSEnabled { get; set; }
    static List<Notification> NotificationQueue = new();
    private static Thread NotificationsThread;
    private static HttpClient Client = new();
    private static readonly string[] VOICEBLACKLIST = new string[] { }; // Add blaclisted voices here
    private static List<string> VoicesStreamElements = new();
    public static string VoicesLink { get; private set; }

    public static void Start()
    {
      if (Started) return;
      Started = true;
      MainWindow.ConsoleWarning(">> Starting notifications thread.");

      GetStreamElementsVoices();
      // CreateVoicesPaste(); // FIXME: first !voices response should use it if VoicesLink variable is empty

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
            if (NotificationQueue[0].Started == false) NotificationQueue[0].Start();
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
          else Thread.Sleep(100); // Slow down the loop while notification is playing, 100 ms shouldn't be a problem?
        }
        else
        {
          Thread.Sleep(500); // Check for new notification every 500 ms
        }
      }
    }

    public static void AddNotification(Notification notification)
    {
      new Task(() =>
      {
        lock (NotificationQueue)
        {
          NotificationQueue.Add(notification);
          MainWindow.SetNotificationQueueCount(NotificationQueue.Count);
        }
      }).Start();
    }

    public static void CreateFollowNotification(string userName)
    {
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
        TextToRead = $"Thank you {userName} for {tier} tier sub! {message}",
        VideoPath = "Resources/peepoHey.mp4",
        SoundVolume = 0.8f
      });
    }

    public static void CreateTTSNotification(string text)
    {
      if (!ChatTTSEnabled) return;

      string message = text;
      // Check for voice, for now only single voice at beginning of the message
      string voice = null;
      int firstSpace = message.IndexOf(" ");
      if (firstSpace > 0)
      {
        voice = message[..firstSpace];
        if (voice.EndsWith(':'))
        {
          voice = voice[..^1];
          bool voiceFound = false;
          // Find voice index in voices arrays
          for (int i = 0; i < VoicesStreamElements.Count; i++)
          {
            if (VoicesStreamElements[i].ToLower().Equals(voice.ToLower()))
            {
              voiceFound = true;
              voice = VoicesStreamElements[i];
              break;
            }
          }

          if (voiceFound) message = message[firstSpace..]; // Remove voice part from the message
        }
        else { voice = null; }
      }

      AddNotification(new Notification()
      {
        TextToRead = message,
        TTSVoice = voice,
        SoundVolume = 0.6f
      });
    }

    public static NAudio.Wave.WaveOut GetTTS(string text, string voice = "Brian", float soundVolume = 1f)
    {
      int voiceIndex;
      voiceIndex = VoicesStreamElements.IndexOf(voice);
      if (voiceIndex >= 0) return GetStreamElementsTTS(text, VoicesStreamElements[voiceIndex], soundVolume);
      // TODO: Implement other voice services here, maybe TikTok?

      return GetStreamElementsTTS(text, soundVolume: soundVolume); // Voice not found, return default StreamElements voice
    }

    private static NAudio.Wave.WaveOut GetStreamElementsTTS(string text, string voice = "Brian", float soundVolume = 1f)
    {
      Stream stream;
      using (HttpRequestMessage request = new(new HttpMethod("GET"), $"https://api.streamelements.com/kappa/v2/speech?voice={voice}&text={text}"))
      {
        stream = Client.Send(request).Content.ReadAsStream();
      }
      return Audio.PlayMp3Sound(stream, soundVolume);
    }

    private static void GetStreamElementsVoices()
    {
      // Ask for speech without specifying the voice, error message will contain all available voices
      using (HttpRequestMessage request = new(new HttpMethod("GET"), "https://api.streamelements.com/kappa/v2/speech?voice="))
      {
        StreamElementsResponse response = StreamElementsResponse.Deserialize(Client.Send(request).Content.ReadAsStringAsync().Result);
        if (response?.Message?.Length > 0)
        {
          int startIndex = response.Message.IndexOf("must be one of");
          if (startIndex > 0)
          {
            startIndex = response.Message.IndexOf('[', startIndex) + 1;
            int endIndex = response.Message.IndexOf(']', startIndex);
            if (endIndex > startIndex)
            {
              string[] voices = response.Message.Substring(startIndex, endIndex - startIndex).Split(",", System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries);
              foreach (string voice in voices)
              {
                if (!voice.Contains('-') && !VOICEBLACKLIST.Contains(voice))
                {
                  // Don't add voices with '-' symbols inside - they are probably not working
                  VoicesStreamElements.Add(voice);
                }
              }
            }
          }
        }
      }

      MainWindow.ConsoleWarning(string.Concat(">> StreamElements voices: ", string.Join(", ", VoicesStreamElements)));
    }

    private static void CreateVoicesPaste()
    {
      StringBuilder sb = new();
      sb.AppendLine("StreamElements voices:");
      sb.AppendJoin(", ", VoicesStreamElements);
      sb.AppendLine();

      GlotPaste paste = new() { Language = "plaintext", Title = "TTS Voices" };
      paste.Files.Add(new GlotFile() { Name = "TTS Voices", Content = sb.ToString() });

      using (HttpRequestMessage request = new(new HttpMethod("POST"), "https://glot.io/api/snippets"))
      {
        request.Content = new StringContent(paste.ToJsonString());
        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");
        GlotResponse response = GlotResponse.Deserialize(Client.Send(request).Content.ReadAsStringAsync().Result);
        if (response?.Url?.Length > 0)
        {
          VoicesLink = response.Url.Replace("api/", ""); // Remove "api/" part
        }
      }
    }
  }

  public class Notification
  {
    public bool Started { get; private set; }
    public string TextToDisplay { get; init; }
    public string TextToRead { get; init; }
    public string TTSVoice { get; init; }
    public string SoundPath { get; init; }
    public float SoundVolume { get; init; } = 1f;
    public string VideoPath { get; init; }
    public float VideoVolume { get; init; } = 1f;
    private WaveOut AudioPlayer;
    private bool VideoEnded;
    private bool VideoStarted;
    private bool TextDisplayed;

    /// <summary> Initializes required things and starts the notification </summary>
    public void Start()
    {
      if (Started) return;
      Started = true;

      VideoEnded = VideoPath is null || VideoPath.Length == 0;
    }

    /// <summary> Update status of playing notification. </summary>
    /// <returns> <value>true</value> if notification ended. </returns>
    public bool Update()
    {
      if (!Started) return false;

      if (!VideoEnded)
      {
        if (!VideoStarted)
        {
          // Start the video
          VideoStarted = true;
          MainWindow.StartVideoPlayer(VideoPath, VideoVolume);
        }
        else if (Notifications.SkipNotification)
        {
          MainWindow.StopVideoPlayer();
          VideoEnded = true;
        }
        else if (MainWindow.VideoEnded) { VideoEnded = true; }
        if (!VideoEnded) return false;
      }

      // Display text
      if (!Notifications.NotificationsPaused && !Notifications.SkipNotification && TextToDisplay.Length > 0 && !TextDisplayed)
      {
        TextDisplayed = true;
        MainWindow.SetTextDisplayed(TextToDisplay);
      }

      // Create audio player and play the sound
      if (AudioPlayer is null && (TextToRead?.Length > 0 || SoundPath?.Length > 0)) { CreateAudioPlayer(); }
      if (Notifications.SkipNotification) { AudioPlayer?.Stop(); }
      else
      {
        if (Notifications.NotificationsPaused)
        {
          AudioPlayer?.Pause();
        }
        else
        {
          AudioPlayer?.Play();
        }
      }

      if (AudioPlayer?.PlaybackState != PlaybackState.Stopped) return false;
      // The notification is over, clear after it

      AudioPlayer?.Dispose(); // Probably it's better to dispose the player after it finished
      MainWindow.SetTextDisplayed(string.Empty); // Clear displayed text

      if (Notifications.SkipNotification) Notifications.SkipNotification = false;

      return true; // return true when notification has ended
    }

    private void CreateAudioPlayer()
    {
      if (!string.IsNullOrEmpty(SoundPath)) AudioPlayer = Audio.PlayWavSound(SoundPath, SoundVolume);
      else if (TextToRead?.Length > 0) AudioPlayer = Notifications.GetTTS(TextToRead, TTSVoice, SoundVolume);
      if (TextToDisplay?.Length > 0) MainWindow.SetTextDisplayed(TextToDisplay);
    }
  }
}
