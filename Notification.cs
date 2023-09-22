using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
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
    static readonly List<Notification> NotificationQueue = new();
    private static Thread NotificationsThread;
    private static readonly HttpClient Client = new();
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
        TTSVolume = 0.8f,
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
        DirectoryInfo dir = new("Resources/videos");
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
        TryToGetVoiceFromMessage = true,
        SoundVolume = 0.6f
      });
    }

    public static WaveOut GetTTS(string _text, string _voice = "Brian", float soundVolume = 1f, bool getVoiceFromMessage = false)
    {
      string voiceName;
      string text = _text;
      string voice = _voice;
      if (getVoiceFromMessage)
      {
        int voiceEndSymbolIndex = text.IndexOf(':');
        int lastSpace = 0;
        if (voiceEndSymbolIndex > 0)
        {
          lastSpace = text.LastIndexOf(" ", voiceEndSymbolIndex);
          if (lastSpace < 0) lastSpace = 0;
          voice = text.Substring(lastSpace, voiceEndSymbolIndex - lastSpace);
        }

        text = text.Substring(lastSpace + voice.Length + 1).Trim();
      }

      // StreamElements
      voiceName = StreamElements.GetVoice(voice);
      if (voiceName?.Length > 0) { return GetStreamElementsTTS(text, voiceName, soundVolume); }

      // TikTok
      voiceName = TikTok.GetVoice(voice);
      if (voiceName?.Length > 0) { return GetTikTokTTS(text, voiceName, soundVolume); }

      return GetStreamElementsTTS(_text, soundVolume: soundVolume); // Voice not found, return default StreamElements voice
    }

    private static WaveOut GetStreamElementsTTS(string text, string voice = "Brian", float soundVolume = 1f)
    {
      Stream stream;
      using HttpRequestMessage request = new(HttpMethod.Get, $"https://api.streamelements.com/kappa/v2/speech?voice={voice}&text={text}");
      stream = Client.Send(request).Content.ReadAsStream();

      return Audio.PlayMp3Sound(stream, soundVolume);
    }

    /// <summary> Can be used to get available StreamElements voices. The voices are printed to the console. </summary>
    private static void GetStreamElementsVoices()
    {
      string[] voices = Array.Empty<string>();

      // Ask for speech without specifying the voice, error message will contain all available voices
      using HttpRequestMessage request = new(HttpMethod.Get, "https://api.streamelements.com/kappa/v2/speech?voice=");
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
            voices = response.Message.Substring(startIndex, endIndex - startIndex).Split(",", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
          }
        }
      }

      MainWindow.ConsoleWarning(string.Concat(">> StreamElements voices: ", string.Join(", ", voices)));
    }

    private static WaveOut GetTikTokTTS(string _text, string voice, float soundVolume = 1f)
    {
      if (Config.Data[Config.Keys.TikTokSessionID].Length == 0) return null;

      string text = _text;
      text = text.Replace("+", "plus");
      text = text.Replace(" ", "+");
      text = text.Replace("&", "and");

      string url = $"https://api16-normal-v6.tiktokv.com/media/api/text/speech/invoke/?text_speaker={voice}&req_text={text}&speaker_map_type=0&aid=1233";

      TikTokTTSResponse result;
      using HttpRequestMessage request = new(HttpMethod.Post, url);
      request.Headers.Add("User-Agent", "com.zhiliaoapp.musically/2022600030 (Linux; U; Android 7.1.2; es_ES; SM-G988N; Build/NRD90M;tt-ok/3.12.13.1)");
      request.Headers.Add("Cookie", $"sessionid={Config.Data[Config.Keys.TikTokSessionID]}");

      result = TikTokTTSResponse.Deserialize(Client.Send(request).Content.ReadAsStringAsync().Result);
      if (result?.StatusCode != 0)
      {
        MainWindow.ConsoleWarning($">> TikTok TTS request status: {result?.StatusCode}, error: {result?.StatusMessage}");
        return null;
      }
      else if (result?.Data?.Duration?.Length is null || string.IsNullOrEmpty(result?.Data?.VStr))
      {
        MainWindow.ConsoleWarning($">> TikTok TTS request returned sound with length 0.");
        return null;
      }

      return Audio.PlayMp3Sound(new MemoryStream(Convert.FromBase64String(result.Data.VStr)), soundVolume);
    }

    private static void CreateVoicesPaste()
    {
      if (Chat.ResponseMessages.ContainsKey("!voices"))
      {
        MainWindow.ConsoleWarning($">> Couldn't add respoonse to \"!voices\" key - the key is already present in response messages.");
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
      MainWindow.ConsoleWarning($">> Added respoonse to \"!voices\" key.");
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
        MainWindow.ConsoleWarning($">> Added respoonse to \"!voices\" key.");
      }
      else
      {
        MainWindow.ConsoleWarning($">> Couldn't add respoonse to \"!voices\" key - the key is already present in response messages.");
      }
    }
  }

  public class Notification
  {
    private static readonly TimeSpan MinimumNotificationTime = new(0, 0, 2);

    public bool Started { get; private set; }
    public DateTime StartTime { get; private set; }
    public string TextToDisplay { get; init; }
    public string TextToRead { get; init; }
    public string TTSVoice { get; init; }
    public bool TryToGetVoiceFromMessage { get; init; }
    public float TTSVolume { get; init; } = 1f;
    public string SoundPath { get; init; }
    public float SoundVolume { get; init; } = 1f;
    public string VideoPath { get; init; }
    public float VideoVolume { get; init; } = 1f;
    private WaveOut AudioPlayer;
    private bool VideoEnded;
    private bool VideoStarted;
    private bool TextDisplayed;
    private bool SoundPlayed;
    private bool VoicePlayed;

    /// <summary> Initializes required things and starts the notification </summary>
    public void Start()
    {
      if (Started) return;
      Started = true;
      StartTime = DateTime.Now;

      VideoEnded = VideoPath is null || VideoPath.Length == 0;
      SoundPlayed = SoundPath is null || SoundPath.Length == 0;
      VoicePlayed = TextToRead is null || TextToRead.Length == 0;
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
      if (!TextDisplayed && !Notifications.NotificationsPaused && !Notifications.SkipNotification && TextToDisplay?.Length > 0)
      {
        TextDisplayed = true;
        MainWindow.SetTextDisplayed(TextToDisplay);
      }

      // Create audio player and play
      if (!SoundPlayed || !VoicePlayed)
      {
        if (Notifications.SkipNotification) { AudioPlayer?.Stop(); }
        else if (AudioPlayer is null && (TextToRead?.Length > 0 || SoundPath?.Length > 0)) { CreateAudioPlayer(); }
        else
        {
          if (Notifications.NotificationsPaused) { AudioPlayer?.Pause(); }
          else { AudioPlayer?.Play(); }
        }

        if (AudioPlayer?.PlaybackState == PlaybackState.Stopped)
        {
          AudioPlayer?.Dispose(); // Probably it's better to dispose the player after it finished
          AudioPlayer = null;
        }
        return false;
      }
      if (AudioPlayer != null && AudioPlayer.PlaybackState != PlaybackState.Stopped) return false;

      // The notification is over, clear after it
      if (DateTime.Now - StartTime < MinimumNotificationTime) return false;
      MainWindow.SetTextDisplayed(string.Empty); // Clear displayed text

      if (Notifications.SkipNotification) Notifications.SkipNotification = false;

      return true; // return true when notification has ended
    }

    private void CreateAudioPlayer()
    {
      if (!SoundPlayed && !string.IsNullOrEmpty(SoundPath))
      {
        AudioPlayer = Audio.PlayWavSound(SoundPath, SoundVolume);
        SoundPlayed = true;
      }
      else if (TextToRead?.Length > 0)
      {
        AudioPlayer = Notifications.GetTTS(TextToRead, TTSVoice, TTSVolume, TryToGetVoiceFromMessage);
        if (AudioPlayer is null) MainWindow.ConsoleWarning(">> Returned AudioPlayer is null!");
        VoicePlayed = true;
      }
    }
  }
}
