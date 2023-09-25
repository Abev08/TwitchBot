using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace AbevBot
{
  public class Chatter
  {
    private const string CHATTERSFILE = ".chatters";
    private const int STARTINGGAMBAPOINTS = 100;

    private static bool UpdateRequired;
    private static Dictionary<long, Chatter> Chatters;

    public long ID { get; set; }
    public string Name { get; set; }
    public int BackseatPoints { get; set; }
    public int RudePoints { get; set; }
    public GambaStats Gamba { get; set; }
    public FightStats Fight { get; set; }

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

    public void AddFightExp(int exp)
    {
      Fight.CurrentExp += exp;
      if (Fight.CurrentExp >= Fight.RequiredExp)
      {
        Fight.Level++;
        Fight.CurrentExp -= Fight.RequiredExp;
        Fight.CheckStats(true);
      }
      else { Fight.CheckStats(); }

      UpdateRequired = true;
    }

    public static Dictionary<long, Chatter> GetChatters()
    {
      if (Chatters is null) LoadChattersFile();
      return Chatters;
    }

    /// <summary> Returns a chatter or creates new one. </summary>
    public static Chatter GetChatterByID(long userID, string userName)
    {
      if (Chatters is null) LoadChattersFile();

      if (!Chatters.TryGetValue(userID, out Chatter c))
      {
        c = new();
        c.InitChatter(userID);
        Chatters.Add(userID, c);
      }
      if (userName?.Length > 0) c.Name = userName.Trim(); // Update chatter name if provided

      return c;
    }

    /// <summary> Returns a chatter or tries to create new one by acquiring all chatters and finding him. </summary>
    public static Chatter GetChatterByName(string userName)
    {
      if (Chatters is null) LoadChattersFile();

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
          return GetChatterByID(c.id, c.name);
        }
      }

      MainWindow.ConsoleWarning($">> Chatter {userName.Trim()} not found!");
      return null;
    }

    public static void UpdateChattersFile()
    {
      if (!UpdateRequired) return;
      UpdateRequired = false;
      if (Chatters is null) return;

      string data = JsonSerializer.Serialize(Chatters, new JsonSerializerOptions() { WriteIndented = true });

      try { File.WriteAllText(CHATTERSFILE, data); }
      catch (Exception ex) { MainWindow.ConsoleWarning($">> {ex}"); }
    }

    public static void LoadChattersFile()
    {
      if (Chatters != null) return;

      FileInfo chattersFile = new(CHATTERSFILE);
      if (chattersFile.Exists)
      {
        string data = File.ReadAllText(chattersFile.FullName);
        if (data is null || data.Length == 0)
        {
          Chatters = new();
          return;
        }
        Chatters = JsonSerializer.Deserialize<Dictionary<long, Chatter>>(data);
      }
      else { Chatters = new(); }
    }
  }
}
