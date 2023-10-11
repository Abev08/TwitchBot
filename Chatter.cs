using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

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
    public static readonly TimeSpan OfflineTimeout = new(0, 30, 0);

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
      if (points > 0) Gamba.Wins++;
      else if (points < 0) Gamba.Looses++;
      else MainWindow.ConsoleWarning($"> Gamba: {Name} gambler won {points} points. Something is not right Hmm.");

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
      LastTimeFollowed = DateTime.Now.Date;
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

      MainWindow.ConsoleWarning($">> Chatter {userName.Trim()} not found!");
      return null;
    }

    public static void UpdateChattersFile(bool forceUpdate = false)
    {
      if (!UpdateRequired && !forceUpdate) return;
      UpdateRequired = false;
      if (Chatters is null || Chatters.Count == 0) return;

      MainWindow.ConsoleWarning(">> Updating chatters file.");

      string data;
      lock (Chatters)
      {
        data = JsonSerializer.Serialize(Chatters, new JsonSerializerOptions() { WriteIndented = true });
      }

      try { File.WriteAllText(CHATTERSFILE, data); }
      catch (Exception ex) { MainWindow.ConsoleWarning($">> {ex.Message}"); }
    }

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
            Chatters.Add(chatter.Key, chatter.Value);
          }
        }
      }
    }
  }
}
