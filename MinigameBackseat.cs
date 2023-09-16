using System.Collections.Generic;
using System.Text;

namespace AbevBot
{
  public static class MinigameBackseat
  {
    private const int MAXLADDERENTRIES = 10;

    public static void AddBackseatPoint(string userName, int point)
    {
      if (string.IsNullOrWhiteSpace(userName)) { GetBackseatLadder(); }
      else
      {
        Chatter c = Chatter.GetChatterByName(userName);
        if (c != null)
        {
          c.AddBackseatPoint(point);

          Chat.AddMessageToQueue(string.Concat(
            "1 point ",
            point > 0 ? "awarded to " : "taken from ",
            userName, ". Now has ", c.BackseatPoints, " points"
          ));
        }
      }
    }

    public static void GetBackseatLadder()
    {
      SortedList<int, string> ladder = new(MinigameGamba.GambaLadderComparer);

      var chatter = Chatter.GetChatters().GetEnumerator();
      Chatter c;
      while (chatter.MoveNext())
      {
        c = chatter.Current.Value;
        if (c.BackseatPoints != 0)
        {
          ladder.Add(c.BackseatPoints, c.Name);
        }
      }

      if (ladder.Count == 0) return;

      StringBuilder sb = new();
      sb.Append("Backseat points ladder -> ");

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
}
