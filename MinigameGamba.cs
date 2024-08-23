using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace AbevBot
{
  public static class MinigameGamba
  {
    private const int MAXLADDERENTRIES = 10;
    private const int GAMBALIFELOSTTIMEOUTSECONDS = 30;
    private static readonly TimeSpan GAMBATIMEOUT = new(0, 5, 0);
    private static readonly TimeSpan ANIMATIONTIMEOUT = new(0, 3, 0);
    public static readonly GambaLadderComparer GambaLadderComparer = new();
    private static readonly bool AnimationVideosAvailable = LoadAnimations();
    private static FileInfo[] AnimationJackpot, AnimationWin, AnimationLoose;
    /// <summary> List of emotes used in chat messages. </summary>
    private static readonly List<string> GambaMachineEmotes = new() { "YEP", "Prayge", "Sadge", "peepoHappy", "UHM" };
    /// <summary> Jackpot emote used in chat message. </summary>
    private const string GambaMachineEmoteJackpot = "Susge";

    public static bool Enabled { get; set; }
    public static bool GambaLifeEnabled { get; set; }
    public static bool GambaAnimationsEnabled { get; set; }
    private static DateTime LastAnimation;

    public static void NewGamba(long userID, string userName, string message)
    {
      if (!Enabled)
      {
        Chat.AddMessageToQueue($"@{userName} !gamba disabled peepoSad");
        return;
      }

      Task.Run(() => StartNewGamba(userID, userName, message));
    }

    private static void StartNewGamba(long userID, string userName, string message)
    {
      Chatter chatter = Chatter.GetChatterByID(userID, userName);

      string msg = message.Replace("\U000e0000", "").Trim().ToLower(); // Removing ShadeEleven's "white space" characters :)
      int pointsToRoll;
      if (msg.Length == 0) { GetStats(chatter); return; }
      else if (msg.Equals("ladder")) { GetLadder(); return; }
      else if (msg.Equals("help"))
      {
        Chat.AddMessageToQueue(string.Concat(
         "GAMBA minigame. ",
         "\" !gamba <empty/value/quarter/half/all/ladder/life> \". ",
         "empty - your stats, ",
         "value - gamble provided amount, ",
         "quarter - gamble 1/4 of your points, ",
         "half - gamble 1/2 of your points, ",
         "all - gamble all your points, ",
         "ladder - the ladder, ",
         "life - gamble your life"
        ));
        return;
      }
      else if (msg.Equals("all")) { pointsToRoll = chatter.Gamba.Points; }
      else if (msg.Equals("half")) { pointsToRoll = chatter.Gamba.Points / 2; }
      else if (msg.Equals("quarter")) { pointsToRoll = chatter.Gamba.Points / 4; }
      else if (msg.Equals("life"))
      {
        if (!GambaLifeEnabled)
        {
          Chat.AddMessageToQueue($"@{chatter.Name} !gamba life disabled peepoSad");
          return;
        }

        // Gambling a life - if lost the chatter will get banned, if won nothing?
        Chat.AddMessageToQueue(string.Concat("@", chatter.Name, " GAMBA is putting a life at risk peepoShake"));

        Task.Delay(2000).Wait();

        if (Random.Shared.Next(0, 100) < 50)
        {
          // won
          Chat.AddMessageToQueue(string.Concat("@", chatter.Name, " this time fate was on your side monkaS"));
        }
        else
        {
          // lost
          Chat.AddMessageToQueue(string.Concat("@", chatter.Name, " has left the chat Deadge"));
          Chat.BanChatter("Lost in !gamba life", chatter.ID, durSeconds: GAMBALIFELOSTTIMEOUTSECONDS);
        }
        return;
      }
      else
      {
        // try to parse points
        if (int.TryParse(msg, out pointsToRoll))
        {
          if (pointsToRoll <= 0)
          {
            Chat.AddMessageToQueue($"@{chatter.Name} you can't gamble negative amount of points WeirdDude");
            return; // Provided negative amount of points or a zero
          }
          if (pointsToRoll > chatter.Gamba.Points)
          {
            Chat.AddMessageToQueue($"@{chatter.Name} you don't have that many points to gamble WeirdDude");
            return; // Provided more points that the chatter has
          }
        }
        else
        {
          Chat.AddMessageToQueue($"@{chatter.Name} that's not a valid amount WeirdDude");
          return; // Points amount was not a number?
        }
      }

      if (DateTime.Now - chatter.Gamba.LastGamba < GAMBATIMEOUT)
      {
        TimeSpan restTimer = GAMBATIMEOUT - (DateTime.Now - chatter.Gamba.LastGamba);
        string amount;
        string t;
        if (restTimer.TotalSeconds < 60)
        {
          var s = Math.Ceiling(restTimer.TotalSeconds);
          amount = s.ToString();
          t = s > 1 ? "seconds" : "second";
        }
        else
        {
          var s = Math.Round(restTimer.TotalMinutes);
          amount = s.ToString();
          t = s > 1 ? "minutes" : "minute";
        }

        Chat.AddMessageToQueue(string.Concat(
          "@", chatter.Name, " you're still shaking, you need to rest for another ",
          amount, " ", t
        ));
        return; // The gamba timeout
      }

      chatter.Gamba.LastGamba = DateTime.Now; // Remember last time the chatter gambled

      if (pointsToRoll == 0) pointsToRoll = 1; // At least one point to roll

      // Gamba machine instead of chat message
      if (GambaAnimationsEnabled && AnimationVideosAvailable && DateTime.Now - LastAnimation >= ANIMATIONTIMEOUT)
      {
        LastAnimation = DateTime.Now;
        bool won = Random.Shared.Next(0, 100) < 50;
        bool jackpot;
        int pointsReceived;
        FileInfo videoPath;
        if (won)
        {
          jackpot = Random.Shared.Next(0, 100) == 0; // 1% chance
          if (jackpot)
          {
            pointsReceived = pointsToRoll * 100;
            videoPath = AnimationJackpot[Random.Shared.Next(0, AnimationJackpot.Length)];
          }
          else
          {
            pointsReceived = pointsToRoll;
            videoPath = AnimationWin[Random.Shared.Next(0, AnimationWin.Length)];
          }
        }
        else
        {
          pointsReceived = -pointsToRoll;
          videoPath = AnimationLoose[Random.Shared.Next(0, AnimationLoose.Length)];
        }

        chatter.AddGambaPoints(pointsReceived);
        MainWindow.I.GambaAnimationStart(videoPath, chatter.Name, pointsToRoll, pointsReceived);
        Server.GambaAnimation($"Resources/Gamba/{videoPath.Name}", chatter.Name, pointsToRoll, pointsReceived);
        return;
      }

      var sb = new StringBuilder();
      sb.Append('@').Append(chatter.Name);
      sb.Append(" GAMBA is putting ").Append(pointsToRoll).Append(" points at risk peepoShake");
      sb.Append(" The gamba machine stops spinning: ");

      if (Random.Shared.Next(0, 100) < 50)
      {
        // won
        // Check for a jackpot 1% chance
        bool jackpot = false;
        string emote;
        if (Random.Shared.Next(0, 100) == 0) // 1% chance
        {
          jackpot = true;
          pointsToRoll *= 100;
          emote = GambaMachineEmoteJackpot;
        }
        else
        {
          // No jackpot, randomise win emote
          var idx = Random.Shared.Next(GambaMachineEmotes.Count);
          emote = GambaMachineEmotes[idx];
        }

        sb.Append(emote).Append(" | ")
          .Append(emote).Append(" | ")
          .Append(emote);

        sb.Append(" You won ").Append(pointsToRoll).Append(" points");
        if (jackpot)
        {
          sb.Append(" hitting a jackpot OMEGALUL");
        }
        sb.Append(" and now have ").Append(chatter.Gamba.Points + pointsToRoll).Append(" points peepoHappy");

        chatter.AddGambaPoints(pointsToRoll);
      }
      else
      {
        // lost
        int newPoints = chatter.Gamba.Points - pointsToRoll;
        if (newPoints < 0) { newPoints = 0; }
        var idx = new int[3]; // Indexes of gamba emotes
        for (int i = 0; i < 3; i++)
        {
          do
          {
            idx[i] = Random.Shared.Next(GambaMachineEmotes.Count);
          } while (i == 2 && idx[2] == idx[0] && idx[2] == idx[1]);
        }

        sb.Append(GambaMachineEmotes[idx[0]]).Append(" | ")
          .Append(GambaMachineEmotes[idx[1]]).Append(" | ")
          .Append(GambaMachineEmotes[idx[2]]);

        sb.Append(" You lost ").Append(pointsToRoll).Append(" points");
        sb.Append(" and now have ").Append(newPoints).Append(" points PepeLaugh");
        if (newPoints <= 0)
        {
          var b = chatter.Gamba.Bankruptcies + 1;
          var bs = b > 1 ? " times" : " time";
          sb.Append(" having gone bankrupt ").Append(b).Append(bs).Append(" GAMBAADDICT");
        }

        chatter.AddGambaPoints(-pointsToRoll);
      }

      Chat.AddMessageToQueue(sb.ToString());
    }

    private static void GetStats(Chatter chatter)
    {
      Chat.AddMessageToQueue(string.Concat(
        "@", chatter.Name, " you have ",
        chatter.Gamba.Points, " points, ",
        chatter.Gamba.Wins, " wins and ",
        chatter.Gamba.Looses, " looses.",
        chatter.Gamba.Bankruptcies > 0 ? $" Also you went bankrupt {chatter.Gamba.Bankruptcies} times." : "",
        " GAMBA"
      ));
    }

    private static void GetLadder()
    {
      List<(int, string)> ladder = new();

      var chatter = Chatter.GetChatters().GetEnumerator();
      Chatter c;
      while (chatter.MoveNext())
      {
        c = chatter.Current.Value;
        if (c.Gamba.Wins != 0 || c.Gamba.Looses != 0)
        {
          ladder.Add((c.Gamba.Points, c.Name));
        }
      }

      if (ladder.Count == 0)
      {
        Chat.AddMessageToQueue("GAMBA ladder -> empty... No gamblers in chat peepoHappy");
        return;
      }

      ladder.Sort((a, b) =>
      {
        if (a.Item1 == b.Item1) return b.Item2.CompareTo(a.Item2);
        return b.Item1.CompareTo(a.Item1);
      });

      StringBuilder sb = new();
      sb.Append("GAMBA ladder -> ");

      var ladderEntry = ladder.GetEnumerator();
      int index = 0;
      bool first = true;
      while (ladderEntry.MoveNext())
      {
        if (index >= MAXLADDERENTRIES) break;
        if (!first) sb.Append(" | ");

        sb.Append(ladderEntry.Current.Item2).Append(": ").Append(ladderEntry.Current.Item1);

        first = false;
        index++;
      }

      Chat.AddMessageToQueue(sb.ToString());
    }

    public static string GetCommands()
    {
      if (!Enabled) return string.Empty;

      if (!GambaLifeEnabled) return "!gamba <empty/value/quarter/half/all/ladder/help>";
      return "!gamba <empty/value/quarter/half/all/ladder/life/help>";
    }

    private static bool LoadAnimations()
    {
      FileInfo file;

      // Jackpot
      AnimationJackpot = new FileInfo[1];
      file = new("Resources/Gamba/GambaJackpot.mp4");
      if (!file.Exists) return false;
      AnimationJackpot[0] = file;

      // Wins
      AnimationWin = new FileInfo[2];
      file = new("Resources/Gamba/GambaWin1.mp4");
      if (!file.Exists) return false;
      AnimationWin[0] = file;
      file = new("Resources/Gamba/GambaWin2.mp4");
      if (!file.Exists) return false;
      AnimationWin[1] = file;

      // Looses
      AnimationLoose = new FileInfo[2];
      file = new("Resources/Gamba/GambaLoose1.mp4");
      if (!file.Exists) return false;
      AnimationLoose[0] = file;
      file = new("Resources/Gamba/GambaLoose2.mp4");
      if (!file.Exists) return false;
      AnimationLoose[1] = file;

      return true;
    }
  }

  public class GambaStats
  {
    public int Points { get; set; }
    public int Wins { get; set; }
    public int Looses { get; set; }
    public int Bankruptcies { get; set; }
    public DateTime LastGamba { get; set; }
  }

  public class GambaLadderComparer : IComparer<int>
  {
    public int Compare(int x, int y) { return y - x; }
  }
}
