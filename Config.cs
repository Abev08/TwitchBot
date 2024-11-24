using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Windows.Input;

using Serilog;

namespace AbevBot;

public static class Config
{
  public enum Keys
  {
    ChannelName, ChannelID, BotID,
    ConsoleVisible, PeriodicMessageTimeInterval, PeriodicMessageMinChatMessages, StartVideoEnabled,
    AlwaysReadTTSFromThem, OverpoweredInFight,
    SongRequestTimeout,
    DiscordMessageOnline,
    HotkeyNotificationPause, HotkeyNotificationSkip,
    PrintChatMessagesToConsole,
    GambaMinimalChatMessages,
    FightMinimalChatMessages,

    Enable, ChatMessage, TextToDisplay, TextPosition, TextSize, TextToSpeech, SoundToPlay, VideoToPlay, VideoPosition, VideoSize, MinimumRaiders, DoShoutout, MinimumDuration, MinimumBitsForTTS, Amount,

    ChannelRedemption_RandomVideo_ID, ChannelRedemption_RandomVideo_MarkAsFulfilled, ChannelRedemption_SongRequest_ID, ChannelRedemption_SongRequest_MarkAsFulfilled, ChannelRedemption_SongSkip_ID, ChannelRedemption_SongSkip_MarkAsFulfilled,
    ChannelRedemption_RandomVideo_Size, ChannelRedemption_RandomVideo_Position,

    ChannelRedemption_ID, ChannelRedemption_KeyAction, ChannelRedemption_KeyActionType, ChannelRedemption_KeyActionAfterTime, ChannelRedemption_KeyActionAfterTimeType, ChannelRedemption_ChatMessage, ChannelRedemption_TextToDisplay, ChannelRedemption_TextPosition, ChannelRedemption_TextToSpeech, ChannelRedemption_SoundToPlay, ChannelRedemption_VideoToPlay, ChannelRedemption_MarkAsFulfilled,
    YouTubeChatMessagePrefix,
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
  public static float VolumeAudio { get; set; }
  public static float VolumeVideo { get; set; }
  private static DateTime ConfigFileTimestamp;
  public static DateTime BroadcasterLastOnline;
  public static DateTime BroadcasterLastOnlineCheck { get; set; }
  public static TimeSpan BroadcasterOnlineCheckInterval { get; } = TimeSpan.FromMinutes(3);
  public static TimeSpan BroadcasterOfflineTimeout { get; } = TimeSpan.FromMinutes(30);
  public static readonly List<Key> HotkeysForPauseNotification = new();
  public static readonly List<Key> HotkeysForSkipNotification = new();

  public static bool ParseConfigFile(bool reload = false)
  {
    if (reload) { Log.Information("Reloading {file} file.", FILENAME); }
    else { Log.Information("Reading {file} file.", FILENAME); }

    // Create resources folders just for user convenience (if they don't exist)
    DirectoryInfo dir = new(Notifications.SOUNDS_PATH);
    if (!dir.Exists) dir.Create();
    dir = new DirectoryInfo(Notifications.RANDOM_VIDEO_PATH);
    if (!dir.Exists) dir.Create();

    Chat.PeriodicMessages.Clear(); // Clear previous preiodic messages
    Chatter.AlwaysReadTTSFromThem.Clear();
    Chatter.OverpoweredInFight.Clear();
    Notifications.ChannelRedemptions.Clear();
    HotkeysForPauseNotification.Clear();
    HotkeysForSkipNotification.Clear();
    foreach (var config in Notifications.Configs) { config.Value.Reset(); }
    Notifications.RandomVideoParameters?.Reset();
    Notifications.CheerRangeNotifications.Clear();

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
      NotificationsConfig currentNotifConfig = null;

      // FileShare.ReadWrite needs to be used because it have to allow other processes to write into the file
      using FileStream fileStream = new(configFile.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
      using StreamReader reader = new(fileStream);
      while ((line = reader.ReadLine()) != null)
      {
        lineIndex++;
        line = line.Trim();
        // Skip commented out lines
        if (line.StartsWith("//") || line.StartsWith(';') || line.StartsWith('#') || string.IsNullOrWhiteSpace(line)) continue;

        // check for group heading
        if (line.EndsWith(':'))
        {
          // Special check if previously configured notification was CheerRange - it should be added to notifications list
          if (currentNotifConfig?.Type == NotificationType.CHEERRANGE) { Notifications.CheerRangeNotifications.Add(currentNotifConfig); }

          if (!Notifications.Configs.TryGetValue(line[..^1], out currentNotifConfig))
          {
            Log.Warning("Bad config line {index}. Group header not recognized!", lineIndex);
            currentNotifConfig = null;
          }

          // If configuring new CheerRange notification create new one - it would be added to CheerRange notifications list
          if (currentNotifConfig?.Type == NotificationType.CHEERRANGE) { currentNotifConfig = new(NotificationType.CHEERRANGE); }
          continue;
        }

        indexOf = line.IndexOf('=');
        if (indexOf > 0)
        {
          text[0] = line[..indexOf].Trim();
          text[1] = line[(indexOf + 1)..].Trim();
          indexOf = text[1].IndexOf(';');
          if (indexOf >= 0) text[1] = text[1][..indexOf].Trim();
          text[1] = text[1].Replace("\"", "").Trim();
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
                if (timeSpan.TotalSeconds > 0) Chat.PeriodicMessageInterval = TimeSpan.FromTicks(timeSpan.Ticks);
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
                Spotify.SongRequestTimeout = TimeSpan.FromTicks(timeSpan.Ticks);
              }
              break;

            case Keys.StartVideoEnabled:
              if (bool.TryParse(text[1], out result)) StartVideoEnabled = result;
              break;

            case Keys.DiscordMessageOnline:
              Discord.CustomOnlineMessage = text[1].Trim();
              break;

            case Keys.PrintChatMessagesToConsole:
              if (bool.TryParse(text[1], out result)) { Chat.PrintChatMessages = result; }
              else { Log.Warning("Keycode: {key} not recognized as boolean value in line {index} in Config.ini file.", text[1], lineIndex); }
              break;

            case Keys.HotkeyNotificationPause:
              keys = text[1].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
              foreach (string k in keys)
              {
                if (Enum.TryParse(typeof(Key), k, out position)) HotkeysForPauseNotification.Add((Key)position);
                else Log.Warning("Keycode: {key} not recognized in line {index} in Config.ini file.", k, lineIndex);
              }
              break;

            case Keys.HotkeyNotificationSkip:
              keys = text[1].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
              foreach (string k in keys)
              {
                if (Enum.TryParse(typeof(Key), k, out position)) HotkeysForSkipNotification.Add((Key)position);
                else Log.Warning("Keycode: {key} not recognized in line {index} in Config.ini file.", k, lineIndex);
              }
              break;

            case Keys.GambaMinimalChatMessages:
              if (bool.TryParse(text[1], out result)) { MinigameGamba.MinimalChatMessages = result; }
              else { Log.Warning("Keycode: {key} not recognized as boolean value in line {index} in Config.ini file.", text[1], lineIndex); }
              break;

            case Keys.FightMinimalChatMessages:
              if (bool.TryParse(text[1], out result)) { MinigameFight.MinimalChatMessages = result; }
              else { Log.Warning("Keycode: {key} not recognized as boolean value in line {index} in Config.ini file.", text[1], lineIndex); }
              break;

            case Keys.Enable:
              if (currentNotifConfig is null) Log.Warning("Bad config line {index}. Missing previous group header definition!", lineIndex);
              else if (bool.TryParse(text[1], out result)) currentNotifConfig.Enable = result;
              else Log.Warning("Bad config line {index}. Value not recognized!", lineIndex);
              break;
            case Keys.ChatMessage:
              if (currentNotifConfig is null) Log.Warning("Bad config line {index}. Missing previous group header definition!", lineIndex);
              else currentNotifConfig.ChatMessage = text[1].Trim();
              break;
            case Keys.TextToDisplay:
              if (currentNotifConfig is null) Log.Warning("Bad config line {index}. Missing previous group header definition!", lineIndex);
              else currentNotifConfig.TextToDisplay = text[1].Trim();
              break;
            case Keys.TextPosition:
              if (currentNotifConfig is null) Log.Warning("Bad config line {index}. Missing previous group header definition!", lineIndex);
              else if (Enum.TryParse(typeof(Notifications.TextPosition), text[1].Trim(), out position)) currentNotifConfig.TextPosition = (Notifications.TextPosition)position;
              else Log.Warning("Bad config line {index}. Value not recognized!", lineIndex);
              break;
            case Keys.TextSize:
              if (currentNotifConfig is null) Log.Warning("Bad config line {index}. Missing previous group header definition!", lineIndex);
              else if (text[1].Length > 0)
              {
                string separator = System.Globalization.CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
                string numString = text[1];
                if (separator == ",") numString = numString.Replace('.', ',');
                else if (separator == ".") numString = numString.Replace(',', '.');
                if (double.TryParse(numString, out double size) && size >= 0.1) currentNotifConfig.TextSize = size;
                else Log.Warning("Bad config line {index}. Value not recognized!", lineIndex);
              }
              break;
            case Keys.TextToSpeech:
              if (currentNotifConfig is null) Log.Warning("Bad config line {index}. Missing previous group header definition!", lineIndex);
              else currentNotifConfig.TextToSpeech = text[1].Trim();
              break;
            case Keys.SoundToPlay:
              if (currentNotifConfig is null) Log.Warning("Bad config line {index}. Missing previous group header definition!", lineIndex);
              else if (text[1].Length > 0) currentNotifConfig.SoundToPlay = $"Resources\\{text[1].Trim()}";
              break;
            case Keys.VideoToPlay:
              if (currentNotifConfig is null) Log.Warning("Bad config line {index}. Missing previous group header definition!", lineIndex);
              else if (text[1].Length > 0) currentNotifConfig.VideoToPlay = $"Resources\\{text[1].Trim()}";
              break;
            case Keys.VideoPosition:
              if (currentNotifConfig is null) { Log.Warning("Bad config line {index}. Missing previous group header definition!", lineIndex); }
              else if (text[1].Length > 0)
              {
                var stringNums = text[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (stringNums.Length == 2)
                {
                  var nums = new double[stringNums.Length];
                  bool ok = true;
                  for (int i = 0; i < stringNums.Length; i++)
                  {
                    if (!double.TryParse(stringNums[i], out nums[i]))
                    {
                      Log.Warning("Bad config line {index}. Value {val} not recognized!", lineIndex, i + 1);
                      ok = false;
                    }
                  }
                  if (ok)
                  {
                    if (currentNotifConfig.VideoParams is null) currentNotifConfig.VideoParams = new();
                    currentNotifConfig.VideoParams.Left = nums[0];
                    currentNotifConfig.VideoParams.Top = nums[1];
                  }
                }
                else Log.Warning("Bad config line {index}. Not enough or too many values!", lineIndex);
              }
              break;
            case Keys.VideoSize:
              if (currentNotifConfig is null) Log.Warning("Bad config line {index}. Missing previous group header definition!", lineIndex);
              else if (text[1].Length > 0)
              {
                var stringNums = text[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (stringNums.Length == 2)
                {
                  var nums = new double[stringNums.Length];
                  bool ok = true;
                  for (int i = 0; i < stringNums.Length; i++)
                  {
                    if (!double.TryParse(stringNums[i], out nums[i]))
                    {
                      Log.Warning("Bad config line {index}. Value {val} not recognized!", lineIndex, i + 1);
                      ok = false;
                    }
                  }
                  if (ok)
                  {
                    // check the aspect ratio
                    if (nums[0] / 16 > nums[1] / 9) { nums[0] = (nums[1] / 9) * 16; }
                    else { nums[1] = (nums[0] / 16) * 9; }

                    if (currentNotifConfig.VideoParams is null) currentNotifConfig.VideoParams = new();
                    currentNotifConfig.VideoParams.Width = nums[0];
                    currentNotifConfig.VideoParams.Height = nums[1];
                  }
                }
                else Log.Warning("Bad config line {index}. Not enough or too many values!", lineIndex);
              }
              break;
            case Keys.MinimumRaiders:
              if (currentNotifConfig is null) Log.Warning("Bad config line {index}. Missing previous group header definition!", lineIndex);
              else if (text[1].Length > 0)
              {
                if (int.TryParse(text[1], out temp2)) currentNotifConfig.MinimumRaiders = temp2;
                else Log.Warning("Bad config line {index}. Value not recognized!", lineIndex);
              }
              break;
            case Keys.DoShoutout:
              if (currentNotifConfig is null) Log.Warning("Bad config line {index}. Missing previous group header definition!", lineIndex);
              else if (bool.TryParse(text[1], out result)) currentNotifConfig.DoShoutout = result;
              else Log.Warning("Bad config line {index}. Value not recognized!", lineIndex);
              break;
            case Keys.MinimumDuration:
              if (currentNotifConfig is null) Log.Warning("Bad config line {index}. Missing previous group header definition!", lineIndex);
              else if (text[1].Length > 0)
              {
                if (TimeSpan.TryParse(text[1], out timeSpan)) currentNotifConfig.MinimumTime = TimeSpan.FromTicks(timeSpan.Ticks);
                else Log.Warning("Bad config line {index}. Value not recognized!", lineIndex);
              }
              break;
            case Keys.MinimumBitsForTTS:
              if (text[1].Length > 0)
              {
                if (currentNotifConfig is null) Log.Warning("Bad config line {index}. Missing previous group header definition!", lineIndex);
                else if (int.TryParse(text[1], out temp2)) currentNotifConfig.MinimumBits = temp2;
                else Log.Warning("Bad config line {index}. Value not recognized!", lineIndex);
              }
              break;
            case Keys.Amount:
              var tmp = text[1].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
              if (tmp.Length == 1)
              {
                if (int.TryParse(tmp[0], out int v1))
                {
                  currentNotifConfig.BitsRange[0] = v1;
                  currentNotifConfig.BitsRange[1] = v1;
                }
                else { Log.Warning("Bad config line {index}. Value not recognized!", lineIndex); }
              }
              else if (tmp.Length != 2) { Log.Warning("Bad config line {index}. Value not recognized!", lineIndex); }
              else
              {
                if (int.TryParse(tmp[0], out int v1) && int.TryParse(tmp[1], out int v2))
                {
                  if (v1 > v2) { Log.Warning("Bad config line {index}. Upper range is smaller than lower range!", lineIndex); }
                  else
                  {
                    currentNotifConfig.BitsRange[0] = v1;
                    currentNotifConfig.BitsRange[1] = v2;
                  }
                }
                else { Log.Warning("Bad config line {index}. Value not recognized!", lineIndex); }
              }
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
                Log.Warning("Bad config line {index}. Missing previous ID filed declaration!", lineIndex);
                continue;
              }
              keys = text[1].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
              foreach (string k in keys)
              {
                if (Enum.TryParse(typeof(Key), k, out position)) redemption.KeysToPress.Add((Key)position);
                else Log.Warning("Keycode: {key} not recognized in line {index} in Config.ini file.", k, lineIndex);
              }
              break;
            case Keys.ChannelRedemption_KeyActionType:
              if (text[1].Length == 0) continue;
              if (redemption is null)
              {
                Log.Warning("Bad config line {index}. Missing previous ID filed declaration!", lineIndex);
                continue;
              }
              if (Enum.TryParse(typeof(KeyActionType), text[1].Trim(), out position)) redemption.KeysToPressType = (KeyActionType)position;
              break;
            case Keys.ChannelRedemption_KeyActionAfterTime:
              if (text[1].Length == 0) continue;
              if (redemption is null)
              {
                Log.Warning("Bad config line {index}. Missing previous ID filed declaration!", lineIndex);
                continue;
              }
              keys = text[1].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
              if (int.TryParse(keys[0], out temp2)) redemption.TimeToPressSecondAction = TimeSpan.FromMilliseconds(temp2);
              else Log.Warning("Action time {key} not recognized in line {index} in Config.ini file.", keys[0], lineIndex);
              keys[0] = string.Empty;
              foreach (string k in keys)
              {
                if (k?.Length == 0) continue;
                if (Enum.TryParse(typeof(Key), k, out position)) redemption.KeysToPressAfterTime.Add((Key)position);
                else Log.Warning("Keycode: {key} not recognized in line {index} in Config.ini file.", k, lineIndex);
              }
              break;
            case Keys.ChannelRedemption_KeyActionAfterTimeType:
              if (text[1].Length == 0) continue;
              if (redemption is null)
              {
                Log.Warning("Bad config line {index}. Missing previous ID filed declaration!", lineIndex);
                continue;
              }
              if (Enum.TryParse(typeof(KeyActionType), text[1].Trim(), out position)) redemption.KeysToPressAfterTimeType = (KeyActionType)position;
              break;
            case Keys.ChannelRedemption_ChatMessage:
              if (text[1].Length == 0) continue;
              if (redemption is null)
              {
                Log.Warning("Bad config line {index}. Missing previous ID filed declaration!", lineIndex);
                continue;
              }
              redemption.Config.ChatMessage = text[1].Trim();
              break;
            case Keys.ChannelRedemption_TextToDisplay:
              if (text[1].Length == 0) continue;
              if (redemption is null)
              {
                Log.Warning("Bad config line {index}. Missing previous ID filed declaration!", lineIndex);
                continue;
              }
              redemption.Config.TextToDisplay = text[1].Trim();
              break;
            case Keys.ChannelRedemption_TextPosition:
              if (text[1].Length == 0) continue;
              if (redemption is null)
              {
                Log.Warning("Bad config line {index}. Missing previous ID filed declaration!", lineIndex);
                continue;
              }
              if (Enum.TryParse(typeof(Notifications.TextPosition), text[1].Trim(), out position)) redemption.Config.TextPosition = (Notifications.TextPosition)position;
              break;
            case Keys.ChannelRedemption_TextToSpeech:
              if (text[1].Length == 0) continue;
              if (redemption is null)
              {
                Log.Warning("Bad config line {index}. Missing previous ID filed declaration!", lineIndex);
                continue;
              }
              redemption.Config.TextToSpeech = text[1].Trim();
              break;
            case Keys.ChannelRedemption_SoundToPlay:
              if (text[1].Length == 0) continue;
              if (redemption is null)
              {
                Log.Warning("Bad config line {index}. Missing previous ID filed declaration!", lineIndex);
                continue;
              }
              if (text[1].Length > 0) redemption.Config.SoundToPlay = $"Resources\\{text[1].Trim()}";
              break;
            case Keys.ChannelRedemption_VideoToPlay:
              if (text[1].Length == 0) continue;
              if (redemption is null)
              {
                Log.Warning("Bad config line {index}. Missing previous ID filed declaration!", lineIndex);
                continue;
              }
              if (text[1].Length > 0) redemption.Config.VideoToPlay = $"Resources\\{text[1].Trim()}";
              break;
            case Keys.ChannelRedemption_MarkAsFulfilled:
              if (redemption is null)
              {
                Log.Warning("Bad config line {index}. Missing previous ID filed declaration!", lineIndex);
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

            case Keys.ChannelRedemption_RandomVideo_Size:
              if (text[1].Length > 0)
              {
                var stringNums = text[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (stringNums.Length == 2)
                {
                  var nums = new double[stringNums.Length];
                  bool ok = true;
                  for (int i = 0; i < stringNums.Length; i++)
                  {
                    if (!double.TryParse(stringNums[i], out nums[i]))
                    {
                      Log.Warning("Bad config line {index}. Value {val} not recognized!", lineIndex, i + 1);
                      ok = false;
                    }
                  }
                  if (ok)
                  {
                    // check the aspect ratio
                    if (nums[0] / 16 > nums[1] / 9) { nums[0] = (nums[1] / 9) * 16; }
                    else { nums[1] = (nums[0] / 16) * 9; }

                    if (Notifications.RandomVideoParameters is null) Notifications.RandomVideoParameters = new();
                    Notifications.RandomVideoParameters.Width = nums[0];
                    Notifications.RandomVideoParameters.Height = nums[1];
                  }
                }
                else Log.Warning("Bad config line {index}. Not enough or too many values!", lineIndex);
              }
              break;
            case Keys.ChannelRedemption_RandomVideo_Position:
              if (text[1].Length > 0)
              {
                var stringNums = text[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (stringNums.Length == 2)
                {
                  var nums = new double[stringNums.Length];
                  bool ok = true;
                  for (int i = 0; i < stringNums.Length; i++)
                  {
                    if (!double.TryParse(stringNums[i], out nums[i]))
                    {
                      Log.Warning("Bad config line {index}. Value {val} not recognized!", lineIndex, i + 1);
                      ok = false;
                    }
                  }
                  if (ok)
                  {
                    if (Notifications.RandomVideoParameters is null) Notifications.RandomVideoParameters = new();
                    Notifications.RandomVideoParameters.Left = nums[0];
                    Notifications.RandomVideoParameters.Top = nums[1];
                  }
                }
                else Log.Warning("Bad config line {index}. Not enough or too many values!", lineIndex);
              }
              break;

            default:
              if (Data.ContainsKey((Keys)key)) { Data[(Keys)key] = text[1].Trim(); }
              else Log.Warning("Not recognized key '{key}' on line {index} in Config.ini file.", text[0], lineIndex);
              break;
          }
        }
        else { Log.Warning("Not recognized key '{key}' on line {index} in Config.ini file.", text[0], lineIndex); }
      }

      // Check if all needed data was read
      if (Data[Keys.ChannelName].Length == 0)
      {
        Log.Error("Missing required info in {file} file.\r\nLook inside \"Required information in {file}\" section in README for help.\r\nYou can delete {file} file to generate new one. ! WARNING - ALL DATA INSIDE IT WILL BE LOST !", FILENAME, FILENAME, FILENAME);
        return true;
      }

      // Nothing is missing, do some setup things
      Log.Information("Loaded {count} periodic messages.", Chat.PeriodicMessages.Count);
    }

    ConfigFileTimestamp = configFile.LastWriteTime;

    return false;
  }

  /// <summary> Creates new Config.ini file. </summary>
  /// <param name="file">FileInfo object describing file parameters</param>
  /// <param name="example">Is this example Config.ini file?</param>
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
      writer.WriteLine($"; Sound samples such as farts, laughs, applauses, etc. should be placed in the \"{Notifications.SOUNDS_PATH}\" folder,");
      writer.WriteLine("; to be used in TTS messages like this \"!tts -laugh\" - calling the file name with \"-\" symbol in front of it.");
      writer.WriteLine(";");
      writer.WriteLine($"; Videos that can be played via random video channel point redemption should be placed in the \"{Notifications.RANDOM_VIDEO_PATH}\" folder.");

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
      writer.WriteLine("; Print chat messages to console window. Default: true");
      writer.WriteLine(string.Concat(Keys.PrintChatMessagesToConsole.ToString(), " = true"));
      writer.WriteLine("; Use minimal gamba minigame chat messages (ugly but less spammy). Default: false");
      writer.WriteLine(string.Concat(Keys.GambaMinimalChatMessages.ToString(), " = false"));
      writer.WriteLine("; Use minimal fight minigame chat messages (ugly but less spammy). Default: false");
      writer.WriteLine(string.Concat(Keys.FightMinimalChatMessages.ToString(), " = false"));
      writer.WriteLine("; Prefix of YouTube chat message resended in Twitch chat.");
      writer.WriteLine(string.Concat(Keys.YouTubeChatMessagePrefix.ToString(), " = YT"));

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
      writer.WriteLine("; Empty assignment means that this part of the notification would be disabled or default value would be used.");
      writer.WriteLine("; TextPosition - available options: TOPLEFT, TOP, TOPRIGHT, LEFT, CENTER, RIGHT, BOTTOMLEFT, BOTTOM, BOTTOMRIGHT, VIDEOABOVE, VIDEOCENTER, VIDEOBELOW.");
      writer.WriteLine("; TextSize - displayed text size. Default: empty - 48");
      writer.WriteLine("; Paths to sounds and videos are relative to Resources folder.");
      writer.WriteLine("; For a file that is placed directly in Resources folder it would be just a filename with it's extension.");
      writer.WriteLine("; VideoPosition - describes played video position from the top left corner in pixels. 2 values separated by a comma are needed.");
      writer.WriteLine("; VideoSize - describes played video size in pixels. 2 values separated by a comma are needed. The aspect ratio is preserved. The best way is to set the width to some high value and shrink the video by chaning the height.");
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
      writer.WriteLine("Follow:");
      writer.WriteLine(string.Concat(Keys.Enable.ToString(), " = true"));
      writer.WriteLine(string.Concat(Keys.ChatMessage.ToString(), " = @{0} thank you for following!"));
      writer.WriteLine(string.Concat(Keys.TextToDisplay.ToString(), " = New follower {0}!"));
      writer.WriteLine(string.Concat(Keys.TextPosition.ToString(), " = TOP"));
      writer.WriteLine(string.Concat(Keys.TextSize.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.TextToSpeech.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.SoundToPlay.ToString(), " = tone1.wav"));
      writer.WriteLine(string.Concat(Keys.VideoToPlay.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.VideoPosition.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.VideoSize.ToString(), " = "));
      writer.WriteLine();
      writer.WriteLine("; ----- Subscription (notification message, always received when someone subscribes even when the subscriber doesn't want it to be public)");
      writer.WriteLine("Subscription:");
      writer.WriteLine(string.Concat(Keys.Enable.ToString(), " = false"));
      writer.WriteLine(string.Concat(Keys.ChatMessage.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.TextToDisplay.ToString(), " = {0} just subscribed!"));
      writer.WriteLine(string.Concat(Keys.TextPosition.ToString(), " = TOP"));
      writer.WriteLine(string.Concat(Keys.TextSize.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.TextToSpeech.ToString(), " = Thank you {0} for tier {1} sub! {7}"));
      writer.WriteLine(string.Concat(Keys.SoundToPlay.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.VideoToPlay.ToString(), " = peepoHey.mp4"));
      writer.WriteLine(string.Concat(Keys.VideoPosition.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.VideoSize.ToString(), " = "));
      writer.WriteLine();
      writer.WriteLine("; ----- Subscription Extended Message (the subscriber shares that he subscribed)");
      writer.WriteLine("SubscriptionExt:");
      writer.WriteLine(string.Concat(Keys.Enable.ToString(), " = true"));
      writer.WriteLine(string.Concat(Keys.ChatMessage.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.TextToDisplay.ToString(), " = {0} just subscribed!"));
      writer.WriteLine(string.Concat(Keys.TextPosition.ToString(), " = TOP"));
      writer.WriteLine(string.Concat(Keys.TextSize.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.TextToSpeech.ToString(), " = Thank you {0} for {2} month{10} in advance tier {1} sub! It is your {3} month in a row! {7}"));
      writer.WriteLine(string.Concat(Keys.SoundToPlay.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.VideoToPlay.ToString(), " = peepoHey.mp4"));
      writer.WriteLine(string.Concat(Keys.VideoPosition.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.VideoSize.ToString(), " = "));
      writer.WriteLine();
      writer.WriteLine("; ----- Subscription gifted (the user is gifting subscriptions)");
      writer.WriteLine("SubscriptionGift:");
      writer.WriteLine(string.Concat(Keys.Enable.ToString(), " = true"));
      writer.WriteLine(string.Concat(Keys.ChatMessage.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.TextToDisplay.ToString(), " = Thank you {0} for {4} sub{12}!"));
      writer.WriteLine(string.Concat(Keys.TextPosition.ToString(), " = TOP"));
      writer.WriteLine(string.Concat(Keys.TextSize.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.TextToSpeech.ToString(), " = Thank you {0} for gifting {4} tier {1} sub{12} to {6}! {7}"));
      writer.WriteLine(string.Concat(Keys.SoundToPlay.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.VideoToPlay.ToString(), " = peepoHey.mp4"));
      writer.WriteLine(string.Concat(Keys.VideoPosition.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.VideoSize.ToString(), " = "));
      writer.WriteLine();
      writer.WriteLine("; ----- Subscription gift received (the user received subscription gift, these events are sent before or after subscription gifted event)");
      writer.WriteLine("SubscriptionGiftReceived:");
      writer.WriteLine(string.Concat(Keys.Enable.ToString(), " = false"));
      writer.WriteLine(string.Concat(Keys.ChatMessage.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.TextToDisplay.ToString(), " = New subscriber {0}!"));
      writer.WriteLine(string.Concat(Keys.TextPosition.ToString(), " = TOP"));
      writer.WriteLine(string.Concat(Keys.TextSize.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.TextToSpeech.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.SoundToPlay.ToString(), " = tone1.wav"));
      writer.WriteLine(string.Concat(Keys.VideoToPlay.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.VideoPosition.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.VideoSize.ToString(), " = "));
      writer.WriteLine();
      writer.WriteLine("; ----- Bits cheer");
      writer.WriteLine("; Default notification");
      writer.WriteLine("Cheer:");
      writer.WriteLine(string.Concat(Keys.Enable.ToString(), " = true"));
      writer.WriteLine(string.Concat(Keys.ChatMessage.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.TextToDisplay.ToString(), " = Thank you {0} for {4} bit{12}!"));
      writer.WriteLine(string.Concat(Keys.TextPosition.ToString(), " = TOP"));
      writer.WriteLine(string.Concat(Keys.TextSize.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.TextToSpeech.ToString(), " = Thank you {0} for {4} bit{12}! {7}"));
      writer.WriteLine(string.Concat(Keys.SoundToPlay.ToString(), " = tone1.wav"));
      writer.WriteLine(string.Concat(Keys.VideoToPlay.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.VideoPosition.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.VideoSize.ToString(), " = "));
      writer.WriteLine("; The minimum amount of bits thrown for the message to be read as TTS. Default: empty - 10");
      writer.WriteLine(string.Concat(Keys.MinimumBitsForTTS.ToString(), " = "));
      writer.WriteLine();
      writer.WriteLine("; Custom cheer notifications (related to amount of bits)");
      writer.WriteLine("; When cheer event is received first custom notifications are inspected if cheer amount mathes");
      writer.WriteLine("; if none of custom cheer notifications is valid the default cheer notification is played");
      writer.WriteLine("; You can have multiple custom cheer notifications - just start new one with \"CheerRange:\" header");
      writer.WriteLine("; In the Amount field you can specify a value on which the notification would be played");
      writer.WriteLine("; or a range on which the custom notification is valid. Examples:");
      writer.WriteLine("; - play notification on 1234 bits: \"Amount = 1234\"");
      writer.WriteLine("; - play notification on bits in range 100 - 1000: \"Amount = 100, 1000\"");
      writer.WriteLine("; If bits range in different CheerRange notifications overlaps the first one configured is played");
      writer.WriteLine("CheerRange:");
      writer.WriteLine(string.Concat(Keys.Enable.ToString(), " = false"));
      writer.WriteLine(string.Concat(Keys.Amount.ToString(), " = 123, 234"));
      writer.WriteLine(string.Concat(Keys.ChatMessage.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.TextToDisplay.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.TextPosition.ToString(), " = TOP"));
      writer.WriteLine(string.Concat(Keys.TextSize.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.TextToSpeech.ToString(), " = {0} cheered with {4} bit{12} which is in range of magic numbers! {7}"));
      writer.WriteLine(string.Concat(Keys.SoundToPlay.ToString(), " = tone1.wav"));
      writer.WriteLine(string.Concat(Keys.VideoToPlay.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.VideoPosition.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.VideoSize.ToString(), " = "));
      writer.WriteLine();
      writer.WriteLine("; ----- Raid (channel got raided)");
      writer.WriteLine("Raid:");
      writer.WriteLine(string.Concat(Keys.Enable.ToString(), " = true"));
      writer.WriteLine(string.Concat(Keys.ChatMessage.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.TextToDisplay.ToString(), " = {4} raider{12} from {0} are coming!"));
      writer.WriteLine(string.Concat(Keys.TextPosition.ToString(), " = TOP"));
      writer.WriteLine(string.Concat(Keys.TextSize.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.TextToSpeech.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.SoundToPlay.ToString(), " = tone1.wav"));
      writer.WriteLine(string.Concat(Keys.VideoToPlay.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.VideoPosition.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.VideoSize.ToString(), " = "));
      writer.WriteLine("; Default empty -> minimum raiders 10.");
      writer.WriteLine(string.Concat(Keys.MinimumRaiders.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.DoShoutout.ToString(), " = true"));
      writer.WriteLine();
      writer.WriteLine("; ----- Chatter timeout");
      writer.WriteLine("Timeout:");
      writer.WriteLine(string.Concat(Keys.Enable.ToString(), " = false"));
      writer.WriteLine(string.Concat(Keys.ChatMessage.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.TextToDisplay.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.TextPosition.ToString(), " = TOP"));
      writer.WriteLine(string.Concat(Keys.TextSize.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.TextToSpeech.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.SoundToPlay.ToString(), " = tone1.wav"));
      writer.WriteLine(string.Concat(Keys.VideoToPlay.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.VideoPosition.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.VideoSize.ToString(), " = "));
      writer.WriteLine("; Minimum timeout duration (HH:MM:SS format -> 1 hour: 1:00:00, 1 minute 0:01:00, 1 second: 0:00:01). Default: empty - 0 seconds");
      writer.WriteLine(string.Concat(Keys.MinimumDuration.ToString(), " = "));
      writer.WriteLine();
      writer.WriteLine("; ----- Chatter ban");
      writer.WriteLine("Ban:");
      writer.WriteLine(string.Concat(Keys.Enable.ToString(), " = false"));
      writer.WriteLine(string.Concat(Keys.ChatMessage.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.TextToDisplay.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.TextPosition.ToString(), " = TOP"));
      writer.WriteLine(string.Concat(Keys.TextSize.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.TextToSpeech.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.SoundToPlay.ToString(), " = tone1.wav"));
      writer.WriteLine(string.Concat(Keys.VideoToPlay.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.VideoPosition.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.VideoSize.ToString(), " = "));
      writer.WriteLine();
      writer.WriteLine("; ----- On screen celebration (channel redemption with bits)");
      writer.WriteLine("OnScreenCelebration:");
      writer.WriteLine(string.Concat(Keys.Enable.ToString(), " = true"));
      writer.WriteLine(string.Concat(Keys.ChatMessage.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.TextToDisplay.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.TextPosition.ToString(), " = TOP"));
      writer.WriteLine(string.Concat(Keys.TextSize.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.TextToSpeech.ToString(), " = funny: {0} fired up a -celebration. {7}"));
      writer.WriteLine(string.Concat(Keys.SoundToPlay.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.VideoToPlay.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.VideoPosition.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.VideoSize.ToString(), " = "));
      writer.WriteLine();
      writer.WriteLine("; ----- Chat message with special effect (channel redemption with bits)");
      writer.WriteLine("MessageEffect:");
      writer.WriteLine(string.Concat(Keys.Enable.ToString(), " = true"));
      writer.WriteLine(string.Concat(Keys.ChatMessage.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.TextToDisplay.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.TextPosition.ToString(), " = TOP"));
      writer.WriteLine(string.Concat(Keys.TextSize.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.TextToSpeech.ToString(), " = {7}"));
      writer.WriteLine(string.Concat(Keys.SoundToPlay.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.VideoToPlay.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.VideoPosition.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.VideoSize.ToString(), " = "));
      writer.WriteLine();
      writer.WriteLine("; ----- Gigantify an emote (channel redemption with bits)");
      writer.WriteLine("GigantifyEmote:");
      writer.WriteLine(string.Concat(Keys.Enable.ToString(), " = true"));
      writer.WriteLine(string.Concat(Keys.ChatMessage.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.TextToDisplay.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.TextPosition.ToString(), " = TOP"));
      writer.WriteLine(string.Concat(Keys.TextSize.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.TextToSpeech.ToString(), " = Santa3: {0} says {7}"));
      writer.WriteLine(string.Concat(Keys.SoundToPlay.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.VideoToPlay.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.VideoPosition.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.VideoSize.ToString(), " = "));

      writer.WriteLine();
      writer.WriteLine();
      writer.WriteLine("; Channel points redemptions. Assign channel point redemption ID.");
      writer.WriteLine("; If the bot receives a channel point redemption event message that is not recognized (not configured),");
      writer.WriteLine("; the message will get printed to the bot's console window.");
      writer.WriteLine("; You can search the message for the ID and configure propper notification for it.");
      writer.WriteLine("; If the console window is not visible search for 'ConsoleVisible' field in this configuration file.");
      writer.WriteLine($"; ----- Plays random video from {Notifications.RANDOM_VIDEO_PATH} folder.");
      writer.WriteLine(string.Concat(Keys.ChannelRedemption_RandomVideo_ID.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.ChannelRedemption_RandomVideo_MarkAsFulfilled.ToString(), " = false"));
      writer.WriteLine(string.Concat(Keys.ChannelRedemption_RandomVideo_Position.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.ChannelRedemption_RandomVideo_Size.ToString(), " = "));
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
      Log.Error("Missing required info in {file} file.\r\nThe file was generated.\r\nPlease fill it up and restart the bot.", FILENAME);
    }
  }

  /// <summary> Checks if Config.ini file was updated. </summary>
  /// <returns><value>true</value> if the file was updated, otherwise <value>false</value></returns>
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

    await Database.UpdateValueInConfig(Database.Keys.VolumeAudio, VolumeAudio.ToString().Replace('.', ','));
    await Database.UpdateValueInConfig(Database.Keys.VolumeVideo, VolumeVideo.ToString().Replace('.', ','));
  }

  /// <summary> Gets current broadcaster status. </summary>
  /// <returns>true if the stream is online, otherwise false.</returns>
  public static bool GetBroadcasterStatus()
  {
    Log.Information("Getting broadcaster status.");
    string uri = string.Concat(
      "https://api.twitch.tv/helix/search/channels",
      "?query=", Config.Data[Config.Keys.ChannelName].Replace(" ", "%20")
    );
    using HttpRequestMessage request = new(HttpMethod.Get, uri);
    request.Headers.Add("Authorization", $"Bearer {Secret.Data[Secret.Keys.TwitchOAuthToken]}");
    request.Headers.Add("Client-Id", Secret.Data[Secret.Keys.TwitchClientID]);

    string resp;
    try { resp = Notifications.Client.Send(request).Content.ReadAsStringAsync().Result; }
    catch (HttpRequestException ex) { Log.Error("Couldn't acquire stream status. {ex}", ex); return false; }
    if (resp is null || resp.Length == 0 || resp.StartsWith('<'))
    {
      Log.Warning("Couldn't acquire stream status.");
      return false;
    }
    var response = StatusResponse.Deserialize(resp);
    if (response is null || response.Data is null || response.Data.Length == 0)
    {
      Log.Warning("Couldn't acquire stream status.");
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
