using System;
using System.Collections.Generic;
using System.IO;

namespace AbevBot
{
  public static class Config
  {
    public enum Keys
    {
      ChannelName, ChannelID,
      ConsoleVisible, PeriodicMessageTimeInterval,

      Follow_Enable, Follow_ChatMessage, Follow_TextToDisplay, Follow_TextPosition, Follow_TextToSpeech, Follow_SoundToPlay, Follow_VideoToPlay,
      Subscription_Enable, Subscription_ChatMessage, Subscription_TextToDisplay, Subscription_TextPosition, Subscription_TextToSpeech, Subscription_SoundToPlay, Subscription_VideoToPlay,
      SubscriptionExt_Enable, SubscriptionExt_ChatMessage, SubscriptionExt_TextToDisplay, SubscriptionExt_TextPosition, SubscriptionExt_TextToSpeech, SubscriptionExt_SoundToPlay, SubscriptionExt_VideoToPlay,
      SubscriptionGift_Enable, SubscriptionGift_ChatMessage, SubscriptionGift_TextToDisplay, SubscriptionGift_TextPosition, SubscriptionGift_TextToSpeech, SubscriptionGift_SoundToPlay, SubscriptionGift_VideoToPlay,
      SubscriptionGiftReceived_Enable, SubscriptionGiftReceived_ChatMessage, SubscriptionGiftReceived_TextToDisplay, SubscriptionGiftReceived_TextPosition, SubscriptionGiftReceived_TextToSpeech, SubscriptionGiftReceived_SoundToPlay, SubscriptionGiftReceived_VideoToPlay,
      Cheer_Enable, Cheer_ChatMessage, Cheer_TextToDisplay, Cheer_TextPosition, Cheer_TextToSpeech, Cheer_SoundToPlay, Cheer_VideoToPlay,

      ChannelPoints_RandomVideo,
      msg
    };

    private const string FILENAME = "Config.ini";
    private const string VOLUMESFILENAME = ".volumes";

    private static Dictionary<Keys, string> _Data;
    public static Dictionary<Keys, string> Data
    {
      get
      {
        if (_Data is null)
        {
          _Data = new();
          foreach (var key in Enum.GetValues(typeof(Keys)))
          {
            _Data.Add((Keys)key, string.Empty);
          }
        }
        return _Data;
      }
    }
    public static bool ConsoleVisible { get; private set; }
    public static bool VolumeValuesDirty { get; set; }
    public static float VolumeTTS { get; set; }
    public static float VolumeSounds { get; set; }
    public static float VolumeVideos { get; set; }
    private static DateTime ConfigFileTimestamp;

    public static bool ParseConfigFile(bool reload = false)
    {
      if (reload) { MainWindow.ConsoleWarning($">> Reloading {FILENAME} file."); }
      else { MainWindow.ConsoleWarning($">> Reading {FILENAME} file."); }

      // Create resources folders just for user convenience (if they don't exist)
      DirectoryInfo dir = new("Resources/Sounds");
      if (!dir.Exists) dir.Create();
      dir = new DirectoryInfo("Resources/Videos");
      if (!dir.Exists) dir.Create();

      FileInfo configFile = new(FILENAME);
      if (configFile.Exists == false)
      {
        CreateConfigFile(configFile);
        return true;
      }
      else
      {
        using (StreamReader reader = new(configFile.FullName))
        {
          string line;
          int lineIndex = 0;
          bool result;
          object position;
          while ((line = reader.ReadLine()) != null)
          {
            lineIndex++;
            // Skip commented out lines
            if (line.StartsWith("//") || line.StartsWith(';') || line.StartsWith('#') || string.IsNullOrWhiteSpace(line)) continue;

            string[] text = line.Split(';')[0].Split('=', StringSplitOptions.TrimEntries);
            if (text.Length < 2 || string.IsNullOrWhiteSpace(text[1]))
            {
              // MainWindow.ConsoleWarning($">> Bad {FILENAME} line: {lineIndex}.");
              continue;
            }
            object key;
            if (Enum.TryParse(typeof(Keys), text[0], out key))
            {
              switch ((Keys)key)
              {
                case Keys.ChannelName:
                  Data[(Keys)key] = text[1].Trim().ToLower();
                  break;

                case Keys.ConsoleVisible:
                  if (bool.TryParse(text[1], out result)) ConsoleVisible = result;
                  break;

                case Keys.PeriodicMessageTimeInterval:
                  if (TimeSpan.TryParse(text[1], out TimeSpan timeSpan))
                  {
                    if (timeSpan.TotalSeconds > 0) Chat.PeriodicMessageInterval = timeSpan;
                  }
                  break;

                case Keys.Follow_Enable:
                  if (bool.TryParse(text[1], out result)) Notifications.ConfigFollow.Enable = result;
                  break;
                case Keys.Follow_ChatMessage:
                  Notifications.ConfigFollow.ChatMessage = text[1].Trim();
                  break;
                case Keys.Follow_TextToDisplay:
                  Notifications.ConfigFollow.TextToDisplay = text[1].Trim();
                  break;
                case Keys.Follow_TextPosition:
                  if (Enum.TryParse(typeof(Notifications.TextPosition), text[1].Trim(), out position)) Notifications.ConfigFollow.TextPosition = (Notifications.TextPosition)position;
                  break;
                case Keys.Follow_TextToSpeech:
                  Notifications.ConfigFollow.TextToSpeech = text[1].Trim();
                  break;
                case Keys.Follow_SoundToPlay:
                  Notifications.ConfigFollow.SoundToPlay = $"Resources\\{text[1].Trim()}";
                  break;
                case Keys.Follow_VideoToPlay:
                  Notifications.ConfigFollow.VideoToPlay = $"Resources\\{text[1].Trim()}";
                  break;

                case Keys.Subscription_Enable:
                  if (bool.TryParse(text[1], out result)) Notifications.ConfigSubscription.Enable = result;
                  break;
                case Keys.Subscription_ChatMessage:
                  Notifications.ConfigSubscription.ChatMessage = text[1].Trim();
                  break;
                case Keys.Subscription_TextToDisplay:
                  Notifications.ConfigSubscription.TextToDisplay = text[1].Trim();
                  break;
                case Keys.Subscription_TextPosition:
                  if (Enum.TryParse(typeof(Notifications.TextPosition), text[1].Trim(), out position)) Notifications.ConfigSubscription.TextPosition = (Notifications.TextPosition)position;
                  break;
                case Keys.Subscription_TextToSpeech:
                  Notifications.ConfigSubscription.TextToSpeech = text[1].Trim();
                  break;
                case Keys.Subscription_SoundToPlay:
                  Notifications.ConfigSubscription.SoundToPlay = $"Resources\\{text[1].Trim()}";
                  break;
                case Keys.Subscription_VideoToPlay:
                  Notifications.ConfigSubscription.VideoToPlay = $"Resources\\{text[1].Trim()}";
                  break;

                case Keys.SubscriptionExt_Enable:
                  if (bool.TryParse(text[1], out result)) Notifications.ConfigSubscriptionExt.Enable = result;
                  break;
                case Keys.SubscriptionExt_ChatMessage:
                  Notifications.ConfigSubscriptionExt.ChatMessage = text[1].Trim();
                  break;
                case Keys.SubscriptionExt_TextToDisplay:
                  Notifications.ConfigSubscriptionExt.TextToDisplay = text[1].Trim();
                  break;
                case Keys.SubscriptionExt_TextPosition:
                  if (Enum.TryParse(typeof(Notifications.TextPosition), text[1].Trim(), out position)) Notifications.ConfigSubscriptionExt.TextPosition = (Notifications.TextPosition)position;
                  break;
                case Keys.SubscriptionExt_TextToSpeech:
                  Notifications.ConfigSubscriptionExt.TextToSpeech = text[1].Trim();
                  break;
                case Keys.SubscriptionExt_SoundToPlay:
                  Notifications.ConfigSubscriptionExt.SoundToPlay = $"Resources\\{text[1].Trim()}";
                  break;
                case Keys.SubscriptionExt_VideoToPlay:
                  Notifications.ConfigSubscriptionExt.VideoToPlay = $"Resources\\{text[1].Trim()}";
                  break;

                case Keys.SubscriptionGift_Enable:
                  if (bool.TryParse(text[1], out result)) Notifications.ConfigSubscriptionGift.Enable = result;
                  break;
                case Keys.SubscriptionGift_ChatMessage:
                  Notifications.ConfigSubscriptionGift.ChatMessage = text[1].Trim();
                  break;
                case Keys.SubscriptionGift_TextToDisplay:
                  Notifications.ConfigSubscriptionGift.TextToDisplay = text[1].Trim();
                  break;
                case Keys.SubscriptionGift_TextPosition:
                  if (Enum.TryParse(typeof(Notifications.TextPosition), text[1].Trim(), out position)) Notifications.ConfigSubscriptionGift.TextPosition = (Notifications.TextPosition)position;
                  break;
                case Keys.SubscriptionGift_TextToSpeech:
                  Notifications.ConfigSubscriptionGift.TextToSpeech = text[1].Trim();
                  break;
                case Keys.SubscriptionGift_SoundToPlay:
                  Notifications.ConfigSubscriptionGift.SoundToPlay = $"Resources\\{text[1].Trim()}";
                  break;
                case Keys.SubscriptionGift_VideoToPlay:
                  Notifications.ConfigSubscriptionGift.VideoToPlay = $"Resources\\{text[1].Trim()}";
                  break;

                case Keys.SubscriptionGiftReceived_Enable:
                  if (bool.TryParse(text[1], out result)) Notifications.ConfigSubscriptionGiftReceived.Enable = result;
                  break;
                case Keys.SubscriptionGiftReceived_ChatMessage:
                  Notifications.ConfigSubscriptionGiftReceived.ChatMessage = text[1].Trim();
                  break;
                case Keys.SubscriptionGiftReceived_TextToDisplay:
                  Notifications.ConfigSubscriptionGiftReceived.TextToDisplay = text[1].Trim();
                  break;
                case Keys.SubscriptionGiftReceived_TextPosition:
                  if (Enum.TryParse(typeof(Notifications.TextPosition), text[1].Trim(), out position)) Notifications.ConfigSubscriptionGiftReceived.TextPosition = (Notifications.TextPosition)position;
                  break;
                case Keys.SubscriptionGiftReceived_TextToSpeech:
                  Notifications.ConfigSubscriptionGiftReceived.TextToSpeech = text[1].Trim();
                  break;
                case Keys.SubscriptionGiftReceived_SoundToPlay:
                  Notifications.ConfigSubscriptionGiftReceived.SoundToPlay = $"Resources\\{text[1].Trim()}";
                  break;
                case Keys.SubscriptionGiftReceived_VideoToPlay:
                  Notifications.ConfigSubscriptionGiftReceived.VideoToPlay = $"Resources\\{text[1].Trim()}";
                  break;

                case Keys.Cheer_Enable:
                  if (bool.TryParse(text[1], out result)) Notifications.ConfigCheer.Enable = result;
                  break;
                case Keys.Cheer_ChatMessage:
                  Notifications.ConfigCheer.ChatMessage = text[1].Trim();
                  break;
                case Keys.Cheer_TextToDisplay:
                  Notifications.ConfigCheer.TextToDisplay = text[1].Trim();
                  break;
                case Keys.Cheer_TextPosition:
                  if (Enum.TryParse(typeof(Notifications.TextPosition), text[1].Trim(), out position)) Notifications.ConfigCheer.TextPosition = (Notifications.TextPosition)position;
                  break;
                case Keys.Cheer_TextToSpeech:
                  Notifications.ConfigCheer.TextToSpeech = text[1].Trim();
                  break;
                case Keys.Cheer_SoundToPlay:
                  Notifications.ConfigCheer.SoundToPlay = $"Resources\\{text[1].Trim()}";
                  break;
                case Keys.Cheer_VideoToPlay:
                  Notifications.ConfigCheer.VideoToPlay = $"Resources\\{text[1].Trim()}";
                  break;

                case Keys.msg:
                  // If there are '=' symbols in the message the previous splitting would brake it
                  string msg = line.Substring(line.IndexOf('=') + 1).Trim();
                  if (msg.Length > 0) Chat.PeriodicMessages.Add(msg);
                  break;

                case Keys.ChannelPoints_RandomVideo:
                  Data[(Keys)key] = text[1].Trim();
                  break;

                default:
                  if (Data.ContainsKey((Keys)key)) { Data[(Keys)key] = text[1].Trim(); }
                  else MainWindow.ConsoleWarning($">> Not recognized key '{text[0]}' on line {lineIndex} in Config.ini file.");
                  break;
              }
            }
            else { MainWindow.ConsoleWarning($">> Not recognized key '{text[0]}' on line {lineIndex} in Config.ini file."); }
          }
        }

        // Check if all needed data was read
        if (Data[Keys.ChannelName].Length == 0)
        {
          MainWindow.ConsoleWarning(string.Concat(
            $">> Missing required info in {FILENAME} file.", Environment.NewLine,
            $"Look inside \"Required information in {FILENAME}\" section in README for help.", Environment.NewLine,
            $"You can delete {FILENAME} file to generate new one. ! WARNING - ALL DATA INSIDE IT WILL BE LOST !"
          ));
          return true;
        }

        // Nothing is missing, do some setup things
        MainWindow.ConsoleWarning($">> Loaded {Chat.PeriodicMessages.Count} periodic messages.");
      }

      if (!reload) LoadVolumesFile();
      ConfigFileTimestamp = configFile.LastWriteTime;

      return false;
    }

    private static void CreateConfigFile(FileInfo file)
    {
      using (StreamWriter writer = new(file.FullName))
      {
        writer.WriteLine("; Required things, needs to be filled in.");
        writer.WriteLine("; Channel name the bot should connect to.");
        writer.WriteLine(string.Concat(Keys.ChannelName.ToString(), " = "));

        writer.WriteLine();
        writer.WriteLine();
        writer.WriteLine("; Sound samples such as farts, laughs, applauses, etc. should be placed in the \"Resources/Sounds\" folder,");
        writer.WriteLine("; to be used in TTS messages like this \"!tts -laugh\" - calling the file name with \"-\" symbol in front of it.");
        writer.WriteLine(";");
        writer.WriteLine("; Videos that can be played via random video channel point redemption should be placed in the \"Resources/Videos\" folder.");

        writer.WriteLine();
        writer.WriteLine();
        writer.WriteLine("; --------------------------------------------------");
        writer.WriteLine("; Additional things, can be left unchanged.");
        writer.WriteLine("; --------------------------------------------------");

        writer.WriteLine();
        writer.WriteLine();
        writer.WriteLine("; Internal bot configuration.");
        writer.WriteLine("; Debug console visibility. Default: false");
        writer.WriteLine("ConsoleVisible = false");
        writer.WriteLine("; Periodic messages time interval (HH:MM:SS format -> 1 hour: 1:00:00, 1 minute 0:01:00, 1 second: 0:00:01). Default: empty - 10 minutes");
        writer.WriteLine("PeriodicMessageTimeInterval = ");

        writer.WriteLine();
        writer.WriteLine();
        writer.WriteLine("; Event messages configuration.");
        writer.WriteLine("; TextPosition - available options: TOP, MIDDLE, BOTTOM.");
        writer.WriteLine("; Empty assignment means that this part of the notification would be disabled.");
        writer.WriteLine("; Paths to sounds and videos are relative to Resources folder.");
        writer.WriteLine("; For a file that is placed directly in Resources folder it would be just a filename with it's extension.");
        writer.WriteLine("; Use below expressions to insert data into messages:");
        writer.WriteLine("; {0} - user name that followed, subscribed, gifted subscriptions, etc.");
        writer.WriteLine("; {1} - subscription tier");
        writer.WriteLine("; {2} - subscription duration in advance, for example subscribed for next 3 months (only in extended subscription message)");
        writer.WriteLine("; {3} - subscription streak, for example subscribed for 10 months in a row (only in extended subscription message)");
        writer.WriteLine("; {4} - gifted subscription count, cheered bits count");
        writer.WriteLine("; {5} - cumulative subscription months (only in extended subscription message)");
        writer.WriteLine("; {7} - attached message");
        writer.WriteLine("; Using index out of supported range WILL CRASH THE BOT!");
        writer.WriteLine(";");
        writer.WriteLine("; ----- Follow");
        writer.WriteLine(string.Concat(Keys.Follow_Enable.ToString(), " = true"));
        writer.WriteLine(string.Concat(Keys.Follow_ChatMessage.ToString(), " = @{0} thank you for following!"));
        writer.WriteLine(string.Concat(Keys.Follow_TextToDisplay.ToString(), " = New follower {0}!"));
        writer.WriteLine(string.Concat(Keys.Follow_TextPosition.ToString(), " = TOP"));
        writer.WriteLine(string.Concat(Keys.Follow_TextToSpeech.ToString(), " = "));
        writer.WriteLine(string.Concat(Keys.Follow_SoundToPlay.ToString(), " = tone1.wav"));
        writer.WriteLine(string.Concat(Keys.Follow_VideoToPlay.ToString(), " = "));
        writer.WriteLine(";");
        writer.WriteLine("; ----- Subscription");
        writer.WriteLine(string.Concat(Keys.Subscription_Enable.ToString(), " = true"));
        writer.WriteLine(string.Concat(Keys.Subscription_ChatMessage.ToString(), " = "));
        writer.WriteLine(string.Concat(Keys.Subscription_TextToDisplay.ToString(), " = {0} just subscribed!\\n{7}"));
        writer.WriteLine(string.Concat(Keys.Subscription_TextPosition.ToString(), " = BOTTOM"));
        writer.WriteLine(string.Concat(Keys.Subscription_TextToSpeech.ToString(), " = Thank you {0} for tier {1} sub! {7}"));
        writer.WriteLine(string.Concat(Keys.Subscription_SoundToPlay.ToString(), " = "));
        writer.WriteLine(string.Concat(Keys.Subscription_VideoToPlay.ToString(), " = peepoHey.mp4"));
        writer.WriteLine(";");
        writer.WriteLine("; ----- Subscription Extended Message (this message is rare?)");
        writer.WriteLine(string.Concat(Keys.SubscriptionExt_Enable.ToString(), " = true"));
        writer.WriteLine(string.Concat(Keys.SubscriptionExt_ChatMessage.ToString(), " = "));
        writer.WriteLine(string.Concat(Keys.SubscriptionExt_TextToDisplay.ToString(), " = {0} just subscribed!\\n{7}"));
        writer.WriteLine(string.Concat(Keys.SubscriptionExt_TextPosition.ToString(), " = BOTTOM"));
        writer.WriteLine(string.Concat(Keys.SubscriptionExt_TextToSpeech.ToString(), " = Thank you {0} for {2} months in advance tier {1} sub! It is your {3} month in a row! {7}"));
        writer.WriteLine(string.Concat(Keys.SubscriptionExt_SoundToPlay.ToString(), " = "));
        writer.WriteLine(string.Concat(Keys.SubscriptionExt_VideoToPlay.ToString(), " = peepoHey.mp4"));
        writer.WriteLine(";");
        writer.WriteLine("; ----- Subscription gifted (the user is gifting subscriptions)");
        writer.WriteLine(string.Concat(Keys.SubscriptionGift_Enable.ToString(), " = true"));
        writer.WriteLine(string.Concat(Keys.SubscriptionGift_ChatMessage.ToString(), " = "));
        writer.WriteLine(string.Concat(Keys.SubscriptionGift_TextToDisplay.ToString(), " = Thank you {0} for {4} subs!\\n{7}"));
        writer.WriteLine(string.Concat(Keys.SubscriptionGift_TextPosition.ToString(), " = BOTTOM"));
        writer.WriteLine(string.Concat(Keys.SubscriptionGift_TextToSpeech.ToString(), " = Thank you {0} for gifting {4} tier {1} subs! {7}"));
        writer.WriteLine(string.Concat(Keys.SubscriptionGift_SoundToPlay.ToString(), " = "));
        writer.WriteLine(string.Concat(Keys.SubscriptionGift_VideoToPlay.ToString(), " = peepoHey.mp4"));
        writer.WriteLine(";");
        writer.WriteLine("; ----- Subscription gift received (the user received subscription gift, these events are sent before or after subscription gifted event)");
        writer.WriteLine(string.Concat(Keys.SubscriptionGiftReceived_Enable.ToString(), " = false"));
        writer.WriteLine(string.Concat(Keys.SubscriptionGiftReceived_ChatMessage.ToString(), " = "));
        writer.WriteLine(string.Concat(Keys.SubscriptionGiftReceived_TextToDisplay.ToString(), " = New subscriber {0}!"));
        writer.WriteLine(string.Concat(Keys.SubscriptionGiftReceived_TextPosition.ToString(), " = TOP"));
        writer.WriteLine(string.Concat(Keys.SubscriptionGiftReceived_TextToSpeech.ToString(), " = "));
        writer.WriteLine(string.Concat(Keys.SubscriptionGiftReceived_SoundToPlay.ToString(), " = tone1.wav"));
        writer.WriteLine(string.Concat(Keys.SubscriptionGiftReceived_VideoToPlay.ToString(), " = "));
        writer.WriteLine(";");
        writer.WriteLine("; ----- Bits cheer");
        writer.WriteLine(string.Concat(Keys.Cheer_Enable.ToString(), " = true"));
        writer.WriteLine(string.Concat(Keys.Cheer_ChatMessage.ToString(), " = "));
        writer.WriteLine(string.Concat(Keys.Cheer_TextToDisplay.ToString(), " = Thank you {0} for {4} bits!\\n{7}"));
        writer.WriteLine(string.Concat(Keys.Cheer_TextPosition.ToString(), " = TOP"));
        writer.WriteLine(string.Concat(Keys.Cheer_TextToSpeech.ToString(), " = Thank you {0} for {4} bits! {7}"));
        writer.WriteLine(string.Concat(Keys.Cheer_SoundToPlay.ToString(), " = tone1.wav"));
        writer.WriteLine(string.Concat(Keys.Cheer_VideoToPlay.ToString(), " = "));

        writer.WriteLine();
        writer.WriteLine();
        writer.WriteLine("; Channel points redemptions. Assign channel point redemption ID.");
        writer.WriteLine("; Plays random video from Resources/Videos folder.");
        writer.WriteLine(string.Concat(Keys.ChannelPoints_RandomVideo.ToString(), " = "));

        writer.WriteLine();
        writer.WriteLine();
        writer.WriteLine("; Periodic messages (one message per line, each starting with \"msg = \"), can be left empty");
        writer.WriteLine("; msg = Commented out periodic message (deactivated) peepoSad");
      }

      // Notify the user
      MainWindow.ConsoleWarning(string.Concat(
        $">> Missing required info in {FILENAME} file.", Environment.NewLine,
        "The file was generated.", Environment.NewLine,
        "Please fill it up and restart the bot."
      ));
    }

    public static bool IsConfigFileUpdated()
    {
      FileInfo file = new(FILENAME);
      if (file.Exists)
      {
        return file.LastWriteTime != ConfigFileTimestamp;
      }

      return false;
    }

    private static void LoadVolumesFile()
    {
      FileInfo file = new(VOLUMESFILENAME);
      if (file.Exists)
      {
        var data = File.ReadAllLines(file.FullName);
        if (data is null || data.Length != 3) { return; }

        float volume;
        if (float.TryParse(data[0], out volume)) { VolumeTTS = MathF.Round(volume / 100f, 2); }
        if (float.TryParse(data[1], out volume)) { VolumeSounds = MathF.Round(volume / 100f, 2); }
        if (float.TryParse(data[2], out volume)) { VolumeVideos = MathF.Round(volume / 100f, 2); }

        MainWindow.I.SetVolumeSliderValues();
      }
    }

    public static void UpdateVolumesFile()
    {
      if (!VolumeValuesDirty) return;
      VolumeValuesDirty = false;

      MainWindow.ConsoleWarning(">> Updating volumes file.");

      string[] data = new string[] { MathF.Round(VolumeTTS * 100).ToString(), MathF.Round(VolumeSounds * 100).ToString(), MathF.Round(VolumeVideos * 100).ToString() };

      try { File.WriteAllLines(VOLUMESFILENAME, data); }
      catch (Exception ex) { MainWindow.ConsoleWarning($">> {ex.Message}"); }
    }
  }
}
