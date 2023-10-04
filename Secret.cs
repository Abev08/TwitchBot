using System;
using System.Collections.Generic;
using System.IO;
using AbevBot;

/// <summary> Secret configuration stuff. </summary>
public static class Secret
{
  /// <summary> Secret keys that should be secret. </summary>
  public enum Keys
  {
    /// <summary> Bot's name. Probably not relevant but should be set to the same as in App Manager in https://dev.twitch.tv/console/apps. </summary>
    Name,
    /// <summary> Bot's Customer ID. Read it from https://dev.twitch.tv/console/apps. </summary>
    CustomerID,
    /// <summary> Bot's Password. Read it from https://dev.twitch.tv/console/apps. </summary>
    Password,
    /// <summary> OAuth token to authenticate the bot. </summary>
    OAuthToken,
    /// <summary> OAuth refresh token to refresh OAuth token when it expiries. </summary>
    OAuthRefreshToken,

    /// <summary> TikTok Session ID needed for TikTok API calls. </summary>
    TikTokSessionID
  }

  private const string FILENAME = "Secrets.ini";

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

  public static bool ParseSecretFile()
  {
    MainWindow.ConsoleWarning($">> Reading {FILENAME} file.");

    FileInfo secretsFile = new(FILENAME);
    if (secretsFile.Exists == false)
    {
      CreateSecretsFile(secretsFile);
      return true;
    }
    else
    {
      using (StreamReader reader = new(secretsFile.FullName))
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
            // MainWindow.ConsoleWarning($">> Bad {FILENAME} line: {lineIndex}.");
            continue;
          }
          object key;
          if (Enum.TryParse(typeof(Keys), text[0], out key))
          {
            switch ((Keys)key)
            {
              default:
                if (Data.ContainsKey((Keys)key)) { Data[(Keys)key] = text[1].Trim(); }
                else { MainWindow.ConsoleWarning($">> Not recognized key '{text[0]}' on line {lineIndex} in {FILENAME} file."); }
                break;
            }
          }
          else { MainWindow.ConsoleWarning($">> Not recognized key '{text[0]}' on line {lineIndex} in {FILENAME} file."); }
        }
      }

      // Check if all needed data was read
      if (Data[Keys.Name].Length == 0 || Data[Keys.CustomerID].Length == 0 || Data[Keys.Password].Length == 0)
      {
        MainWindow.ConsoleWarning(string.Concat(
          $">> Missing required information in {FILENAME} file.", Environment.NewLine,
          $"Look inside \"Required information in {FILENAME}\" section in README for help.", Environment.NewLine,
          $"You can delete {FILENAME} file to generate new one. ! WARNING - ALL DATA INSIDE IT WILL BE LOST !"
        ));
        return true;
      }
    }

    return false;
  }

  private static void CreateSecretsFile(FileInfo file)
  {
    using (StreamWriter writer = new(file.FullName))
    {
      writer.WriteLine("; The file contains top secret stuff.");
      writer.WriteLine("; DO NOT SHOW THE FILE ON STREAM, or else...");

      writer.WriteLine();
      writer.WriteLine();
      writer.WriteLine("; Required things, that needs to be filled in.");
      writer.WriteLine("; Bot's name. Probably not relevant but should be set to the same as in App Manager in https://dev.twitch.tv/console/apps.");
      writer.WriteLine(string.Concat(Keys.Name.ToString(), " = "));
      writer.WriteLine("; Bot's Customer ID. Read it from https://dev.twitch.tv/console/apps.");
      writer.WriteLine(string.Concat(Keys.CustomerID.ToString(), " = "));
      writer.WriteLine("; Bot's Password. Read it from https://dev.twitch.tv/console/apps.");
      writer.WriteLine(string.Concat(Keys.Password.ToString(), " = "));

      writer.WriteLine();
      writer.WriteLine();
      writer.WriteLine("; Additional things, can be left empty.");
      writer.WriteLine("; TikTok Session ID needed for TikTok API calls.");
      writer.WriteLine(string.Concat(Keys.TikTokSessionID.ToString(), " = "));
    }

    // Notify the user
    MainWindow.ConsoleWarning(string.Concat(
      $">> Missing required info in {FILENAME} file.", Environment.NewLine,
      "The file was generated.", Environment.NewLine,
      "Please fill it up and restart the bot."
    ));
  }
}
