using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace AbevBot
{
  public class MinigameRude
  {
    private const int MAXLADDERENTRIES = 10;

    public static void AddRudePoint(string userName, string message, int point = 1)
    {
      Task.Run(() => StartAddRudePoint(userName, message, point));
    }

    private static void StartAddRudePoint(string userName, string message, int point)
    {
      string rudeChatter = message.Trim();
      if (rudeChatter.StartsWith('@')) { rudeChatter = rudeChatter[1..]; }

      if (string.IsNullOrWhiteSpace(rudeChatter))
      {
        GetRudeLadder();
        return;
      }

      Chatter c = Chatter.GetChatterByName(rudeChatter);
      if (c is null)
      {
        Chat.AddMessageToQueue($"@{userName}, couldn't find {rudeChatter} in the chat");
        return;
      }
      else if (userName.ToLower().Equals(c.Name.ToLower()))
      {
        Chat.AddMessageToQueue($"@{userName} you can't be rude to yourself WeirdDude");
        return;
      }

      c.AddRudePoint(point);

      Chat.AddMessageToQueue(string.Concat(
        c.Name, " stop being rude Madge you have been rude ",
        c.RudePoints, " times Madge"
      ));
    }

    private static void GetRudeLadder()
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
