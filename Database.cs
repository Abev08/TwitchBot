using System;
using System.Data;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;

using Serilog;

namespace AbevBot;

public static class Database
{
  public enum Keys
  {
    Version,
    TwitchOAuth, TwitchOAuthRefresh,
    VolumeAudio, VolumeVideo,
    EnabledChatTTS,
    EnabledGamba, EnabledGambaLife, EnabledGambaAnimations,
    EnabledFight,
    EnabledWelcomeMessages,
    EnabledSpotifySkip, EnabledSpotifyRequest,
    EnabledVanish,
    SpotifyOAuth, SpotifyOAuthRefresh,
    DiscordOAuth, DiscordOAuthRefresh, DiscordOAuthExpiration,
    LastOnline
  }

  private const string DATABASEVERSION = "1.0";
  private const string DATABASEPATH = ".db";
  private const string DBCONNECTIONSTRING = $"Data Source={DATABASEPATH}; Version=3;";
  private static readonly SQLiteConnection Connection = new(DBCONNECTIONSTRING);

  public static bool Init()
  {
    Log.Information("Initializing database.");

    // Check if database file exist
    FileInfo file = new(DATABASEPATH);
    if (!file.Exists)
    {
      Log.Information("Creating new database file.");

      // Create file
      SQLiteConnection.CreateFile(DATABASEPATH);
      Connection.Open();
      file.Refresh();

      if (!file.Exists)
      {
        Log.Error("Failed to create '{file}' database file!", DATABASEPATH);
        return true;
      }

      // Create tables
      SQLiteCommand command = new("CREATE TABLE Config (ID INTEGER NOT NULL UNIQUE, Name TEXT, Value TEXT, PRIMARY KEY(ID AUTOINCREMENT));", Connection);
      int affected = command.ExecuteNonQuery();
      if (affected != 0)
      {
        Log.Error("Creating Config tablie in '{file}' database returned wrong result!", DATABASEPATH);
        return true;
      }

    }
    else { Connection.Open(); }

    string result;

    // Get database version
    result = GetValueOrCreateFromConfig(Keys.Version, DATABASEVERSION);
    if (result?.Length == 0) { return true; }
    else if (!result.Equals(DATABASEVERSION)) UpdateValueInConfig(Keys.Version, DATABASEVERSION).Wait();
    DateTime.TryParse(GetValueOrCreateFromConfig(Keys.LastOnline, DateTime.MinValue.ToString()), out Config.BroadcasterLastOnline);

    // Get tokens
    Secret.Data[Secret.Keys.OAuthToken] = GetValueOrCreateFromConfig(Keys.TwitchOAuth, string.Empty);
    Secret.Data[Secret.Keys.OAuthRefreshToken] = GetValueOrCreateFromConfig(Keys.TwitchOAuthRefresh, string.Empty);
    Secret.Data[Secret.Keys.SpotifyOAuthToken] = GetValueOrCreateFromConfig(Keys.SpotifyOAuth, string.Empty);
    Secret.Data[Secret.Keys.SpotifyOAuthRefreshToken] = GetValueOrCreateFromConfig(Keys.SpotifyOAuthRefresh, string.Empty);
    Secret.Data[Secret.Keys.DiscordOAuthToken] = GetValueOrCreateFromConfig(Keys.DiscordOAuth, string.Empty);
    Secret.Data[Secret.Keys.DiscordOAuthRefreshToken] = GetValueOrCreateFromConfig(Keys.DiscordOAuthRefresh, string.Empty);
    DateTime.TryParse(GetValueOrCreateFromConfig(Keys.DiscordOAuthExpiration, DateTime.MinValue.ToString()), out AccessTokens.DiscordOAuthTokenExpiration);

    // Get sound slider values
    Config.VolumeAudio = float.Parse(GetValueOrCreateFromConfig(Keys.VolumeAudio, "0,4"), new NumberFormatInfo() { NumberDecimalSeparator = "," });
    Config.VolumeVideo = float.Parse(GetValueOrCreateFromConfig(Keys.VolumeVideo, "0,8"), new NumberFormatInfo() { NumberDecimalSeparator = "," });
    MainWindow.I.SetVolumeSliderValues();

    // Get checkbox states
    Notifications.ChatTTSEnabled = bool.Parse(GetValueOrCreateFromConfig(Keys.EnabledChatTTS, "True"));
    MinigameGamba.Enabled = bool.Parse(GetValueOrCreateFromConfig(Keys.EnabledGamba, "True"));
    MinigameGamba.GambaLifeEnabled = bool.Parse(GetValueOrCreateFromConfig(Keys.EnabledGambaLife, "True"));
    MinigameGamba.GambaAnimationsEnabled = bool.Parse(GetValueOrCreateFromConfig(Keys.EnabledGambaAnimations, "True"));
    MinigameFight.Enabled = bool.Parse(GetValueOrCreateFromConfig(Keys.EnabledFight, "True"));
    Notifications.WelcomeMessagesEnabled = bool.Parse(GetValueOrCreateFromConfig(Keys.EnabledWelcomeMessages, "True"));
    Spotify.SkipEnabled = bool.Parse(GetValueOrCreateFromConfig(Keys.EnabledSpotifySkip, "True"));
    Spotify.RequestEnabled = bool.Parse(GetValueOrCreateFromConfig(Keys.EnabledSpotifyRequest, "True"));
    Chat.VanishEnabled = bool.Parse(GetValueOrCreateFromConfig(Keys.EnabledVanish, "True"));
    MainWindow.I.SetEnabledStatus();

    Connection.Close();
    return false;
  }

  /// <summary><para> Returns value readed from Config table. </para>
  /// <para> If the value is not found creates new one using defaultValue. </para></summary>
  /// <param name="name">Name of value to be read.</param>
  /// <param name="defaultValue">Default value.</param>
  /// <returns>Readed value.</returns>
  private static string GetValueOrCreateFromConfig(Keys name, string defaultValue)
  {
    SQLiteCommand command;
    object result;
    int affected;

    command = new($"SELECT * FROM Config WHERE Name='{name}';", Connection);
    result = command.ExecuteReader();
    if (result is SQLiteDataReader reader && reader.HasRows && reader.StepCount == 1)
    {
      if (reader.Read()) { result = reader.IsDBNull("Value") ? string.Empty : reader.GetString("Value"); }
      else
      {
        Log.Warning("Error when reading database for key {name}", name);
        result = string.Empty;
      }
      reader.Close();
      return (string)result;
    }
    else
    {
      // Row doesn't exist, add it
      if (defaultValue?.Length > 0) command = new($"INSERT INTO Config (Name, Value) VALUES ('{name}', '{defaultValue}');", Connection);
      else command = new($"INSERT INTO Config (Name) VALUES ('{name}');", Connection);
      affected = command.ExecuteNonQuery();
      if (affected != 1)
      {
        Log.Warning("Couldn't add {name} to database Config table!", name);
        return string.Empty;
      }
      else { return defaultValue; }
    }
  }

  public static string GetValueFromConfig(Keys name)
  {
    SQLiteCommand command = new($"SELECT Value FROM Config WHERE Name='{name}';", Connection);
    string result = (string)command.ExecuteScalar();
    if (result is null)
    {
      // Row doesn't exist
      Log.Warning("Name {name} doesn't exist in database Config table!", name);
      return string.Empty;
    }
    return result;
  }

  public static async Task<bool> UpdateValueInConfig(Keys name, object value)
  {
    Log.Information("Updating '{name}' in Config database.", name);
    if (Connection.State == ConnectionState.Closed) Connection.Open();

    SQLiteCommand command = new($"UPDATE Config SET Value='{value}' WHERE Name='{name}';", Connection);
    int affected = await command.ExecuteNonQueryAsync();

    Connection.Close();

    if (affected != 1)
    {
      Log.Warning("Couldn't update {name} in database Config table!", name);
      return false;
    }
    return true;
  }
}
