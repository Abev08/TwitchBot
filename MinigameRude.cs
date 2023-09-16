using System.Collections.Generic;
using System.Text;

namespace AbevBot
{
  public class MinigameRude
  {
    private const int MAXLADDERENTRIES = 10;

    public static void AddRudePoint(string userName, int point = 1)
    {
      if (string.IsNullOrWhiteSpace(userName)) { GetRudeLadder(); }
      else
      {
        Chatter c = Chatter.GetChatterByName(userName);
        if (c != null)
        {
          c.AddRudePoint(point);

          Chat.AddMessageToQueue(string.Concat(
            c.Name, " stop being rude Madge you have been rude ",
            c.RudePoints, " times Madge"
          ));
        }
      }
    }

    public static void GetRudeLadder()
    {
      SortedList<int, string> ladder = new(MinigameGamba.GambaLadderComparer);

      var chatter = Chatter.GetChatters().GetEnumerator();
      Chatter c;
      while (chatter.MoveNext())
      {
        c = chatter.Current.Value;
        if (c.RudePoints != 0)
        {
          ladder.Add(c.RudePoints, c.Name);
        }
      }

      if (ladder.Count == 0) return;

      StringBuilder sb = new();
      sb.Append("Rude points ladder -> ");

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
