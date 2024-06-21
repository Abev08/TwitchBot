using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

using Serilog;

namespace AbevBot
{
  public class Chatter
  {
    private const string CHATTERSFILE = ".chatters";
    private const int STARTINGGAMBAPOINTS = 100;

    private static bool UpdateRequired;
    private static readonly Dictionary<long, Chatter> Chatters = new();
    public static readonly List<string> AlwaysReadTTSFromThem = new();
    public static readonly List<string> OverpoweredInFight = new();
    public static readonly TimeSpan OfflineTimeout = TimeSpan.FromMinutes(30);

    public long ID { get; set; }
    public string Name { get; set; }
    public DateTime LastTimeFollowed { get; set; } = DateTime.MinValue;
    public int BackseatPoints { get; set; }
    public int RudePoints { get; set; }
    public GambaStats Gamba { get; set; }
    public FightStats Fight { get; set; }
    public DateTime LastWelcomeMessage { get; set; } = DateTime.MinValue;
    public string WelcomeMessage { get; set; } = string.Empty;
    [JsonIgnore]
    public DateTime LastChatted { get; set; } = DateTime.MinValue;
    public DateTime LastSongRequest { get; set; } = DateTime.MinValue;
    private static bool StopFollowBotsActive = false;

    /// <summary> Sets starting values for new chatter. </summary>
    private void InitChatter(long id)
    {
      ID = id;
      Gamba = new()
      {
        Points = STARTINGGAMBAPOINTS
      };
    }

    public void AddGambaPoints(int points)
    {
      if (points > 0) { Gamba.Wins++; }
      else if (points < 0) { Gamba.Looses++; }
      else { Log.Warning("Gamba: {Name} gambler won {points} points. Something is not right Hmm.", Name, points); }

      Gamba.Points += points;
      if (Gamba.Points <= 0)
      {
        Gamba.Points = STARTINGGAMBAPOINTS;
        Gamba.Bankruptcies++;
      }

      UpdateRequired = true;
    }

    public void AddBackseatPoint(int point)
    {
      BackseatPoints += point;

      UpdateRequired = true;
    }

    public void AddRudePoint(int point)
    {
      RudePoints += point;

      UpdateRequired = true;
    }

    public void AddFightExp(float exp)
    {
      Fight.CurrentExp += MathF.Round(exp, 2);
      if (Fight.CurrentExp >= Fight.RequiredExp)
      {
        Fight.Level++;
        Fight.CurrentExp -= Fight.RequiredExp;
        Fight.CheckStats(Name, true);
      }
      else { Fight.CheckStats(Name); }

      UpdateRequired = true;
    }

    public void SetLastTimeFollowedToNow()
    {
      LastTimeFollowed = DateTime.Now;
      UpdateRequired = true;
    }

    public void SetLastWelcomeMessageToNow()
    {
      LastWelcomeMessage = DateTime.Now.Date;
      UpdateRequired = true;
    }

    public void SetWelcomeMessage(string msg)
    {
      WelcomeMessage = msg;
      UpdateRequired = true;
    }

    public static Dictionary<long, Chatter> GetChatters()
    {
      if (Chatters is null || Chatters.Count == 0) LoadChattersFile();
      return Chatters;
    }

    /// <summary> Returns a chatter or creates new one. </summary>
    public static Chatter GetChatterByID(long userID, string userName)
    {
      if (Chatters is null || Chatters.Count == 0) LoadChattersFile();

      Chatter c;

      lock (Chatters)
      {
        if (!Chatters.TryGetValue(userID, out c))
        {
          c = new();
          c.InitChatter(userID);
          Chatters.Add(userID, c);
        }
        if (userName?.Length > 0) c.Name = userName.Trim(); // Update chatter name if provided
      }

      return c;
    }

    /// <summary> Returns a chatter or tries to create new one by acquiring all chatters and finding him. </summary>
    public static Chatter GetChatterByName(string userName)
    {
      if (Chatters is null || Chatters.Count == 0) LoadChattersFile();
      if (userName is null || userName.Length == 0)
      {
        Log.Warning("Get chatter by name provided empty user name!");
        return null;
      }

      string name = userName.Trim().ToLower();
      var chatter = Chatters.GetEnumerator();
      while (chatter.MoveNext())
      {
        if (chatter.Current.Value.Name.ToLower().Equals(name)) return chatter.Current.Value;
      }

      var chatters = Chat.GetChatters();
      foreach (var c in chatters)
      {
        if (c.name.ToLower().Equals(name))
        {
          Chatter cc = GetChatterByID(c.id, c.name);
          cc.LastChatted = DateTime.Now;
          return cc;
        }
      }

      Log.Warning("Chatter {name} not found!", userName.Trim());
      return null;
    }

    public static void UpdateChattersFile(bool forceUpdate = false)
    {
      if (!UpdateRequired && !forceUpdate) return;
      UpdateRequired = false;
      if (Chatters is null || Chatters.Count == 0) return;

      Log.Information("Updating chatters file.");

      string data;
      lock (Chatters)
      {
        // Clean up chatters when force update is active - the bot is being closed
        if (forceUpdate)
        {
          var chatter = Chatters.GetEnumerator();
          while (chatter.MoveNext())
          {
            if (!CheckIfChatterIsActive(chatter.Current.Value)) Chatters.Remove(chatter.Current.Value.ID);
          }
        }

        data = JsonSerializer.Serialize(Chatters, new JsonSerializerOptions() { WriteIndented = true });
      }

      try { File.WriteAllText(CHATTERSFILE, data); }
      catch (Exception ex) { Log.Error("Error when updating chatters file: {ex}", ex); }
    }

    /// <summary> Loads chatters from chatters file. </summary>
    public static void LoadChattersFile()
    {
      if (Chatters?.Count > 0) return;

      FileInfo chattersFile = new(CHATTERSFILE);
      if (chattersFile.Exists)
      {
        string data = File.ReadAllText(chattersFile.FullName);
        if (data is null || data.Length == 0) { return; }
        lock (Chatters)
        {
          var chat = JsonSerializer.Deserialize<Dictionary<long, Chatter>>(data);
          foreach (var chatter in chat)
          {
            if (CheckIfChatterIsActive(chatter.Value)) Chatters.Add(chatter.Key, chatter.Value);
          }
        }
      }
    }

    /// <summary> Checks several things to assess whether the chatter is active. </summary>
    /// <param name="chatter">Chatter to be checked.</param>
    /// <returns>true if chatter is active, otherwise false</returns>
    private static bool CheckIfChatterIsActive(Chatter chatter)
    {
      if (chatter.Fight?.Level > 0) return true;
      if (chatter.Gamba?.Wins != 0 || chatter.Gamba?.Looses != 0) return true;
      if (chatter.RudePoints > 0) return true;
      if (chatter.BackseatPoints > 0) return true;
      if (chatter.WelcomeMessage?.Length > 0) return true;
      // if (chatter.LastTimeFollowed != DateTime.MinValue) return true;

      // All checks failed - the chatter is inactive and can be forgotten peepoSad
      return false;
    }

    /// <summary><para> Looks through Chatters array to find every chatter that followed in less than 20 seconds. </para>
    /// <para> Then bans find users for 10 hours, deletes them from Chatters array and deletes follow notifications. </para></summary>
    public static void StopFollowBots()
    {
      if (StopFollowBotsActive) return;
      StopFollowBotsActive = true;

      Log.Warning("Stop follow bots clicked!");
      TimeSpan followedLimit = TimeSpan.FromSeconds(20);
      var now = DateTime.Now;

      // First clean up notifications because it's faster than sending http messages for bans
      Notifications.CleanFollowNotifications();

      // Ban the chatters
      LoadChattersFile();
      Task.Run(() =>
      {
        lock (Chatters)
        {
          var chatter = Chatters.GetEnumerator();
          while (chatter.MoveNext())
          {
            if (now - chatter.Current.Value.LastTimeFollowed <= followedLimit)
            {
              // Followed in last x seconds - BAN!
              Chat.BanChatter("follow bot?", chatter.Current.Value, 36000); // 10 hours
              Chatters.Remove(chatter.Current.Value.ID);
            }
          }
        }
        StopFollowBotsActive = false;
      });
    }

    public override string ToString() { return Name; }
  }

  public class SkipSongChatter
  {
    public long ChatterID { get; set; }
    public DateTime TimeRequested { get; set; }
  }
}
