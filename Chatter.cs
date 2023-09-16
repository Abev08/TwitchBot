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

    public string Name { get; set; }

    public int BackseatPoints { get; set; }
    public GambaStats Gamba { get; set; }

    /// <summary> Sets starting values for new chatter. </summary>
    private void InitChatter()
    {
      Gamba = new()
      {
        Points = STARTINGGAMBAPOINTS
      };
    }

    public void AddGambaPoints(int points)
    {
      UpdateRequired = true;

      if (points > 0) Gamba.Wins++;
      else if (points < 0) Gamba.Looses++;
      else MainWindow.ConsoleWarning($"> Gamba: {Name} gambler won {points} points. Something is not right Hmm.");

      Gamba.Points += points;
      if (Gamba.Points <= 0)
      {
        Gamba.Points = STARTINGGAMBAPOINTS;
        Gamba.Bankruptcies++;
      }
    }

    public void AddBackseatPoint(int point)
    {
      UpdateRequired = true;

      BackseatPoints += point;
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
        c.InitChatter();
        Chatters.Add(userID, c);
      }
      if (userName?.Length > 0) c.Name = userName; // Update chatter name if provided

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
      catch (Exception ex) { MainWindow.ConsoleWarning($">> {ex.Message}"); }
    }

    public static void LoadChattersFile()
    {
      if (Chatters != null) return;

      FileInfo chattersFile = new(CHATTERSFILE);
      if (chattersFile.Exists)
      {
        string data = File.ReadAllText(chattersFile.FullName);
        Chatters = JsonSerializer.Deserialize<Dictionary<long, Chatter>>(data);
      }
      else { Chatters = new(); }
    }
  }
}
