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
      if (message is null) return;

      string rudeChatter = message.Replace("\U000e0000", "").Trim(); // Removing ShadeEleven's "white space" characters :)
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
      List<(int, string)> ladder = new();

      var chatter = Chatter.GetChatters().GetEnumerator();
      Chatter c;
      while (chatter.MoveNext())
      {
        c = chatter.Current.Value;
        if (c.RudePoints != 0)
        {
          ladder.Add((c.RudePoints, c.Name));
        }
      }

      if (ladder.Count == 0)
      {
        Chat.AddMessageToQueue("Rude points ladder -> empty... Nobody has been rude peepoHappy");
        return;
      }

      ladder.Sort((a, b) =>
      {
        if (a.Item1 == b.Item1) return b.Item2.CompareTo(a.Item2);
        return b.Item1.CompareTo(a.Item1);
      });

      StringBuilder sb = new();
      sb.Append("Rude points ladder -> ");

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
      return "!rude chatter_name";
    }
  }
}
