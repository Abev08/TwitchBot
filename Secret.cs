using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

using Serilog;

namespace AbevBot;

/// <summary> Secret configuration stuff. </summary>
public static class Secret
{
  /// <summary> Secret keys that should be secret. </summary>
  public enum Keys
  {
    /// <summary> Twitch bot name. Probably not relevant but should be set to the same as in App Manager in https://dev.twitch.tv/console/apps. </summary>
    TwitchName,
    /// <summary> Twitch bot client ID. Read it from https://dev.twitch.tv/console/apps. </summary>
    TwitchClientID,
    /// <summary> Twitch bot password. Read it from https://dev.twitch.tv/console/apps. </summary>
    TwitchPassword,
    /// <summary> Twitch main bot OAuth token. </summary>
    TwitchOAuthToken,
    /// <summary> Twitch main bot OAuth refresh token to refresh OAuth token when it expiries. </summary>
    TwitchOAuthRefreshToken,
    /// <summary> Twitch sub bot OAuth token. Used as an account for posting chat messages. </summary>
    TwitchSubOAuthToken,
    /// <summary> Twitch sub bot OAuth refresh token to refresh OAuth token when it expiries. </summary>
    TwitchSubOAuthRefreshToken,
    /// <summary> Should Twitch chat bot use sub account for posting chat messages? "1" == enabled </summary>
    TwitchUseTwoAccounts,

    /// <summary> TikTok Session ID needed for TikTok API calls. </summary>
    TikTokSessionID,

    /// <summary> Spotify app Client ID. </summary>
    SpotifyClientID,
    /// <summary> Spotify app Client Secret. </summary>
    SpotifyClientSecret,
    /// <summary> Spotify OAuth token to authenticate the bot. </summary>
    SpotifyOAuthToken,
    /// <summary> Spotify OAuth refresh token to refresh OAuth token when it expiries. </summary>
    SpotifyOAuthRefreshToken,

    /// <summary> Discord app Client ID. </summary>
    DiscrodClientID,
    /// <summary> Discord app Client Secret. </summary>
    DiscordClientSecret,
    /// <summary> Discord app Bot Token. </summary>
    DiscordBotToken,
    /// <summary> Discord channel ID on which messages shoud be posted. </summary>
    DiscordChannelID,
    /// <summary> Discord channel ID on which random videos are posted. </summary>
    DiscordRandomVideosChannelID,
    /// <summary> Discord OAuth token to authenticate the bot. </summary>
    DiscordOAuthToken,
    /// <summary> Discord OAuth refresh token to refresh OAuth token when it expiries. </summary>
    DiscordOAuthRefreshToken,

    /// <summary> YouTube channel ID, can be read from: https://www.youtube.com/account_advanced </summary>
    YouTubeChannelID,
    /// <summary> YouTube API key, can be generated at: https://console.developers.google.com </summary>
    YouTubeAPIKey,
  }

  private const string FILENAME = "Secrets.ini";
  private const string FILENAMEEXAMPLE = "Secrets_example.ini";

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
    Log.Information("Reading {file} file.", FILENAME);

    // Create example Secrets.ini
    FileInfo secretsFile = new(FILENAMEEXAMPLE);
    CreateSecretsFile(secretsFile, true);

    secretsFile = new(FILENAME);
    if (secretsFile.Exists == false)
    {
      CreateSecretsFile(secretsFile);
      return true;
    }
    else
    {
      bool oldTwitchNamesUsed = false;
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
            // Log.Warning("Bad {file} line: {index}.", FILENAME, lineIndex);
            continue;
          }
          object key;
          if (Enum.TryParse(typeof(Keys), text[0], out key))
          {
            switch ((Keys)key)
            {
              case Keys.TwitchUseTwoAccounts:
                if (bool.TryParse(text[1].Trim(), out var val) && val) { Data[Keys.TwitchUseTwoAccounts] = "1"; }
                break;
              default:
                if (Data.ContainsKey((Keys)key)) { Data[(Keys)key] = text[1].Trim(); }
                else { Log.Warning("Not recognized key '{key}' on line {index} in {file} file.", text[0], lineIndex, FILENAME); }
                break;
            }
          }
          else
          {
            Log.Warning("Not recognized key '{key}' on line {index} in {file} file.", text[0], lineIndex, FILENAME);
            if (text[0] == "Name" || text[0] == "CustomerID" || text[0] == "Password") { oldTwitchNamesUsed = true; }
          }
        }
      }
      if (oldTwitchNamesUsed)
      {
        System.Windows.MessageBox.Show("Old Twitch data naming detected in Secrets.ini.\nPlease change to current one and restart the bot", "AbevBot: Old Twitch data in Secrets.ini");
      }


      // Check if all needed data was read
      if (Data[Keys.TwitchName].Length == 0 || Data[Keys.TwitchClientID].Length == 0 || Data[Keys.TwitchPassword].Length == 0)
      {
        Log.Error(string.Concat("Missing required information in {file} file.\r\n",
        "Look inside \"Required information in {file}\" section in README for help.\r\n",
        "{fileExample} was generated that can be used as a reference.\r\n",
        "You can delete {file} file to generate new one. ! WARNING - ALL DATA INSIDE IT WILL BE LOST !"
        ), FILENAME, FILENAME, FILENAMEEXAMPLE, FILENAME);
        return true;
      }
      else
      {
        // Check if TwitchClientID or TwitchPassword has changed since previous run
        StringBuilder sb = new();
        foreach (var b in SHA256.HashData(Encoding.UTF8.GetBytes(Data[Keys.TwitchClientID]))) { sb.Append(b.ToString("x2")); }
        var clientHash = sb.ToString();
        sb.Clear();
        foreach (var b in SHA256.HashData(Encoding.UTF8.GetBytes(Data[Keys.TwitchPassword]))) { sb.Append(b.ToString("x2")); }
        var passwordHash = sb.ToString();
        var previousClientHash = Database.GetValueFromConfig(Database.Keys.TwitchClientSHA);
        var previousPasswordHash = Database.GetValueFromConfig(Database.Keys.TwitchPasswordSHA);
        if (clientHash != previousClientHash || passwordHash != previousPasswordHash)
        {
          // The bot data has changed, reset saved Twitch OAuth tokens
          Data[Keys.TwitchOAuthToken] = string.Empty;
          Data[Keys.TwitchOAuthRefreshToken] = string.Empty;
          Data[Keys.TwitchSubOAuthToken] = string.Empty;
          Data[Keys.TwitchSubOAuthRefreshToken] = string.Empty;
          // Also store current client and password hash
          _ = Database.UpdateValueInConfig(Database.Keys.TwitchClientSHA, clientHash).Result;
          _ = Database.UpdateValueInConfig(Database.Keys.TwitchPasswordSHA, passwordHash).Result;
        }
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
      writer.WriteLine(";  7. Copy 'Name' and 'Client ID' into the fields below. Also generate new 'Client secret' and copy that too.");
      writer.WriteLine(string.Concat(Keys.TwitchName.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.TwitchClientID.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.TwitchPassword.ToString(), " = "));
      writer.WriteLine(";  8. Optional. Use bot instead of broadcaster account for sending chat messages.");
      writer.WriteLine(";  Requirements to use:");
      writer.WriteLine(";    - You have to have two Twitch accounts - one for the bot (ex. AbevBot), other on which you stream (ex. Abev08),");
      writer.WriteLine(";    - Name provided in 'TwitchName' field has to be the same as bot account name,");
      writer.WriteLine(";    - The app created in previous steps has to be on the bot account.");
      writer.WriteLine(";  While using it, once a while when new bot authentication is required it would be neccessary to be logged");
      writer.WriteLine(";  on both accounts and accept permissions on both of them.");
      writer.WriteLine(";  Available options: true - enabled, any other or empty - disabled");
      writer.WriteLine(string.Concat(Keys.TwitchUseTwoAccounts.ToString(), " = "));
      writer.WriteLine("; Tip: If you need to reset previously accepted authorization, change ClientID and/or Password to something else (can't be empty)");
      writer.WriteLine("; and run the bot. It will clear saved data even if it would fail to connect.");

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

      writer.WriteLine();
      writer.WriteLine();
      writer.WriteLine("; ----- Discord");
      writer.WriteLine("; The Discord integration is used to post 'Stream went online' messages.");
      writer.WriteLine("; Steps:");
      writer.WriteLine(";  1. Log in to: https://discord.com/login.");
      writer.WriteLine(";  2. Go to: https://discord.com/developers/applications?new_application=true and name the new Discord app.");
      writer.WriteLine(";  3. Click the 'OAuth2' button on the left.");
      writer.WriteLine(";  4. Copy 'Client ID' and 'Client Secret' into the fields below.");
      writer.WriteLine(";  5. In the 'Redirects' section, add new URI 'http://localhost:3000/' and save the changes.");
      writer.WriteLine(";  6. Click the 'Bot' button on the left.");
      writer.WriteLine(";  7. You can change the bot 'User Name'. This name will be displayed in bot's messages.");
      writer.WriteLine(";  8. Click 'Reset Token' or 'View Token' (whichever option is available), then copy new token into field below.");
      writer.WriteLine(";  9. In the Discord application, right click on the channel you want the bot to post messages to and copy channel link.");
      writer.WriteLine(";     Channel ID is the last part of the link you copied (after the last '/') - put it in the field below.");
      writer.WriteLine(string.Concat(Keys.DiscrodClientID.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.DiscordClientSecret.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.DiscordBotToken.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.DiscordChannelID.ToString(), " = "));
      writer.WriteLine("; Using discord channel as random video clips storage.");
      writer.WriteLine("; The bot will use messages with attached videos in specified channel as source of clips for random videos.");
      writer.WriteLine("; To enable this functionality:");
      writer.WriteLine(";  10. Log in to: https://discord.com/login.");
      writer.WriteLine(";  11. Go to: https://discord.com/developers/applications and click on a button with application (bot's User Name).");
      writer.WriteLine(";  12. Click the 'Bot' button on the left.");
      writer.WriteLine(";  13. Enable 'Message Content Intent'. This is required for the bot to read contents of messages in channels.");
      writer.WriteLine(";  14. In the Discord application, right click on the channel on which video clips will be posted and copy channel link.");
      writer.WriteLine(";     Channel ID is the last part of the link you copied (after the last '/') - put it in the field below.");
      writer.WriteLine(string.Concat(Keys.DiscordRandomVideosChannelID.ToString(), " = "));

      writer.WriteLine();
      writer.WriteLine();
      writer.WriteLine("; ----- YouTube");
      writer.WriteLine("; Used for posting YouTube livestream chat messages into Twitch chat.");
      writer.WriteLine("; Twitch chat messages ARE NOT posted on YouTube livestream.");
      writer.WriteLine("; Steps:");
      writer.WriteLine(";  1. Log in to your YouTube account.");
      writer.WriteLine(";  2. Go to: https://www.youtube.com/account_advanced and copy Channel ID into field below.");
      writer.WriteLine(";  3. Go to: https://console.cloud.google.com/projectselector2 and create new project (it may take a wile for Google to accept).");
      writer.WriteLine(";  4. Go to: https://console.developers.google.com/apis/api/youtube.googleapis.com and enable \"YouTube Data API v3\".");
      writer.WriteLine(";  5. Click on the \'Credentials\' (key in the menu on the left).");
      writer.WriteLine(";  6. Generate new API key from top menu and copy it into field below.");
      writer.WriteLine(string.Concat(Keys.YouTubeChannelID.ToString(), " = "));
      writer.WriteLine(string.Concat(Keys.YouTubeAPIKey.ToString(), " = "));
    }

    if (!example)
    {
      // Notify the user
      Log.Error("Missing required info in {file} file.\r\nThe file was generated.\r\nPlease fill it up and restart the bot.", FILENAME);
    }
  }
}
