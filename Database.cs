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
    TwitchSubOAuth, TwitchSubOAuthRefresh,
    TwitchClientSHA, TwitchPasswordSHA,
    VolumeAudio, VolumeVideo,
    EnabledChatTTS,
    EnabledGamba, EnabledGambaLife, EnabledGambaAnimations,
    EnabledFight,
    EnabledWelcomeMessages,
    EnabledSpotifySkip, EnabledSpotifyRequest,
    EnabledVanish,
    SpotifyOAuth, SpotifyOAuthRefresh,
    DiscordOAuth, DiscordOAuthRefresh, DiscordOAuthExpiration,
    LastOnline,
    WindowAudioDisabled
  }

  private const string DATABASEVERSION = "1.0";
  private const string DATABASEPATH = ".db";
  private const string DBCONNECTIONSTRING = $"Data Source={DATABASEPATH}; Version=3;";
  public static readonly SQLiteConnection Connection = new(DBCONNECTIONSTRING);

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
    }
    else { Connection.Open(); }

    // Try to create tables, if they already exists, "CREATE TABLE" will return an error if (CreateTable("CREATE TABLE death_counter (id INTEGER NOT NULL UNIQUE, name TEXT, counters TEXT, PRIMARY KEY(id AUTOINCREMENT));")) { return true; }
    if (CreateTable("Config", "(ID INTEGER NOT NULL UNIQUE, Name TEXT, Value TEXT, PRIMARY KEY(ID AUTOINCREMENT))")) { return true; }
    if (CreateTable("counters", "(id INTEGER NOT NULL UNIQUE, name TEXT, counters TEXT, PRIMARY KEY(id AUTOINCREMENT))")) { return true; }

    string result;
    // Get database version
    result = GetValueOrCreateFromConfig(Keys.Version, DATABASEVERSION);
    if (result?.Length == 0) { return true; }
    else if (!result.Equals(DATABASEVERSION)) UpdateValueInConfig(Keys.Version, DATABASEVERSION).Wait();
    DateTime.TryParse(GetValueOrCreateFromConfig(Keys.LastOnline, DateTime.MinValue.ToString()), out Config.BroadcasterLastOnline);

    // Get tokens
    Secret.Data[Secret.Keys.TwitchOAuthToken] = GetValueOrCreateFromConfig(Keys.TwitchOAuth, string.Empty);
    Secret.Data[Secret.Keys.TwitchOAuthRefreshToken] = GetValueOrCreateFromConfig(Keys.TwitchOAuthRefresh, string.Empty);
    Secret.Data[Secret.Keys.TwitchSubOAuthToken] = GetValueOrCreateFromConfig(Keys.TwitchSubOAuth, string.Empty);
    Secret.Data[Secret.Keys.TwitchSubOAuthRefreshToken] = GetValueOrCreateFromConfig(Keys.TwitchSubOAuthRefresh, string.Empty);
    Secret.Data[Secret.Keys.SpotifyOAuthToken] = GetValueOrCreateFromConfig(Keys.SpotifyOAuth, string.Empty);
    Secret.Data[Secret.Keys.SpotifyOAuthRefreshToken] = GetValueOrCreateFromConfig(Keys.SpotifyOAuthRefresh, string.Empty);
    Secret.Data[Secret.Keys.DiscordOAuthToken] = GetValueOrCreateFromConfig(Keys.DiscordOAuth, string.Empty);
    Secret.Data[Secret.Keys.DiscordOAuthRefreshToken] = GetValueOrCreateFromConfig(Keys.DiscordOAuthRefresh, string.Empty);
    GetValueOrCreateFromConfig(Keys.TwitchClientSHA, string.Empty); // Just create the key if not present
    GetValueOrCreateFromConfig(Keys.TwitchPasswordSHA, string.Empty); // Just create the key if not present
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

    // Connection.Close();
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
    var result = command.ExecuteScalar();
    if (result is null)
    {
      // Row doesn't exist
      Log.Warning("Name {name} doesn't exist in database Config table!", name);
      return string.Empty;
    }
    else if (result is DBNull) { return string.Empty; } // The Value is NULL
    return (string)result;
  }

  public static async Task<bool> UpdateValueInConfig(Keys name, object value)
  {
    Log.Information("Updating '{name}' in Config database.", name);
    if (Connection.State == ConnectionState.Closed) Connection.Open();

    SQLiteCommand command = new($"UPDATE Config SET Value='{value}' WHERE Name='{name}';", Connection);
    int affected = await command.ExecuteNonQueryAsync();

    // Connection.Close();

    if (affected != 1)
    {
      Log.Warning("Couldn't update {name} in database Config table!", name);
      return false;
    }
    return true;
  }

  /// <summary> Creates table in the database according to provided sql command. </summary>
  /// <returns><value>true</value> if an error occured, otherwise <value>false</value></returns>
  private static bool CreateTable(string name, string columns)
  {
    SQLiteCommand cmd = new($"CREATE TABLE {name} {columns};", Connection);
    try
    {
      if (cmd.ExecuteNonQuery() != 0)
      {
        Log.Error("Creating '{name}' table in '{file}' database returned wrong result!", name, DATABASEPATH);
        return true;
      }
    }
    catch (SQLiteException ex)
    {
      if (ex.ErrorCode == 1) { } // Table already exists
      else
      {
        Log.Error("Creating '{name}' table in '{file}' database returned an error: {ex}", name, DATABASEPATH, ex.Message);
        return true;
      }
    }
    return false;
  }

  public static void UpdateCounterData(Counters counter)
  {
    counter.Dirty = true;

    Task.Run(() =>
    {
      Log.Information("Updating '{name}' in counters database.", counter.Name);
      var data = counter.GetCountersData();
      if (Connection.State == ConnectionState.Closed) Connection.Open();

      SQLiteCommand command = new($"UPDATE counters SET counters='{data}' WHERE name='{counter.Name}';", Connection);
      int affected = command.ExecuteNonQuery();
      if (affected == 0)
      {
        // Nothing was affected, so probably the counter doesn't exist, add it
        command = new($"INSERT INTO counters (name, counters) VALUES ('{counter.Name}', '{data}');", Connection);
        affected = command.ExecuteNonQuery();
        if (affected != 1) { Log.Warning("Couldn't add {name} to database counters table!", counter.Name); }
      }
    });
  }

  public static Counters GetLastCounters()
  {
    if (Connection.State == ConnectionState.Closed) Connection.Open();
    Counters c = null;
    SQLiteCommand command = new("SELECT * FROM counters ORDER BY id DESC LIMIT 1;", Connection);
    var reader = command.ExecuteReader();
    if (reader.HasRows && reader.Read())
    {
      c = new();
      for (int i = 0; i < reader.FieldCount; i++)
      {
        if (reader.GetFieldType(i) == typeof(string))
        {
          var s = reader.GetString(i);
          if (i == 1) { c.Name = s; }
          else if (i == 2) { c.ParseCountersData(s); }
        }
      }
    }

    reader.Close();
    return c;
  }
}
