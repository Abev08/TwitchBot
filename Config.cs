using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;

namespace AbevBot
{
  public static class Config
  {
    public enum Keys
    {
      ChannelName, ChannelID,
      BotNick, BotClientID, BotPass,
      BotOAuthToken, BotOAuthRefreshToken,
      TikTokSessionID,
      ConsoleVisible, PeriodicMessageTimeInterval,
      PathToSubscriptionVideo, PathToGiftSubscriptionVideo,
      ChannelPointRandomVideo,
      msg
    };

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

    public static bool ParseConfigFile()
    {
      MainWindow.ConsoleWarning(">> Reading Config.ini file.");

      // Create resources folders just for user convenience
      DirectoryInfo dir = new("Resources/Sounds");
      if (!dir.Exists) dir.Create();
      dir = new DirectoryInfo("Resources/Videos");
      if (!dir.Exists) dir.Create();

      FileInfo configFile = new(@"./Config.ini");
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
          while ((line = reader.ReadLine()) != null)
          {
            lineIndex++;
            // Skip commented out lines
            if (line.StartsWith("//") || line.StartsWith(';') || line.StartsWith('#') || string.IsNullOrWhiteSpace(line)) continue;

            string[] text = line.Split(';')[0].Split('=', StringSplitOptions.TrimEntries);
            if (text.Length < 2 || string.IsNullOrWhiteSpace(text[1]))
            {
              // MainWindow.ConsoleWarning($">> Bad Config.ini line: {lineIndex}.");
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

                case Keys.msg:
                  // If there are '=' symbols in the message the previous splitting would brake it
                  string msg = line.Substring(line.IndexOf('=') + 1).Trim();
                  if (msg.Length > 0) Chat.PeriodicMessages.Add(msg);
                  break;

                case Keys.ConsoleVisible:
                  if (bool.TryParse(text[1], out bool result)) ConsoleVisible = result;
                  break;

                case Keys.PeriodicMessageTimeInterval:
                  if (TimeSpan.TryParse(text[1], out TimeSpan timeSpan))
                  {
                    if (timeSpan.TotalSeconds > 0) Chat.PeriodicMessageInterval = timeSpan;
                  }
                  break;

                case Keys.PathToSubscriptionVideo:
                case Keys.PathToGiftSubscriptionVideo:
                  Data[(Keys)key] = new FileInfo(string.Concat(
                      "Resources/",
                      text[1].Trim().Replace('\\', '/').Replace("\"", "") // Replace "\" with "/" and remove '"'
                    )).FullName;
                  break;

                case Keys.ChannelPointRandomVideo:
                  Data[(Keys)key] = text[1].Trim().Replace("\"", ""); // remove '"'
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
        if (Data[Keys.ChannelName].Length == 0 ||
            Data[Keys.BotNick].Length == 0 || Data[Keys.BotClientID].Length == 0 || Data[Keys.BotPass].Length == 0)
        {
          MainWindow.ConsoleWarning(string.Concat(
            ">> Missing required info in Config.ini file.", Environment.NewLine,
            "Look inside \"Required information in Config.ini\" section in README for help.", Environment.NewLine,
            "You can delete Config.ini file to generate new one. ! WARNING - ALL DATA INSIDE IT WILL BE LOST !"
          ));
          Console.ReadLine();
          return true;
        }
        else
        {
          // Nothing is missing, do some setup things
          MainWindow.ConsoleWarning($">> Loaded {Chat.PeriodicMessages.Count} periodic messages.");
          AccessTokens.GetAccessTokens();
          GetBroadcasterID();
        }
      }

      return false;
    }

    private static void CreateConfigFile(FileInfo configFile)
    {
      using (StreamWriter writer = new(configFile.FullName))
      {
        writer.WriteLine("; Required things, needs to be filled in");
        writer.WriteLine(string.Concat(Keys.ChannelName.ToString(), " = "));
        writer.WriteLine(string.Concat(Keys.BotNick.ToString(), " = "));
        writer.WriteLine(string.Concat(Keys.BotClientID.ToString(), " = "));
        writer.WriteLine(string.Concat(Keys.BotPass.ToString(), " = "));

        writer.WriteLine();
        writer.WriteLine();
        writer.WriteLine("; Additional things, can be left empty");
        writer.WriteLine(string.Concat(Keys.TikTokSessionID.ToString(), " = "));
        writer.WriteLine(string.Concat(Keys.PathToSubscriptionVideo.ToString(), " = ; Path to video file for subscription notification (relative to Resources folder)"));
        writer.WriteLine(string.Concat(Keys.PathToGiftSubscriptionVideo.ToString(), " = ; Path to video file for gifted subscription notification (relative to Resources folder)"));
        writer.WriteLine("ConsoleVisible = false ; Debug console visibility");
        writer.WriteLine("PeriodicMessageTimeInterval = ; default 10 minutes, HH:MM:SS format");

        writer.WriteLine();
        writer.WriteLine();
        writer.WriteLine("; Sound samples such as farts, laughs, applauses, etc. should be placed in the \"Resources/Sounds\" folder");
        writer.WriteLine("; to be used in TTS messages like this \"!tts -laugh\", etc. - calling the file name with \"-\" symbol in front of it.");
        writer.WriteLine(";");
        writer.WriteLine("; Videos that can be played via random video channel point redemption should be placed in the \"Resources/Videos\" folder");

        writer.WriteLine();
        writer.WriteLine();
        writer.WriteLine("; Channel points redemptions IDs assignments");
        writer.WriteLine(string.Concat(Keys.ChannelPointRandomVideo.ToString(), " = "));

        writer.WriteLine();
        writer.WriteLine();
        writer.WriteLine("; Periodic messages (one message per line, each starting with \"msg = \"), can be left empty");
        writer.WriteLine("; msg = Commented out periodic message (deactivated) peepoSad");
      }

      // Notify the user
      MainWindow.ConsoleWarning(string.Concat(
        ">> Missing required info in Config.ini file.", Environment.NewLine,
        "The file was generated.", Environment.NewLine,
         "Please fill it up and restart the bot."
      ));
      Console.ReadLine();
    }

    private static void GetBroadcasterID()
    {
      MainWindow.ConsoleWarning(">> Getting broadcaster ID.");
      string uri = $"https://api.twitch.tv/helix/users?login={Data[Keys.ChannelName]}";
      using HttpRequestMessage request = new(HttpMethod.Get, uri);
      request.Headers.Add("Authorization", $"Bearer {Data[Keys.BotOAuthToken]}");
      request.Headers.Add("Client-Id", Data[Keys.BotClientID]);

      using HttpClient client = new();
      ChannelIDResponse response = ChannelIDResponse.Deserialize(client.Send(request).Content.ReadAsStringAsync().Result);
      if (response != null && response?.Data?.Length == 1)
      {
        Data[Keys.ChannelID] = response.Data[0].ID;
      }
      else
      {
        MainWindow.ConsoleWarning(">> Couldn't acquire broadcaster ID. Probably defined channel name doesn't exist.");
      }
    }
  }
}
