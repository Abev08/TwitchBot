using System;
using System.Collections.Generic;
using System.Text;

namespace AbevBot
{
  public static class MinigameGamba
  {
    private const int MAXLADDERENTRIES = 10;

    public static readonly GambaLadderComparer GambaLadderComparer = new();

    public static void NewGamba(long userID, string userName, string message)
    {
      Chatter chatter = Chatter.GetChatterByID(userID, userName);

      string msg = message.Trim().ToLower();
      int pointsToRoll;
      if (msg.Length == 0) { GetStats(chatter); return; }
      else if (msg.Equals("ladder")) { GetLadder(); return; }
      else if (msg.Equals("all")) { pointsToRoll = chatter.Gamba.Points; }
      else if (msg.Equals("half")) { pointsToRoll = chatter.Gamba.Points / 2; }
      else if (msg.Equals("quarter")) { pointsToRoll = chatter.Gamba.Points / 4; }
      else
      {
        // try to parse points
        if (int.TryParse(msg, out pointsToRoll))
        {
          if (pointsToRoll <= 0) return; // Provided negative amount of points or a zero
          if (pointsToRoll > chatter.Gamba.Points) return; // Provided more points that the chatter has
        }
        else { return; } // Points amount was not a number?
      }

      if (pointsToRoll == 0) pointsToRoll = 1; // At least one point to roll
      Chat.AddMessageToQueue(string.Concat(
          "@", chatter.Name, " GAMBA is putting ",
          pointsToRoll, " points at risk peepoShake"
        ));

      if (Random.Shared.Next(0, 2) == 1)
      {
        // won
        Chat.AddMessageToQueue(string.Concat(
          "@", chatter.Name, " won ",
          pointsToRoll, " points peepoHappy and have ",
          chatter.Gamba.Points + pointsToRoll, " points peepoHappy"
        ));

        chatter.AddGambaPoints(pointsToRoll);
      }
      else
      {
        // lost
        int newPoints = chatter.Gamba.Points - pointsToRoll;
        Chat.AddMessageToQueue(string.Concat(
          "@", chatter.Name, " lost ",
          pointsToRoll, " points PepeLaugh and have ",
          newPoints < 0 ? 0 : newPoints, " points PepeLaugh",
          newPoints <= 0 ? $" also went bankrupt for {chatter.Gamba.Bankruptcies + 1} time GAMBAADDICT" : ""
        ));
        chatter.AddGambaPoints(-pointsToRoll);
      }
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
      SortedList<int, string> ladder = new(GambaLadderComparer);

      var chatter = Chatter.GetChatters().GetEnumerator();
      Chatter c;
      while (chatter.MoveNext())
      {
        c = chatter.Current.Value;
        if (c.Gamba.Wins != 0 || c.Gamba.Looses != 0)
        {
          ladder.Add(c.Gamba.Points, c.Name);
        }
      }

      if (ladder.Count == 0) return;

      StringBuilder sb = new();
      sb.Append("GAMBA ladder -> ");

      var ladderEntry = ladder.GetEnumerator();
      int index = 0;
      bool first = true;
      while (ladderEntry.MoveNext())
      {
        if (index > MAXLADDERENTRIES) break;
        if (!first) sb.Append(" | ");

        sb.Append(ladderEntry.Current.Value).Append(": ").Append(ladderEntry.Current.Key);

        first = false;
        index++;
      }

      Chat.AddMessageToQueue(sb.ToString());
    }
  }

  public class GambaStats
  {
    public int Points { get; set; }
    public int Wins { get; set; }
    public int Looses { get; set; }
    public int Bankruptcies { get; set; }
  }

  public class GambaLadderComparer : IComparer<int>
  {
    public int Compare(int x, int y) { return y - x; }
  }
}
