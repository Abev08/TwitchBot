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
    TikTokSessionID,

    /// <summary> Spotify app Client ID. </summary>
    SpotifyClientID,
    /// <summary> Spotify app Client Secret. </summary>
    SpotifyClientSecret,
    /// <summary> Spotify OAuth token to authenticate the bot. </summary>
    SpotifyOAuthToken,
    /// <summary> Spotify OAuth refresh token to refresh OAuth token when it expiries. </summary>
    SpotifyOAuthRefreshToken
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

    // Create example Secrets.ini
    FileInfo secretsFile = new("Secrets_example.ini");
    CreateSecretsFile(secretsFile, true);

    secretsFile = new(FILENAME);
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

  private static void CreateSecretsFile(FileInfo file, bool example = false)
  {
    using (StreamWriter writer = new(file.FullName))
    {
      if (example)
      {
        writer.WriteLine();
        writer.WriteLine(";                               CAUTION");
        writer.WriteLine("; ----------------------------------------------------------------------");
        writer.WriteLine("; ---------------------------------------------------------------------");
        writer.WriteLine("; EXAMPLE SECRETS.INI FILE, WILL BE OVERRIDEN EVERY TIME THE BOT IS RUN");
        writer.WriteLine("; ---------------------------------------------------------------------");
        writer.WriteLine("; ----------------------------------------------------------------------");
        writer.WriteLine();
        writer.WriteLine();
      }

      writer.WriteLine("; The file contains top secret stuff.");
      writer.WriteLine("; DO NOT SHOW THE FILE ON STREAM, or else...");
      writer.WriteLine();
      writer.WriteLine("; --------------------------------------------------");
      writer.WriteLine("; Required things, that needs to be filled in.");
      writer.WriteLine("; --------------------------------------------------");

      writer.WriteLine();
      writer.WriteLine("; ----- Twitch");
      writer.WriteLine("; You need to register an app to get required information.");
      writer.WriteLine("; Steps to register an app:");
      writer.WriteLine(";  1. Log in to: https://dev.twitch.tv.");
      writer.WriteLine(";  2. Go to dev console (via 'Your Console' button on top right or https://dev.twitch.tv/console).");
      writer.WriteLine(";  3. Click 'Register your app' button on top right.");
      writer.WriteLine(";  4. Fill up required information. As Redirect URL use 'http://localhost:3000'.");
      writer.WriteLine(";  5. Next step will show secret information on your screen - don't show it on the stream.");
      writer.WriteLine(";  6. Go back to list of appliactions (https://dev.twitch.tv/console/apps) and click 'Manage' button next to your newly created app.");
      writer.WriteLine(";  7. Copy 'Name' and 'Customer ID' into the fields below. Also generate new 'Client secret' and copy that too.");
      writer.WriteLine(string.Concat(Keys.Name.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.CustomerID.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.Password.ToString(), " = "));

      writer.WriteLine();
      writer.WriteLine();
      writer.WriteLine("; --------------------------------------------------");
      writer.WriteLine("; Additional things, can be left empty.");
      writer.WriteLine("; --------------------------------------------------");

      writer.WriteLine();
      writer.WriteLine("; ----- TikTok");
      writer.WriteLine("; Session ID needed for API calls. You have to search yourself how to get one. I can't help you.");
      writer.WriteLine(string.Concat(Keys.TikTokSessionID.ToString(), " = "));

      writer.WriteLine();
      writer.WriteLine("; ----- Spotify");
      writer.WriteLine("; The usage of Spotify is simillar to Twitch.");
      writer.WriteLine("; You need Spotify account and you need to register a spotify app.");
      writer.WriteLine("; Steps to register an app:");
      writer.WriteLine(";  1. Log in to: https://developer.spotify.com.");
      writer.WriteLine(";  2. Go to the dashboard (via button on top right or https://developer.spotify.com/dashboard).");
      writer.WriteLine(";  3. Click 'Create app' button on top right.");
      writer.WriteLine(";  4. Fill up required information. Website field can be left empty and as Redirect URI use 'http://localhost:3000'.");
      writer.WriteLine(";  5. Next step will show secret information on your screen - don't show it on the stream.");
      writer.WriteLine(";  6. Go to your app settings and copy 'Client ID' and 'Client secret' into the fields below.");
      writer.WriteLine(string.Concat(Keys.SpotifyClientID.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.SpotifyClientSecret.ToString(), " = "));
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
}
