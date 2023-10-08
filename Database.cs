using System;
using System.Data;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;

namespace AbevBot
{
  public static class Database
  {
    public enum Keys
    {
      Version,
      TwitchOAuth, TwitchOAuthRefresh,
      VolumeTTS, VolumeSounds, VolumeVideos,
      EnabledChatTTS, EnabledGamba, EnabledGambaLife, EnabledGambaAnimations, EnabledFight, EnabledWelcomeMessages
    }

    private const string DATABASEVERSION = "1.0";
    private const string DATABASEPATH = ".db";
    private const string DBCONNECTIONSTRING = $"Data Source={DATABASEPATH}; Version=3;";
    private static readonly SQLiteConnection Connection = new(DBCONNECTIONSTRING);

    public static bool Init()
    {
      MainWindow.ConsoleWarning(">> Initializing database.");

      // Check if database file exist
      FileInfo file = new(DATABASEPATH);
      if (!file.Exists)
      {
        MainWindow.ConsoleWarning(">> Creating new database file.");

        // Create file
        SQLiteConnection.CreateFile(DATABASEPATH);
        Connection.Open();
        file.Refresh();

        if (!file.Exists)
        {
          MainWindow.ConsoleWarning($">> Failed to create '{DATABASEPATH}' database file!");
          return true;
        }

        // Create tables
        SQLiteCommand command = new("CREATE TABLE Config (ID INTEGER NOT NULL UNIQUE, Name TEXT, Value TEXT, PRIMARY KEY(ID AUTOINCREMENT));", Connection);
        int affected = command.ExecuteNonQuery();
        if (affected != 0)
        {
          MainWindow.ConsoleWarning($">> Creating Config tablie in '{DATABASEPATH}' database returned wrong result!");
          return true;
        }

      }
      else { Connection.Open(); }

      string result;

      // Get database version
      result = GetValueOrCreateFromConfig(Keys.Version, DATABASEVERSION);
      if (result?.Length == 0) { return true; }
      else if (!result.Equals(DATABASEVERSION)) UpdateValueInConfig(Keys.Version, DATABASEVERSION).Wait();

      // Get tokens
      Secret.Data[Secret.Keys.OAuthToken] = GetValueOrCreateFromConfig(Keys.TwitchOAuth, string.Empty);
      Secret.Data[Secret.Keys.OAuthRefreshToken] = GetValueOrCreateFromConfig(Keys.TwitchOAuthRefresh, string.Empty);

      // Get sound slider values
      Config.VolumeTTS = float.Parse(GetValueOrCreateFromConfig(Keys.VolumeTTS, "0,4"), new NumberFormatInfo() { NumberDecimalSeparator = "," });
      Config.VolumeSounds = float.Parse(GetValueOrCreateFromConfig(Keys.VolumeSounds, "0,3"), new NumberFormatInfo() { NumberDecimalSeparator = "," });
      Config.VolumeVideos = float.Parse(GetValueOrCreateFromConfig(Keys.VolumeVideos, "0,8"), new NumberFormatInfo() { NumberDecimalSeparator = "," });
      MainWindow.I.SetVolumeSliderValues();

      // Get checkbox states
      Notifications.ChatTTSEnabled = bool.Parse(GetValueOrCreateFromConfig(Keys.EnabledChatTTS, "True"));
      MinigameGamba.Enabled = bool.Parse(GetValueOrCreateFromConfig(Keys.EnabledGamba, "True"));
      MinigameGamba.GambaLifeEnabled = bool.Parse(GetValueOrCreateFromConfig(Keys.EnabledGambaLife, "True"));
      MinigameGamba.GambaAnimationsEnabled = bool.Parse(GetValueOrCreateFromConfig(Keys.EnabledGambaAnimations, "True"));
      MinigameFight.Enabled = bool.Parse(GetValueOrCreateFromConfig(Keys.EnabledFight, "True"));
      Notifications.WelcomeMessagesEnabled = bool.Parse(GetValueOrCreateFromConfig(Keys.EnabledWelcomeMessages, "True"));
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

      command = new($"SELECT Value FROM Config WHERE Name='{name}';", Connection);
      result = command.ExecuteScalar();
      if (result is null || result is DBNull || ((string)result).Length == 0)
      {
        // Row doesn't exist, add it
        if (defaultValue?.Length > 0) command = new($"INSERT INTO Config (Name, Value) VALUES ('{name}', '{defaultValue}');", Connection);
        else command = new($"INSERT INTO Config (Name) VALUES ('{name}');", Connection);
        affected = command.ExecuteNonQuery();
        if (affected != 1)
        {
          MainWindow.ConsoleWarning($">> Couldn't add {name} to database Config table!");
          return string.Empty;
        }
        else { return defaultValue; }
      }

      return (string)result;
    }

    public static string GetValueFromConfig(Keys name)
    {
      SQLiteCommand command = new($"SELECT Value FROM Config WHERE Name='{name}';", Connection);
      string result = (string)command.ExecuteScalar();
      if (result is null)
      {
        // Row doesn't exist
        MainWindow.ConsoleWarning($">> Name {name} doesn't exist in database Config table!");
        return string.Empty;
      }
      return result;
    }

    public static async Task<bool> UpdateValueInConfig(Keys name, object value)
    {
      MainWindow.ConsoleWarning($">> Updating '{name}' in Config database.");
      if (Connection.State == ConnectionState.Closed) Connection.Open();

      SQLiteCommand command = new($"UPDATE Config SET Value='{value}' WHERE Name='{name}';", Connection);
      int affected = await command.ExecuteNonQueryAsync();

      Connection.Close();

      if (affected != 1)
      {
        MainWindow.ConsoleWarning($">> Couldn't update {name} to {value} in database Config table!");
        return false;
      }
      return true;
    }
  }
}
