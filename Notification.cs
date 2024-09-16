using System;
using System.Collections.Generic;
using System.IO;

using NAudio.Wave;

using Serilog;

namespace AbevBot
{
  public class Notification
  {
    private static readonly TimeSpan MinimumNotificationTime = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan MaximumNotificationTime = TimeSpan.FromSeconds(120);

    public bool Started { get; private set; }
    private DateTime StartTime { get; set; }
    public DateTime CreationTime { get; }
    public string TextToDisplay { get; init; }
    public Notifications.TextPosition TextToDisplayPosition { get; init; } = Notifications.TextPosition.TOP;
    public double TextToDisplaySize { get; init; }
    public string TextToRead { get; init; }
    public string SoundPath { get; init; }
    public string VideoPath { get; init; }
    private bool VideoEnded;
    private bool VideoStarted;
    private bool VideoPaused;
    private bool TextDisplayed;
    private bool TextCleared;
    private bool SoundPlayed;
    private bool TTSPlayed;
    private bool AudioEnded;
    private bool AudioStarted;
    private bool AudioPaused;
    private readonly ChannelRedemption Redemption;
    private bool KeysPressed, Keys2Pressed;
    public NotificationType Type { get; init; }
    public Action ExtraActionAtStartup { get; set; }
    public VideoParameters VideoParams;
    public DateTime StartAfter;
    public string Sender = string.Empty;
    public DateTime RelevanceTime;
    public NotificationControl Control { get; private set; }

    public Notification()
    {
      CreationTime = DateTime.Now;
    }

    public Notification(NotificationsConfig config, string[] data, ChannelRedemption redemption = null)
    {
      CreationTime = DateTime.Now;

      Sender = data[0];
      TextToDisplay = string.Format(config.TextToDisplay, data).Replace("\\n", Environment.NewLine);
      TextToDisplayPosition = config.TextPosition;
      TextToDisplaySize = config.TextSize;
      if (config.Type == NotificationType.CHEER)
      {
        // For bits check the minimum amount
        if (int.TryParse(data[4], out int bits))
        {
          if (bits >= config.MinimumBits) TextToRead = string.Format(config.TextToSpeech, data).Replace("\\n", Environment.NewLine).Replace("#", ""); // Remove '#' symbols - they are not allowed in TTS request messages
          else TextToRead = string.Empty;
        }
      }
      else { TextToRead = string.Format(config.TextToSpeech, data).Replace("\\n", Environment.NewLine).Replace("#", ""); } // Remove '#' symbols - they are not allowed in TTS request messages
      SoundPath = config.SoundToPlay;
      VideoPath = config.VideoToPlay;
      VideoParams = config.VideoParams;
      Redemption = redemption;
      Type = config.Type;

      Control = new(this);
    }

    /// <summary> Initializes required things and starts the notification </summary>
    public void Start()
    {
      if (Started) return;
      Started = true;

      TextDisplayed = TextToDisplay is null || TextToDisplay.Length == 0;
      TextCleared = TextDisplayed;
      VideoEnded = VideoPath is null || VideoPath.Length == 0;
      SoundPlayed = SoundPath is null || SoundPath.Length == 0;
      TTSPlayed = TextToRead is null || TextToRead.Length == 0;
      KeysPressed = Redemption is null || Redemption.KeysToPress.Count == 0;
      Keys2Pressed = Redemption is null || Redemption.KeysToPressAfterTime.Count == 0;

      if (!VideoEnded)
      {
        Server.VideoEndedCounter = 0;
        Server.VideoEnded = false;
      }

      List<ISampleProvider> sounds = new();
      if (!TTSPlayed)
      {
        // There is TTS to play, find all of the voices in the message and split the message to be read by different voices
        var sampleSounds = Notifications.GetSampleSounds();
        List<string> text = new();
        int index;
        text.AddRange(TextToRead.Split(':'));

        if (text.Count == 0) { Log.Warning("Notification has nothing to read: {text}", TextToRead); } // Nothing to read? Do nothing
        else if (text.Count == 1) { NoIdeaForTheName(text[^1], ref sampleSounds, ref sounds, "StreamElements", "Brian"); } // Just text, read with default voice
        else
        {
          // There is at least one attempt to change the voice
          string voice, maybeVoice;
          while (text.Count > 1)
          {
            // Find a space before voice name
            index = text[^2].LastIndexOf(" ");
            if (index <= 0) { maybeVoice = text[^2].Trim(); } // Whole text[^2] is voice name, try to get it
            else { maybeVoice = text[^2].Substring(index).Trim(); } // Part of text[^2] is voice name - extract it

            voice = StreamElements.GetVoice(maybeVoice);
            if (voice?.Length > 0)
            {
              NoIdeaForTheName(text[^1].Trim(), ref sampleSounds, ref sounds, "StreamElements", voice);
              text.RemoveAt(text.Count - 1);
              if (index <= 0) { text.RemoveAt(text.Count - 1); }
              else { text[^1] = text[^1].Substring(0, index); }
              continue;
            }
            voice = TikTok.GetVoice(maybeVoice);
            if (voice?.Length > 0)
            {
              NoIdeaForTheName(text[^1].Trim(), ref sampleSounds, ref sounds, "TikTok", voice);
              text.RemoveAt(text.Count - 1);
              if (index <= 0) { text.RemoveAt(text.Count - 1); }
              else { text[^1] = text[^1].Substring(0, index); }
              continue;
            }

            // The voice is not found, join [^2] with [^1] and ':' symbol, and remove last text that was merged
            text[^2] = string.Join(':', text[^2], text[^1]);
            text.RemoveAt(text.Count - 1);
          }

          if (text.Count == 1)
          {
            // A text to read is left, so no voice is found for it, read it with default voice
            // It may be that there was a voice change after a voice change - try to find the voice with remaining text
            if (text[0].Trim().Length > 0)
            {
              maybeVoice = text[0].Trim();
              voice = StreamElements.GetVoice(maybeVoice);
              if (voice?.Length == 0) { voice = TikTok.GetVoice(maybeVoice); }
              if (voice is null || voice?.Length == 0) { NoIdeaForTheName(text[0].Trim(), ref sampleSounds, ref sounds, "StreamElements", "Brian"); }
              else { } // The remaining part was also a voice change so, do nothing? May add null to sounds but what's the point of it?
            }
          }
          else if (text.Count != 0)
          {
            // Something bad happened
            Log.Warning("Something bad happened with TTS generation");
          }
        }
      }
      if (!SoundPlayed) { Audio.AddToSampleProviderList(SoundPath, ref sounds, 0); }

      // Create sound to play from samples
      if (sounds.Count > 0)
      {
        Server.AudioEndedCounter = 0;
        Server.AudioEnded = false;
        Server.CurrentAudio = Audio.GetSoundData(sounds);
        AudioEnded = false;
      }
      else
      {
        Server.CurrentAudio = null;
        AudioEnded = true;
      }

      if (ExtraActionAtStartup != null) ExtraActionAtStartup();

      StartTime = DateTime.Now;
    }

    /// <summary> Update status of playing notification. </summary>
    /// <returns> <value>true</value> if notification ended. </returns>
    public bool Update()
    {
      if (!Started) return false;

      if (DateTime.Now - StartTime > MaximumNotificationTime)
      {
        Log.Warning("Maximum notification time reached, something went wrong, to not block other notificaitons force closing this one!");
        Stop();
        return true;
      }

      // Press keys
      if (Redemption != null)
      {
        if (!KeysPressed)
        {
          KeysPressed = true;
          if (Redemption.KeysToPressType == KeyActionType.PRESS)
          {
            for (int i = 0; i < Redemption.KeysToPress.Count; i++) Simulation.Keyboard.Press(Redemption.KeysToPress[i]);
            for (int i = Redemption.KeysToPress.Count - 1; i >= 0; i--) Simulation.Keyboard.Release(Redemption.KeysToPress[i]);
          }
          else if (Redemption.KeysToPressType == KeyActionType.TYPE)
          {
            for (int i = 0; i < Redemption.KeysToPress.Count; i++) Simulation.Keyboard.Type(Redemption.KeysToPress[i]);
          }
        }

        if (!Keys2Pressed)
        {
          if (DateTime.Now - StartTime >= Redemption.TimeToPressSecondAction)
          {
            Keys2Pressed = true;

            // Press keys
            if (Redemption.KeysToPressAfterTimeType == KeyActionType.PRESS)
            {
              for (int i = 0; i < Redemption.KeysToPressAfterTime.Count; i++) Simulation.Keyboard.Press(Redemption.KeysToPressAfterTime[i]);
              for (int i = Redemption.KeysToPressAfterTime.Count - 1; i >= 0; i--) Simulation.Keyboard.Release(Redemption.KeysToPressAfterTime[i]);
            }
            else if (Redemption.KeysToPressAfterTimeType == KeyActionType.TYPE)
            {
              for (int i = 0; i < Redemption.KeysToPressAfterTime.Count; i++) Simulation.Keyboard.Type(Redemption.KeysToPressAfterTime[i]);
            }
          }
        }
      }

      if (!VideoEnded)
      {
        if (!VideoStarted && !Notifications.SkipNotification)
        {
          // Start the video
          VideoStarted = true;
          Server.PlayVideo(VideoPath,
            VideoParams != null ? (float)VideoParams.Left : -1f,
            VideoParams != null ? (float)VideoParams.Top : -1f,
            VideoParams != null ? (float)VideoParams.Width : -1f,
            VideoParams != null ? (float)VideoParams.Height : -1f,
            Config.VolumeVideo);
        }
        else if (Notifications.SkipNotification)
        {
          Server.ClearVideo();
          VideoEnded = true;
        }
        else if (Notifications.NotificationsPaused)
        {
          if (!VideoPaused)
          {
            VideoPaused = true;
            Server.Pause();
          }
        }
        else if (VideoPaused)
        {
          VideoPaused = false;
          Server.Resume();
        }
        else if (Server.VideoEnded) { VideoEnded = true; }
      }

      // Display text
      if (!TextDisplayed && !Notifications.NotificationsPaused && !Notifications.SkipNotification)
      {
        TextDisplayed = true;
        Server.DisplayText(TextToDisplay, TextToDisplayPosition, TextToDisplaySize);
      }

      if (!VideoEnded) return false;

      // Clear displayed text
      if (!TextCleared && (DateTime.Now - StartTime >= MinimumNotificationTime))
      {
        TextCleared = true;
        Server.ClearText();
      }

      // Play the audio
      if (!AudioEnded)
      {
        if (Notifications.SkipNotification)
        {
          // Skip notification active - stop current audio and clear the queue
          Server.ClearAudio();
        }
        else if (Notifications.NotificationsPaused && !AudioPaused)
        {
          // Pause notification active and the sound is playing - pause it
          AudioPaused = true;
          Server.Pause();
        }
        else if (!Notifications.NotificationsPaused && AudioPaused)
        {
          // Pause notification not active and the sound is not playing - play it
          AudioPaused = false;
          Server.Resume();
        }
        else if (Server.AudioEnded)
        {
          // Server is reporting that audio has finished
          AudioEnded = true;
        }
        else if (!AudioStarted)
        {
          // Play the audio
          AudioStarted = true;
          Server.PlayAudio(Config.VolumeAudio);
        }

        if (!AudioEnded) { return false; }
      }

      // Check if keys was pressed
      if (!KeysPressed || !Keys2Pressed) return false;

      // The notification is over, clear after it
      if (!Notifications.SkipNotification && (DateTime.Now - StartTime < MinimumNotificationTime)) return false;
      Stop(); // Just to be sure that everything is cleared out

      return true; // return true when notification has ended
    }

    /// <summary> Stops the notification. </summary>
    public void Stop()
    {
      if (!Started) return;
      Server.ClearAll();
    }

    // FIXME: Figure out good method name :D
    // It searches for sound samples in provided text,
    // splits the text to parts that have to be read, and parts that should play sound sample
    // It also inserts new sounds at the beginning of provided sounds array
    // GettoTTSoAndoInsertoSamplesoAndoMergoWithoProvidedoSoundeso??
    private void NoIdeaForTheName(string _text, ref Dictionary<string, FileInfo> sampleSounds, ref List<ISampleProvider> sounds, string supplier, string voice)
    {
      string text = _text;
      string maybeSample;
      int index = -1, nextIndex;
      bool maybeUpSample, maybeDownSample;
      List<ISampleProvider> newAudio = new();

      while (text.Length > 0)
      {
        index = text.IndexOf('-', index + 1);
        maybeUpSample = false;
        maybeDownSample = false;
        if (index >= 0)
        {
          nextIndex = text.IndexOf(" ", index); // Space index (end of word after '-' symbol)
          if (nextIndex > index || nextIndex == -1)
          {
            // We got indexes between a word that starts with '-' symbol is placed (-1 means to the end of the text)
            maybeSample = text.Substring(index + 1, nextIndex > 0 ? (nextIndex - index - 1) : (text.Length - index - 1));
            maybeSample = maybeSample.Trim().ToLower();
            // Skip symbols: !, ., ,, :, etc. - characters between 33 and 47 in ASCII
            for (int i = 0; i < maybeSample.Length; i++)
            {
              var c = maybeSample[i];
              if (c >= 33 && c <= 47)
              {
                // Found first symbol
                maybeSample = maybeSample[..i];
                break;
              }
            }
            // Check if the sample exist in samples list
            if (sampleSounds.TryGetValue(maybeSample, out var sample))
            {
              // Add text before the sample to be read
              if (supplier.Equals("StreamElements")) { Audio.AddToSampleProviderList(StreamElements.GetTTS(text[..index].Trim(), voice), ref newAudio); }
              else if (supplier.Equals("TikTok")) { Audio.AddToSampleProviderList(TikTok.GetTTS(text[..index].Trim(), voice), ref newAudio); }
              else { Log.Warning("TTS supplier {supplier} not recognized!", supplier); }

              // Add sample sound
              Audio.AddToSampleProviderList(sample, ref newAudio);

              // Remove already parsed text
              if (nextIndex == -1) { text = string.Empty; } // Already reached the end
              else { text = text[nextIndex..]; }

              index = -1;
            }
            else
            {
              // Maybe it was -upsample or -downsample?
              if (maybeSample.StartsWith("up"))
              {
                maybeUpSample = true;
                maybeSample = maybeSample[2..]; // 2 == "up".Length
              }
              else if (maybeSample.StartsWith("down"))
              {
                maybeDownSample = true;
                maybeSample = maybeSample[4..]; // 2 == "down".Length
              }

              if (maybeUpSample || maybeDownSample)
              {
                // Check again if sample without "up" / "down" prefix exist
                if (sampleSounds.ContainsKey(maybeSample.Trim().ToLower()))
                {
                  // Add text before the sample to be read
                  if (supplier.Equals("StreamElements")) { Audio.AddToSampleProviderList(StreamElements.GetTTS(text[..index].Trim(), voice), ref newAudio); }
                  else if (supplier.Equals("TikTok")) { Audio.AddToSampleProviderList(TikTok.GetTTS(text[..index].Trim(), voice), ref newAudio); }
                  else { Log.Warning("TTS supplier {supplier} not recognized!", supplier); }

                  // Add sample sound
                  if (maybeUpSample) { Audio.AddToSampleProviderList(Audio.GetUpOrDownSample(sampleSounds[maybeSample], false), ref newAudio); }
                  else if (maybeDownSample) { Audio.AddToSampleProviderList(Audio.GetUpOrDownSample(sampleSounds[maybeSample], true), ref newAudio); }

                  // Remove already parsed text
                  if (nextIndex == -1) { text = string.Empty; } // Already reached the end
                  else { text = text[nextIndex..]; }

                  index = -1;
                }
              }
            }
          }
        }
        else
        {
          // No sample found, add text to be read, clear the remainder of the text
          if (supplier.Equals("StreamElements")) { Audio.AddToSampleProviderList(StreamElements.GetTTS(text.Trim(), voice), ref newAudio); }
          else if (supplier.Equals("TikTok")) { Audio.AddToSampleProviderList(TikTok.GetTTS(text.Trim(), voice), ref newAudio); }
          else { Log.Warning("TTS supplier {supplier} not recognized!", supplier); }
          text = string.Empty;
        }
      }

      // Insert new audio to sounds list
      for (int i = newAudio.Count - 1; i >= 0; i--)
      {
        if (newAudio[i] is null) { Log.Warning("Some TTS request returned null audio player!"); }
        else { sounds.Insert(0, newAudio[i]); }
      }
    }

    public void UpdateControl()
    {
      if (Control is null) { Control = new(this); }

      Control.Update();
    }
  }
}
