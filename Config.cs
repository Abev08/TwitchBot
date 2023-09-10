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
      ChannelName, BroadcasterID,
      BotNick, BotClientID, BotPass,
      BotOAuthToken, BotOAuthRefreshToken
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

    public static bool ParseConfigFile()
    {
      MainWindow.ConsoleWarning(">> Reading Config.ini file.");

      FileInfo configFile = new FileInfo(@"./Config.ini");
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
            if (line.StartsWith("//") || string.IsNullOrWhiteSpace(line)) continue;
            string[] text = line.Split('=', StringSplitOptions.TrimEntries);
            if (text.Length < 2 || string.IsNullOrWhiteSpace(text[1]))
            {
              MainWindow.ConsoleWarning($">> Bad Config.ini line: {lineIndex}.");
              continue;
            }
            object key;
            if (Enum.TryParse(typeof(Keys), text[0], out key))
            {
              if ((Keys)key == Keys.ChannelName) Data[(Keys)key] = text[1].ToLower();
              else Data[(Keys)key] = text[1];
            }
            else { MainWindow.ConsoleWarning($">> Not recognized key in line {lineIndex}."); }
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
        writer.WriteLine(string.Concat(Keys.ChannelName.ToString(), " = "));
        writer.WriteLine(string.Concat(Keys.BotNick.ToString(), " = "));
        writer.WriteLine(string.Concat(Keys.BotClientID.ToString(), " = "));
        writer.WriteLine(string.Concat(Keys.BotPass.ToString(), " = "));
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
      using (HttpRequestMessage request = new HttpRequestMessage(new HttpMethod("GET"), uri))
      {
        request.Headers.Add("Authorization", $"Bearer {Data[Keys.BotOAuthToken]}");
        request.Headers.Add("Client-Id", Data[Keys.BotClientID]);

        using (HttpClient client = new HttpClient())
        {
          ChannelIDResponse response = ChannelIDResponse.Deserialize(client.SendAsync(request).Result.Content.ReadAsStringAsync().Result);
          if (response != null && response?.Data?.Length == 1)
          {
            Data[Keys.BroadcasterID] = response.Data[0].ID;
          }
          else
          {
            MainWindow.ConsoleWarning(">> Couldn't acquire broadcaster ID. Probably defined channel name doesn't exist.");
          }
        }
      }
    }
  }
}
