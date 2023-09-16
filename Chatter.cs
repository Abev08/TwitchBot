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

    public static Chatter GetChatter(long userID, string userName)
    {
      if (Chatters is null) LoadChattersFile();

      if (!Chatters.TryGetValue(userID, out Chatter c))
      {
        c = new();
        c.InitChatter();
        Chatters.Add(userID, c);
      }
      c.Name = userName; // Always update chatter name

      return c;
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

    public static SortedList<int, string> GetGambaLadder()
    {
      SortedList<int, string> ladder = new(GambaMinigame.GambaLadderComparer);

      var chatter = Chatters.GetEnumerator();
      Chatter c;
      while (chatter.MoveNext())
      {
        c = chatter.Current.Value;
        if (c.Gamba.Wins != 0 || c.Gamba.Looses != 0)
        {
          ladder.Add(c.Gamba.Points, c.Name);
        }
      }

      return ladder;
    }
  }
}
