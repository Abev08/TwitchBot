using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Windows.Input;

namespace AbevBot;

public static class Config
{
  public enum Keys
  {
    ChannelName, ChannelID,
    ConsoleVisible, PeriodicMessageTimeInterval, PeriodicMessageMinChatMessages, StartVideoEnabled,
    AlwaysReadTTSFromThem, OverpoweredInFight,
    SongRequestTimeout,
    DiscordMessageOnline,
    HotkeyNotificationPause, HotkeyNotificationSkip,

    Follow_Enable, Follow_ChatMessage, Follow_TextToDisplay, Follow_TextPosition, Follow_TextToSpeech, Follow_SoundToPlay, Follow_VideoToPlay,
    Subscription_Enable, Subscription_ChatMessage, Subscription_TextToDisplay, Subscription_TextPosition, Subscription_TextToSpeech, Subscription_SoundToPlay, Subscription_VideoToPlay,
    SubscriptionExt_Enable, SubscriptionExt_ChatMessage, SubscriptionExt_TextToDisplay, SubscriptionExt_TextPosition, SubscriptionExt_TextToSpeech, SubscriptionExt_SoundToPlay, SubscriptionExt_VideoToPlay,
    SubscriptionGift_Enable, SubscriptionGift_ChatMessage, SubscriptionGift_TextToDisplay, SubscriptionGift_TextPosition, SubscriptionGift_TextToSpeech, SubscriptionGift_SoundToPlay, SubscriptionGift_VideoToPlay,
    SubscriptionGiftReceived_Enable, SubscriptionGiftReceived_ChatMessage, SubscriptionGiftReceived_TextToDisplay, SubscriptionGiftReceived_TextPosition, SubscriptionGiftReceived_TextToSpeech, SubscriptionGiftReceived_SoundToPlay, SubscriptionGiftReceived_VideoToPlay,
    Cheer_Enable, Cheer_ChatMessage, Cheer_TextToDisplay, Cheer_TextPosition, Cheer_TextToSpeech, Cheer_SoundToPlay, Cheer_VideoToPlay, Cheer_MinimumBitsForTTS,
    Raid_Enable, Raid_ChatMessage, Raid_TextToDisplay, Raid_TextPosition, Raid_TextToSpeech, Raid_SoundToPlay, Raid_VideoToPlay, Raid_MinimumRaiders, Raid_DoShoutout,

    ChannelRedemption_RandomVideo_ID, ChannelRedemption_RandomVideo_MarkAsFulfilled, ChannelRedemption_SongRequest_ID, ChannelRedemption_SongRequest_MarkAsFulfilled, ChannelRedemption_SongSkip_ID, ChannelRedemption_SongSkip_MarkAsFulfilled,
    ChannelRedemption_ID, ChannelRedemption_KeyAction, ChannelRedemption_KeyActionType, ChannelRedemption_KeyActionAfterTime, ChannelRedemption_KeyActionAfterTimeType, ChannelRedemption_ChatMessage, ChannelRedemption_TextToDisplay, ChannelRedemption_TextPosition, ChannelRedemption_TextToSpeech, ChannelRedemption_SoundToPlay, ChannelRedemption_VideoToPlay, ChannelRedemption_MarkAsFulfilled,
    msg
  };

  private const string FILENAME = "Config.ini";

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
  public static bool StartVideoEnabled { get; private set; } = true;
  public static bool VolumeValuesDirty { get; set; }
  public static float VolumeTTS { get; set; }
  public static float VolumeSounds { get; set; }
  public static float VolumeVideos { get; set; }
  private static DateTime ConfigFileTimestamp;
  public static DateTime BroadcasterLastOnline;
  public static DateTime BroadcasterLastOnlineCheck { get; set; }
  public static TimeSpan BroadcasterOnlineCheckInterval { get; } = TimeSpan.FromMinutes(3);
  public static TimeSpan BroadcasterOfflineTimeout { get; } = TimeSpan.FromMinutes(30);
  public static readonly List<Key> HotkeysForPauseNotification = new();
  public static readonly List<Key> HotkeysForSkipNotification = new();

  public static bool ParseConfigFile(bool reload = false)
  {
    if (reload) { MainWindow.ConsoleWarning($">> Reloading {FILENAME} file."); }
    else { MainWindow.ConsoleWarning($">> Reading {FILENAME} file."); }

    // Create resources folders just for user convenience (if they don't exist)
    DirectoryInfo dir = new("Resources/Sounds");
    if (!dir.Exists) dir.Create();
    dir = new DirectoryInfo("Resources/Videos");
    if (!dir.Exists) dir.Create();

    Chat.PeriodicMessages.Clear(); // Clear previous preiodic messages
    Chatter.AlwaysReadTTSFromThem.Clear();
    Chatter.OverpoweredInFight.Clear();
    Notifications.ChannelRedemptions.Clear();
    HotkeysForPauseNotification.Clear();
    HotkeysForSkipNotification.Clear();

    // Create example Config.ini
    FileInfo configFile = new("Config_example.ini");
    CreateConfigFile(configFile, true);

    // Create real Config.ini
    configFile = new(FILENAME);
    if (configFile.Exists == false)
    {
      CreateConfigFile(configFile);
      return true;
    }
    else
    {
      string line;
      int lineIndex = 0, indexOf;
      bool result;
      object position;
      string[] text = new string[2];
      string temp;
      int temp2;
      ChannelRedemption redemption = null;
      string[] keys;
      TimeSpan timeSpan;

      // FileShare.ReadWrite needs to be used because it have to allow other processes to write into the file
      using FileStream fileStream = new(configFile.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
      using StreamReader reader = new(fileStream);
      while ((line = reader.ReadLine()) != null)
      {
        lineIndex++;
        // Skip commented out lines
        if (line.StartsWith("//") || line.StartsWith(';') || line.StartsWith('#') || string.IsNullOrWhiteSpace(line)) continue;

        indexOf = line.IndexOf('=');
        if (indexOf > 0)
        {
          text[0] = line[..indexOf].Trim();
          text[1] = line[(indexOf + 1)..].Trim();
          indexOf = text[1].IndexOf(';');
          if (indexOf >= 0) text[1] = text[1][..indexOf].Trim();
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
              if (TimeSpan.TryParse(text[1], out timeSpan))
              {
                if (timeSpan.TotalSeconds > 0) Chat.PeriodicMessageInterval = timeSpan;
              }
              break;

            case Keys.PeriodicMessageMinChatMessages:
              if (int.TryParse(text[1], out temp2))
              {
                if (temp2 >= 0) Chat.PeriodicMessageMinChatMessages = temp2;
              }
              break;

            case Keys.SongRequestTimeout:
              if (TimeSpan.TryParse(text[1], out timeSpan))
              {
                Spotify.SongRequestTimeout = timeSpan;
              }
              break;

            case Keys.StartVideoEnabled:
              if (bool.TryParse(text[1], out result)) StartVideoEnabled = result;
              break;

            case Keys.DiscordMessageOnline:
              Discord.CustomOnlineMessage = text[1].Trim();
              break;

            case Keys.HotkeyNotificationPause:
              keys = text[1].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
              foreach (string k in keys)
              {
                if (Enum.TryParse(typeof(Key), k, out position)) HotkeysForPauseNotification.Add((Key)position);
                else MainWindow.ConsoleWarning($">> Keycode: {k} not recognized in line {lineIndex} in Config.ini file.");
              }
              break;

            case Keys.HotkeyNotificationSkip:
              keys = text[1].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
              foreach (string k in keys)
              {
                if (Enum.TryParse(typeof(Key), k, out position)) HotkeysForSkipNotification.Add((Key)position);
                else MainWindow.ConsoleWarning($">> Keycode: {k} not recognized in line {lineIndex} in Config.ini file.");
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
              if (text[1].Length > 0) Notifications.ConfigFollow.SoundToPlay = $"Resources\\{text[1].Trim()}";
              break;
            case Keys.Follow_VideoToPlay:
              if (text[1].Length > 0) Notifications.ConfigFollow.VideoToPlay = $"Resources\\{text[1].Trim()}";
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
              if (text[1].Length > 0) Notifications.ConfigSubscription.SoundToPlay = $"Resources\\{text[1].Trim()}";
              break;
            case Keys.Subscription_VideoToPlay:
              if (text[1].Length > 0) Notifications.ConfigSubscription.VideoToPlay = $"Resources\\{text[1].Trim()}";
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
              if (text[1].Length > 0) Notifications.ConfigSubscriptionExt.SoundToPlay = $"Resources\\{text[1].Trim()}";
              break;
            case Keys.SubscriptionExt_VideoToPlay:
              if (text[1].Length > 0) Notifications.ConfigSubscriptionExt.VideoToPlay = $"Resources\\{text[1].Trim()}";
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
              if (text[1].Length > 0) Notifications.ConfigSubscriptionGift.SoundToPlay = $"Resources\\{text[1].Trim()}";
              break;
            case Keys.SubscriptionGift_VideoToPlay:
              if (text[1].Length > 0) Notifications.ConfigSubscriptionGift.VideoToPlay = $"Resources\\{text[1].Trim()}";
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
              if (text[1].Length > 0) Notifications.ConfigSubscriptionGiftReceived.SoundToPlay = $"Resources\\{text[1].Trim()}";
              break;
            case Keys.SubscriptionGiftReceived_VideoToPlay:
              if (text[1].Length > 0) Notifications.ConfigSubscriptionGiftReceived.VideoToPlay = $"Resources\\{text[1].Trim()}";
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
              if (text[1].Length > 0) Notifications.ConfigCheer.SoundToPlay = $"Resources\\{text[1].Trim()}";
              break;
            case Keys.Cheer_VideoToPlay:
              if (text[1].Length > 0) Notifications.ConfigCheer.VideoToPlay = $"Resources\\{text[1].Trim()}";
              break;
            case Keys.Cheer_MinimumBitsForTTS:
              if (text[1].Length > 0 && int.TryParse(text[1], out temp2)) Notifications.ConfigCheer.MinimumBits = temp2;
              break;

            case Keys.Raid_Enable:
              if (bool.TryParse(text[1], out result)) Notifications.ConfigRaid.Enable = result;
              break;
            case Keys.Raid_ChatMessage:
              Notifications.ConfigRaid.ChatMessage = text[1].Trim();
              break;
            case Keys.Raid_TextToDisplay:
              Notifications.ConfigRaid.TextToDisplay = text[1].Trim();
              break;
            case Keys.Raid_TextPosition:
              if (Enum.TryParse(typeof(Notifications.TextPosition), text[1].Trim(), out position)) Notifications.ConfigRaid.TextPosition = (Notifications.TextPosition)position;
              break;
            case Keys.Raid_TextToSpeech:
              Notifications.ConfigRaid.TextToSpeech = text[1].Trim();
              break;
            case Keys.Raid_SoundToPlay:
              if (text[1].Length > 0) Notifications.ConfigRaid.SoundToPlay = $"Resources\\{text[1].Trim()}";
              break;
            case Keys.Raid_VideoToPlay:
              if (text[1].Length > 0) Notifications.ConfigRaid.VideoToPlay = $"Resources\\{text[1].Trim()}";
              break;
            case Keys.Raid_MinimumRaiders:
              if (int.TryParse(text[1], out temp2) && temp2 > 0) Notifications.ConfigRaid.MinimumRaiders = temp2;
              break;
            case Keys.Raid_DoShoutout:
              if (bool.TryParse(text[1], out result)) Notifications.ConfigRaid.DoShoutout = result;
              break;

            case Keys.ChannelRedemption_ID:
              if (text[1].Length > 0)
              {
                temp = text[1].Trim();
                redemption = null;
                for (int i = 0; i < Notifications.ChannelRedemptions.Count; i++)
                {
                  if (Notifications.ChannelRedemptions[i].ID.Equals(temp))
                  {
                    redemption = Notifications.ChannelRedemptions[i];
                    break;
                  }
                }
                if (redemption is null)
                {
                  redemption = new() { ID = temp };
                  Notifications.ChannelRedemptions.Add(redemption);
                }
              }
              break;
            case Keys.ChannelRedemption_KeyAction:
              if (text[1].Length == 0) continue;
              if (redemption is null)
              {
                MainWindow.ConsoleWarning($">> Bad config line {lineIndex}. Missing previous ID filed declaration!");
                continue;
              }
              keys = text[1].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
              foreach (string k in keys)
              {
                if (Enum.TryParse(typeof(Key), k, out position)) redemption.KeysToPress.Add((Key)position);
                else MainWindow.ConsoleWarning($">> Keycode: {k} not recognized in line {lineIndex} in Config.ini file.");
              }
              break;
            case Keys.ChannelRedemption_KeyActionType:
              if (text[1].Length == 0) continue;
              if (redemption is null)
              {
                MainWindow.ConsoleWarning($">> Bad config line {lineIndex}. Missing previous ID filed declaration!");
                continue;
              }
              if (Enum.TryParse(typeof(KeyActionType), text[1].Trim(), out position)) redemption.KeysToPressType = (KeyActionType)position;
              break;
            case Keys.ChannelRedemption_KeyActionAfterTime:
              if (text[1].Length == 0) continue;
              if (redemption is null)
              {
                MainWindow.ConsoleWarning($">> Bad config line {lineIndex}. Missing previous ID filed declaration!");
                continue;
              }
              keys = text[1].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
              if (int.TryParse(keys[0], out temp2)) redemption.TimeToPressSecondAction = TimeSpan.FromMilliseconds(temp2);
              else MainWindow.ConsoleWarning($">> Action time {keys[0]} not recognized in line {lineIndex} in Config.ini file.");
              keys[0] = string.Empty;
              foreach (string k in keys)
              {
                if (k?.Length == 0) continue;
                if (Enum.TryParse(typeof(Key), k, out position)) redemption.KeysToPressAfterTime.Add((Key)position);
                else MainWindow.ConsoleWarning($">> Keycode: {k} not recognized in line {lineIndex} in Config.ini file.");
              }
              break;
            case Keys.ChannelRedemption_KeyActionAfterTimeType:
              if (text[1].Length == 0) continue;
              if (redemption is null)
              {
                MainWindow.ConsoleWarning($">> Bad config line {lineIndex}. Missing previous ID filed declaration!");
                continue;
              }
              if (Enum.TryParse(typeof(KeyActionType), text[1].Trim(), out position)) redemption.KeysToPressAfterTimeType = (KeyActionType)position;
              break;
            case Keys.ChannelRedemption_ChatMessage:
              if (text[1].Length == 0) continue;
              if (redemption is null)
              {
                MainWindow.ConsoleWarning($">> Bad config line {lineIndex}. Missing previous ID filed declaration!");
                continue;
              }
              redemption.Config.ChatMessage = text[1].Trim();
              break;
            case Keys.ChannelRedemption_TextToDisplay:
              if (text[1].Length == 0) continue;
              if (redemption is null)
              {
                MainWindow.ConsoleWarning($">> Bad config line {lineIndex}. Missing previous ID filed declaration!");
                continue;
              }
              redemption.Config.TextToDisplay = text[1].Trim();
              break;
            case Keys.ChannelRedemption_TextPosition:
              if (text[1].Length == 0) continue;
              if (redemption is null)
              {
                MainWindow.ConsoleWarning($">> Bad config line {lineIndex}. Missing previous ID filed declaration!");
                continue;
              }
              if (Enum.TryParse(typeof(Notifications.TextPosition), text[1].Trim(), out position)) redemption.Config.TextPosition = (Notifications.TextPosition)position;
              break;
            case Keys.ChannelRedemption_TextToSpeech:
              if (text[1].Length == 0) continue;
              if (redemption is null)
              {
                MainWindow.ConsoleWarning($">> Bad config line {lineIndex}. Missing previous ID filed declaration!");
                continue;
              }
              redemption.Config.TextToSpeech = text[1].Trim();
              break;
            case Keys.ChannelRedemption_SoundToPlay:
              if (text[1].Length == 0) continue;
              if (redemption is null)
              {
                MainWindow.ConsoleWarning($">> Bad config line {lineIndex}. Missing previous ID filed declaration!");
                continue;
              }
              if (text[1].Length > 0) redemption.Config.SoundToPlay = $"Resources\\{text[1].Trim()}";
              break;
            case Keys.ChannelRedemption_VideoToPlay:
              if (text[1].Length == 0) continue;
              if (redemption is null)
              {
                MainWindow.ConsoleWarning($">> Bad config line {lineIndex}. Missing previous ID filed declaration!");
                continue;
              }
              if (text[1].Length > 0) redemption.Config.VideoToPlay = $"Resources\\{text[1].Trim()}";
              break;
            case Keys.ChannelRedemption_MarkAsFulfilled:
              if (redemption is null)
              {
                MainWindow.ConsoleWarning($">> Bad config line {lineIndex}. Missing previous ID filed declaration!");
                continue;
              }
              if (bool.TryParse(text[1], out result)) redemption.MarkAsFulfilled = result;
              break;

            case Keys.msg:
              // If there are '=' symbols in the message the previous splitting would brake it
              string msg = line.Substring(line.IndexOf('=') + 1).Trim();
              if (msg.Length > 0) Chat.PeriodicMessages.Add(msg);
              break;

            case Keys.ChannelRedemption_RandomVideo_ID:
            case Keys.ChannelRedemption_SongRequest_ID:
            case Keys.ChannelRedemption_SongSkip_ID:
              Data[(Keys)key] = text[1].Trim();
              break;

            case Keys.ChannelRedemption_RandomVideo_MarkAsFulfilled:
            case Keys.ChannelRedemption_SongRequest_MarkAsFulfilled:
            case Keys.ChannelRedemption_SongSkip_MarkAsFulfilled:
              if (bool.TryParse(text[1], out result)) Data[(Keys)key] = result.ToString();
              break;

            case Keys.AlwaysReadTTSFromThem:
              Chatter.AlwaysReadTTSFromThem.AddRange(text[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
              break;

            case Keys.OverpoweredInFight:
              Chatter.OverpoweredInFight.AddRange(text[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
              break;

            default:
              if (Data.ContainsKey((Keys)key)) { Data[(Keys)key] = text[1].Trim(); }
              else MainWindow.ConsoleWarning($">> Not recognized key '{text[0]}' on line {lineIndex} in Config.ini file.");
              break;
          }
        }
        else { MainWindow.ConsoleWarning($">> Not recognized key '{text[0]}' on line {lineIndex} in Config.ini file."); }
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

    ConfigFileTimestamp = configFile.LastWriteTime;

    return false;
  }

  private static void CreateConfigFile(FileInfo file, bool example = false)
  {
    using (StreamWriter writer = new(file.FullName))
    {
      if (example)
      {
        writer.WriteLine();
        writer.WriteLine(";                               CAUTION");
        writer.WriteLine("; ----------------------------------------------------------------------");
        writer.WriteLine("; ---------------------------------------------------------------------");
        writer.WriteLine("; EXAMPLE CONFIG.INI FILE, WILL BE OVERRIDEN EVERY TIME THE BOT IS RUN");
        writer.WriteLine("; ---------------------------------------------------------------------");
        writer.WriteLine("; ----------------------------------------------------------------------");
        writer.WriteLine();
        writer.WriteLine();
      }

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
      writer.WriteLine(string.Concat(Keys.ConsoleVisible.ToString(), " = false"));
      writer.WriteLine("; Periodic messages time interval (HH:MM:SS format -> 1 hour: 1:00:00, 1 minute 0:01:00, 1 second: 0:00:01). Default: empty - 10 minutes");
      writer.WriteLine(string.Concat(Keys.PeriodicMessageTimeInterval.ToString(), " = "));
      writer.WriteLine("; Minimum number of chat messages between periodic messages. Default: empty - 10 messages. 0 - disabled");
      writer.WriteLine(string.Concat(Keys.PeriodicMessageMinChatMessages.ToString(), " = "));
      writer.WriteLine("; Starting video when the bot is run. Default: true");
      writer.WriteLine(string.Concat(Keys.StartVideoEnabled.ToString(), " = true"));
      writer.WriteLine("; Comma separated list of user names from which TTS messages will ALWAYS be read (even when !tts chat is turned off)");
      writer.WriteLine(string.Concat(Keys.AlwaysReadTTSFromThem.ToString(), " = AbevBot, Abev08"));
      writer.WriteLine("; Comma separated list of user names that will be overpowered in !fight minigame");
      writer.WriteLine(string.Concat(Keys.OverpoweredInFight.ToString(), " = Abev08"));
      writer.WriteLine("; Song request timeout from the same user (HH:MM:SS format -> 1 hour: 1:00:00, 1 minute 0:01:00, 1 second: 0:00:01). Default: empty - 2 minutes");
      writer.WriteLine(string.Concat(Keys.SongRequestTimeout.ToString(), " = "));
      writer.WriteLine("; Discord message that will be sent when the stream goes online. Default: empty - \"Hello @everyone, stream just started https://twitch.tv/{ChannelName} ! {title}\"");
      writer.WriteLine("; The \"{title}\" part of the message will be replaced with current stream title and can be used in custom online message specified below.");
      writer.WriteLine(string.Concat(Keys.DiscordMessageOnline.ToString(), " = "));

      writer.WriteLine();
      writer.WriteLine("; Hotkeys configuration");
      writer.WriteLine("; Each hotkey is comma separated list of keyboard keys that should be pressed for hotkey action to be activated.");
      writer.WriteLine("; The keys needs to be written in according to: https://learn.microsoft.com/en-us/dotnet/api/system.windows.input.key");
      writer.WriteLine("; For each hotkey - default: empty - inactive");
      writer.WriteLine(string.Concat(Keys.HotkeyNotificationPause.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.HotkeyNotificationSkip.ToString(), " = "));

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
      writer.WriteLine("; {4} - gifted subscription count, cheered bits count, raiders count");
      writer.WriteLine("; {5} - cumulative subscription months (only in extended subscription message)");
      writer.WriteLine("; {6} - comma separated list of user names that received gift subscription");
      writer.WriteLine("; {7} - attached message");
      writer.WriteLine("; {10} - if {2} > 1 then it is set to 's', otherwise empty");
      writer.WriteLine("; {11} - if {3} > 1 then it is set to 's', otherwise empty");
      writer.WriteLine("; {12} - if {4} > 1 then it is set to 's', otherwise empty");
      writer.WriteLine("; {13} - if {5} > 1 then it is set to 's', otherwise empty");
      writer.WriteLine("; Using index out of supported range WILL CRASH THE BOT!");
      writer.WriteLine();
      writer.WriteLine("; ----- Follow");
      writer.WriteLine(string.Concat(Keys.Follow_Enable.ToString(), " = true"));
      writer.WriteLine(string.Concat(Keys.Follow_ChatMessage.ToString(), " = @{0} thank you for following!"));
      writer.WriteLine(string.Concat(Keys.Follow_TextToDisplay.ToString(), " = New follower {0}!"));
      writer.WriteLine(string.Concat(Keys.Follow_TextPosition.ToString(), " = TOP"));
      writer.WriteLine(string.Concat(Keys.Follow_TextToSpeech.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.Follow_SoundToPlay.ToString(), " = tone1.wav"));
      writer.WriteLine(string.Concat(Keys.Follow_VideoToPlay.ToString(), " = "));
      writer.WriteLine();
      writer.WriteLine("; ----- Subscription (notification message, always received when someone subscribes even when the subscriber doesn't want it to be public)");
      writer.WriteLine(string.Concat(Keys.Subscription_Enable.ToString(), " = false"));
      writer.WriteLine(string.Concat(Keys.Subscription_ChatMessage.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.Subscription_TextToDisplay.ToString(), " = {0} just subscribed!"));
      writer.WriteLine(string.Concat(Keys.Subscription_TextPosition.ToString(), " = BOTTOM"));
      writer.WriteLine(string.Concat(Keys.Subscription_TextToSpeech.ToString(), " = Thank you {0} for tier {1} sub! {7}"));
      writer.WriteLine(string.Concat(Keys.Subscription_SoundToPlay.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.Subscription_VideoToPlay.ToString(), " = peepoHey.mp4"));
      writer.WriteLine();
      writer.WriteLine("; ----- Subscription Extended Message (the subscriber shares that he subscribed)");
      writer.WriteLine(string.Concat(Keys.SubscriptionExt_Enable.ToString(), " = true"));
      writer.WriteLine(string.Concat(Keys.SubscriptionExt_ChatMessage.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.SubscriptionExt_TextToDisplay.ToString(), " = {0} just subscribed!"));
      writer.WriteLine(string.Concat(Keys.SubscriptionExt_TextPosition.ToString(), " = BOTTOM"));
      writer.WriteLine(string.Concat(Keys.SubscriptionExt_TextToSpeech.ToString(), " = Thank you {0} for {2} month{10} in advance tier {1} sub! It is your {3} month in a row! {7}"));
      writer.WriteLine(string.Concat(Keys.SubscriptionExt_SoundToPlay.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.SubscriptionExt_VideoToPlay.ToString(), " = peepoHey.mp4"));
      writer.WriteLine();
      writer.WriteLine("; ----- Subscription gifted (the user is gifting subscriptions)");
      writer.WriteLine(string.Concat(Keys.SubscriptionGift_Enable.ToString(), " = true"));
      writer.WriteLine(string.Concat(Keys.SubscriptionGift_ChatMessage.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.SubscriptionGift_TextToDisplay.ToString(), " = Thank you {0} for {4} sub{12}!"));
      writer.WriteLine(string.Concat(Keys.SubscriptionGift_TextPosition.ToString(), " = BOTTOM"));
      writer.WriteLine(string.Concat(Keys.SubscriptionGift_TextToSpeech.ToString(), " = Thank you {0} for gifting {4} tier {1} sub{12} to {6}! {7}"));
      writer.WriteLine(string.Concat(Keys.SubscriptionGift_SoundToPlay.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.SubscriptionGift_VideoToPlay.ToString(), " = peepoHey.mp4"));
      writer.WriteLine();
      writer.WriteLine("; ----- Subscription gift received (the user received subscription gift, these events are sent before or after subscription gifted event)");
      writer.WriteLine(string.Concat(Keys.SubscriptionGiftReceived_Enable.ToString(), " = false"));
      writer.WriteLine(string.Concat(Keys.SubscriptionGiftReceived_ChatMessage.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.SubscriptionGiftReceived_TextToDisplay.ToString(), " = New subscriber {0}!"));
      writer.WriteLine(string.Concat(Keys.SubscriptionGiftReceived_TextPosition.ToString(), " = TOP"));
      writer.WriteLine(string.Concat(Keys.SubscriptionGiftReceived_TextToSpeech.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.SubscriptionGiftReceived_SoundToPlay.ToString(), " = tone1.wav"));
      writer.WriteLine(string.Concat(Keys.SubscriptionGiftReceived_VideoToPlay.ToString(), " = "));
      writer.WriteLine();
      writer.WriteLine("; ----- Bits cheer");
      writer.WriteLine(string.Concat(Keys.Cheer_Enable.ToString(), " = true"));
      writer.WriteLine(string.Concat(Keys.Cheer_ChatMessage.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.Cheer_TextToDisplay.ToString(), " = Thank you {0} for {4} bit{12}!"));
      writer.WriteLine(string.Concat(Keys.Cheer_TextPosition.ToString(), " = TOP"));
      writer.WriteLine(string.Concat(Keys.Cheer_TextToSpeech.ToString(), " = Thank you {0} for {4} bit{12}! {7}"));
      writer.WriteLine(string.Concat(Keys.Cheer_SoundToPlay.ToString(), " = tone1.wav"));
      writer.WriteLine(string.Concat(Keys.Cheer_VideoToPlay.ToString(), " = "));
      writer.WriteLine("; The minimum amount of bits thrown for the message to be read as TTS. Default: empty - 10");
      writer.WriteLine(string.Concat(Keys.Cheer_MinimumBitsForTTS.ToString(), " = "));
      writer.WriteLine();
      writer.WriteLine("; ----- Raid (channel got raided)");
      writer.WriteLine(string.Concat(Keys.Raid_Enable.ToString(), " = true"));
      writer.WriteLine(string.Concat(Keys.Raid_ChatMessage.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.Raid_TextToDisplay.ToString(), " = {4} raider{12} from {0} are coming!"));
      writer.WriteLine(string.Concat(Keys.Raid_TextPosition.ToString(), " = TOP"));
      writer.WriteLine(string.Concat(Keys.Raid_TextToSpeech.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.Raid_SoundToPlay.ToString(), " = tone1.wav"));
      writer.WriteLine(string.Concat(Keys.Raid_VideoToPlay.ToString(), " = "));
      writer.WriteLine("; Default empty -> minimum raiders 10.");
      writer.WriteLine(string.Concat(Keys.Raid_MinimumRaiders.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.Raid_DoShoutout.ToString(), " = true"));

      writer.WriteLine();
      writer.WriteLine();
      writer.WriteLine("; Channel points redemptions. Assign channel point redemption ID.");
      writer.WriteLine("; If the bot receives a channel point redemption event message that is not recognized (not configured),");
      writer.WriteLine("; the message will get printed to the bot's console window.");
      writer.WriteLine("; You can search the message for the ID and configure propper notification for it.");
      writer.WriteLine("; If the console window is not visible search for 'ConsoleVisible' field in this configuration file.");
      writer.WriteLine("; ----- Plays random video from Resources/Videos folder.");
      writer.WriteLine(string.Concat(Keys.ChannelRedemption_RandomVideo_ID.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.ChannelRedemption_RandomVideo_MarkAsFulfilled.ToString(), " = false"));
      writer.WriteLine();
      writer.WriteLine("; ----- Adds provided song (Spotify link) to song queue.");
      writer.WriteLine(string.Concat(Keys.ChannelRedemption_SongRequest_ID.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.ChannelRedemption_SongRequest_MarkAsFulfilled.ToString(), " = false"));
      writer.WriteLine();
      writer.WriteLine("; ----- Skips current Spotify song.");
      writer.WriteLine(string.Concat(Keys.ChannelRedemption_SongSkip_ID.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.ChannelRedemption_SongSkip_MarkAsFulfilled.ToString(), " = false"));
      writer.WriteLine();
      writer.WriteLine("; ----- Custom channel points redemptions.");
      writer.WriteLine("; The configuration group has to start with ID filed.");
      writer.WriteLine("; Multiple groups are allowed. Just copy the group and start with ID field.");
      writer.WriteLine("; Other available fields after ID fileds are referencing last assigned ID field.");
      writer.WriteLine("; This means that for example setting chat message 2 times after assigning ID would override each other.");
      writer.WriteLine("; Unused fields can be skipped (deleted from Config.ini).");
      writer.WriteLine("; KeyAction is comma separated list of keyboard keys that should be pressed when the channel redemption happens.");
      writer.WriteLine("; KeyActionAfterTime first value is time in miliseconds after which the action should be performed, next is comma separated list of keyboard keys that should be pressed.");
      writer.WriteLine("; The keys needs to be written in according to: https://learn.microsoft.com/en-us/dotnet/api/system.windows.input.key");
      writer.WriteLine("; For example to open task manager the combination would be: LeftCtrl, LeftShift, Escape.");
      writer.WriteLine("; Key action type describes what key action should perform: PRESS (presses the keys at once), TYPE (presses the keys one after another like during the typing). Default: PRESS.");
      writer.WriteLine(string.Concat(Keys.ChannelRedemption_ID.ToString(), " = 123456789"));
      writer.WriteLine(string.Concat(Keys.ChannelRedemption_KeyAction.ToString(), " = LeftCtrl, LeftShift, Escape"));
      writer.WriteLine(string.Concat(Keys.ChannelRedemption_KeyActionType.ToString(), " = PRESS"));
      writer.WriteLine(string.Concat(Keys.ChannelRedemption_KeyActionAfterTime.ToString(), " = 1000, LeftCtrl, LeftShift, Escape"));
      writer.WriteLine(string.Concat(Keys.ChannelRedemption_KeyActionAfterTimeType.ToString(), " = PRESS"));
      writer.WriteLine(string.Concat(Keys.ChannelRedemption_ChatMessage.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.ChannelRedemption_TextToDisplay.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.ChannelRedemption_TextPosition.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.ChannelRedemption_TextToSpeech.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.ChannelRedemption_SoundToPlay.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.ChannelRedemption_VideoToPlay.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.ChannelRedemption_MarkAsFulfilled.ToString(), " = false"));

      writer.WriteLine();
      writer.WriteLine();
      writer.WriteLine("; Periodic messages (one message per line, each starting with \"msg = \"), can be left empty");
      writer.WriteLine("; Multiple messages are allowed.");
      writer.WriteLine("; Time interval of sending periodic messages is configured in 'PeriodicMessageTimeInterval' field in this configuration file.");
      writer.WriteLine("; Periodic message is sent when time 'PeriodicMessageTimeInterval' from previous one has been exceeded and there have been at least 'PeriodicMessageMinChatMessages' chat messages.");
      writer.WriteLine("; msg = Commented out periodic message (deactivated) peepoSad");
    }

    if (!example)
    {
      // Notify the user
      MainWindow.ConsoleWarning(string.Concat(
        $">> Missing required info in {FILENAME} file.", Environment.NewLine,
        "The file was generated.", Environment.NewLine,
        "Please fill it up and restart the bot."
      ));
    }
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

  public static async void UpdateVolumes()
  {
    if (!VolumeValuesDirty) return;
    VolumeValuesDirty = false;

    await Database.UpdateValueInConfig(Database.Keys.VolumeTTS, VolumeTTS.ToString().Replace('.', ','));
    await Database.UpdateValueInConfig(Database.Keys.VolumeSounds, VolumeSounds.ToString().Replace('.', ','));
    await Database.UpdateValueInConfig(Database.Keys.VolumeVideos, VolumeVideos.ToString().Replace('.', ','));
  }

  /// <summary> Gets Broadcaster ID from Channel Name. Should be used once at bot startup. </summary>
  public static void GetBroadcasterID()
  {
    MainWindow.ConsoleWarning(">> Getting broadcaster ID.");
    string uri = $"https://api.twitch.tv/helix/users?login={Config.Data[Config.Keys.ChannelName]}";
    using HttpRequestMessage request = new(HttpMethod.Get, uri);
    request.Headers.Add("Authorization", $"Bearer {Secret.Data[Secret.Keys.OAuthToken]}");
    request.Headers.Add("Client-Id", Secret.Data[Secret.Keys.CustomerID]);

    string resp;
    try { resp = Notifications.Client.Send(request).Content.ReadAsStringAsync().Result; }
    catch (HttpRequestException ex) { MainWindow.ConsoleWarning($">> Couldn't acquire broadcaster ID. {ex.Message}"); return; }
    var response = ChannelIDResponse.Deserialize(resp);
    if (response != null && response?.Data?.Length == 1) { Config.Data[Config.Keys.ChannelID] = response.Data[0].ID; }
    else { MainWindow.ConsoleWarning(">> Couldn't acquire broadcaster ID. Probably defined channel name doesn't exist."); }
  }

  /// <summary> Gets current broadcaster status. </summary>
  /// <returns>true if the stream is online, otherwise false.</returns>
  public static bool GetBroadcasterStatus()
  {
    MainWindow.ConsoleWarning(">> Getting broadcaster status.");
    string uri = string.Concat(
      "https://api.twitch.tv/helix/search/channels",
      "?query=", Config.Data[Config.Keys.ChannelName].Replace(" ", "%20")
    );
    using HttpRequestMessage request = new(HttpMethod.Get, uri);
    request.Headers.Add("Authorization", $"Bearer {Secret.Data[Secret.Keys.OAuthToken]}");
    request.Headers.Add("Client-Id", Secret.Data[Secret.Keys.CustomerID]);

    string resp;
    try { resp = Notifications.Client.Send(request).Content.ReadAsStringAsync().Result; }
    catch (HttpRequestException ex) { MainWindow.ConsoleWarning($">> Couldn't acquire stream status. {ex.Message}"); return false; }
    if (resp is null || resp.Length == 0 || resp.StartsWith('<'))
    {
      MainWindow.ConsoleWarning(">> Couldn't acquire stream status.");
      return false;
    }
    var response = StatusResponse.Deserialize(resp);
    if (response is null || response.Data is null || response.Data.Length == 0)
    {
      MainWindow.ConsoleWarning(">> Couldn't acquire stream status.");
      return false;
    }
    else
    {
      foreach (var data in response.Data)
      {
        if (data.ID.Equals(Config.Data[Config.Keys.ChannelID]))
        {
          Discord.LastStreamTitle = data.Title;
          return data.IsLive == true;
        }
      }
    }

    return false;
  }

  public static async void UpdateLastOnline()
  {
    await Database.UpdateValueInConfig(Database.Keys.LastOnline, BroadcasterLastOnline);
  }
}
