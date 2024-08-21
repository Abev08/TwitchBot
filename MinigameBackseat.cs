using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace AbevBot
{
  public static class MinigameBackseat
  {
    private const int MAXLADDERENTRIES = 10;

    public static void AddBackseatPoint(string userName, int point)
    {
      Task.Run(() => StartAddBackseatPoint(userName, point));
    }

    private static void StartAddBackseatPoint(string userName, int point)
    {
      string chatter = userName?.Trim();
      if (chatter.StartsWith('@')) { chatter = chatter[1..]; }

      if (string.IsNullOrWhiteSpace(chatter))
      {
        GetBackseatLadder();
        return;
      }

      Chatter c = Chatter.GetChatterByName(chatter);
      if (c != null)
      {
        c.AddBackseatPoint(point);

        Chat.AddMessageToQueue(string.Concat(
          "1 point ",
          point > 0 ? "awarded to " : "taken from ",
          c.Name, ". Now has ",
          c.BackseatPoints,
          c.BackseatPoints > 1 ? " points" : " point"
        ));
      }
    }

    private static void GetBackseatLadder()
    {
      List<(int, string)> ladder = new();

      var chatter = Chatter.GetChatters().GetEnumerator();
      Chatter c;
      while (chatter.MoveNext())
      {
        c = chatter.Current.Value;
        if (c.BackseatPoints != 0)
        {
          ladder.Add((c.BackseatPoints, c.Name));
        }
      }

      if (ladder.Count == 0)
      {
        Chat.AddMessageToQueue("Backseat points ladder -> empty... No helpers in chat? Susge");
        return;
      }

      ladder.Sort((a, b) =>
      {
        if (a.Item1 == b.Item1) return b.Item2.CompareTo(a.Item2);
        return b.Item1.CompareTo(a.Item1);
      });

      StringBuilder sb = new();
      sb.Append("Backseat points ladder -> ");

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
      return "!point/unpoint chatter_name";
    }
  }
}
