using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace AbevBot
{
  public static class MinigameFight
  {
    private const int MAXLADDERENTRIES = 10;
    private static readonly TimeSpan FIGHTINGTIMEOUT = new(0, 5, 0);

    public static bool Enabled { get; set; }

    public static void NewFight(long userID, string userName, string message)
    {
      if (!Enabled)
      {
        Chat.AddMessageToQueue($"@{userName} !fight disabled peepoSad");
        return;
      }

      Task.Run(() => StartNewFight(userID, userName, message));
    }

    private static void StartNewFight(long userID, string userName, string message)
    {
      Chatter fighter1 = Chatter.GetChatterByID(userID, userName);

      string chatter = message.Replace("\U000e0000", "").Trim(); // Removing ShadeEleven's "white space" characters :)
      if (chatter.StartsWith('@')) { chatter = chatter[1..]; }

      if (string.IsNullOrEmpty(chatter))
      {
        GetStats(fighter1);
        return;
      }
      else if (chatter.ToLower().Equals("ladder"))
      {
        GetLadder();
        return;
      }
      else if (chatter.ToLower().Equals("help"))
      {
        Chat.AddMessageToQueue(string.Concat(
         "peepoBox minigame. ",
         "\" !fight <empty/chatter_name/ladder> \". ",
         "empty - your stats, ",
         "chatter_name - fighting others, ",
         "ladder - the ladder"
        ));
        return;
      }

      Chatter fighter2 = Chatter.GetChatterByName(chatter);

      if (fighter1 is null || fighter2 is null)
      {
        Chat.AddMessageToQueue($"@{fighter1.Name}, couldn't find {chatter} in the chat");
        return;
      }
      if (fighter1.ID == fighter2.ID)
      {
        Chat.AddMessageToQueue($"{fighter1.Name} you can't fight yourself WeirdDude");
        return;
      }
      if (DateTime.Now - fighter2.LastChatted >= Chatter.OfflineTimeout)
      {
        // Check if chatter is present in the chat
        if (!Chat.CheckIfChatterIsInChat(fighter2.Name))
        {
          Chat.AddMessageToQueue($"@{fighter1.Name}, couldn't find {chatter} in the chat");
          return;
        }
        fighter2.LastChatted = DateTime.Now; // Still in the chat, pretend that he chatted
      }
      if (InitFighter(fighter1))
      {
        TimeSpan restTimer = FIGHTINGTIMEOUT - (DateTime.Now - fighter1.Fight.LastFight);

        Chat.AddMessageToQueue(string.Concat(
          "@", fighter1.Name, " you are exhausted, you need to rest for another ",
          restTimer.TotalSeconds < 60 ?
            $"{Math.Ceiling(restTimer.TotalSeconds)} seconds" :
            $"{Math.Ceiling(restTimer.TotalMinutes)} minutes"
        ));
        return; // The fighter can't fight
      }
      if (InitFighter(fighter2))
      {
        TimeSpan restTimer = FIGHTINGTIMEOUT - (DateTime.Now - fighter2.Fight.LastFight);

        Chat.AddMessageToQueue(string.Concat(
          fighter2.Name, " is exhausted, needs to rest for another ",
          restTimer.TotalSeconds < 60 ?
            $"{Math.Ceiling(restTimer.TotalSeconds)} seconds" :
            $"{Math.Ceiling(restTimer.TotalMinutes)} minutes"
        ));
        return; // The fighter can't fight
      }

      // Fight
      Chat.AddMessageToQueue(string.Concat(fighter1.Name, " is fighting ", fighter2.Name, "... peepoBox"));
      fighter1.Fight.LastFight = fighter2.Fight.LastFight = DateTime.Now;
      int rounds = 0, dmg;
      while (fighter1.Fight.CurrentHp > 0 && fighter2.Fight.CurrentHp > 0)
      {
        rounds++;
        dmg = fighter1.Fight.Dmg;
        if (Random.Shared.Next(0, 101) <= FightStats.BASEDODGE) dmg = 0; // Dodge
        else if (Random.Shared.Next(0, 101) <= FightStats.BASECRIT) dmg *= 2; // Critical hit
        fighter2.Fight.CurrentHp -= dmg;

        dmg = fighter2.Fight.Dmg;
        if (Random.Shared.Next(0, 101) <= FightStats.BASEDODGE) dmg = 0; // Dodge
        else if (Random.Shared.Next(0, 101) <= FightStats.BASECRIT) dmg *= 2; // Critical hit
        fighter1.Fight.CurrentHp -= dmg;
      }

      // End
      string msg = string.Empty;
      if (fighter1.Fight.CurrentHp <= 0 && fighter2.Fight.CurrentHp <= 0)
      {
        // Draw, both of the fighter gain 20% exp
        int f1level = fighter1.Fight.Level;
        int f2level = fighter2.Fight.Level;
        fighter1.Fight.Draws++;
        fighter2.Fight.Draws++;
        fighter1.AddFightExp(fighter2.Fight.Level * 0.2f);
        fighter2.AddFightExp(f1level * 0.2f);
        msg = string.Concat(
          "It was a draw! The fight took ", rounds, " rounds",
          f1level != fighter1.Fight.Level ? $". {fighter1.Name} leveled up to level {fighter1.Fight.Level}" : "",
          f2level != fighter2.Fight.Level ? $". {fighter2.Name} leveled up to level {fighter2.Fight.Level}" : ""
        );
      }
      else if (fighter2.Fight.CurrentHp <= 0)
      {
        // Fighter 1 won
        int level = fighter1.Fight.Level;
        int hp = fighter1.Fight.CurrentHp;
        fighter1.Fight.Wins++;
        fighter2.Fight.Looses++;
        fighter2.Fight.CheckStats(fighter2.Name);
        fighter1.AddFightExp(fighter2.Fight.Level);
        msg = string.Concat(
          fighter1.Name, " won with ", hp, " hp left! The fight took ", rounds, " rounds",
          level != fighter1.Fight.Level ? $". {fighter1.Name} leveled up to level {fighter1.Fight.Level}" : ""
        );
      }
      else if (fighter1.Fight.CurrentHp <= 0)
      {
        // Fighter 2 won
        int level = fighter2.Fight.Level;
        int hp = fighter2.Fight.CurrentHp;
        fighter1.Fight.Looses++;
        fighter2.Fight.Wins++;
        fighter1.Fight.CheckStats(fighter1.Name);
        fighter2.AddFightExp(fighter1.Fight.Level);
        msg = string.Concat(
          fighter2.Name, " won with ", hp, " hp left! The fight took ", rounds, " rounds",
          level != fighter2.Fight.Level ? $". {fighter2.Name} leveled up to level {fighter2.Fight.Level}" : ""
        );
      }

      Task.Delay(2000).Wait(); // Add some time to keep them waiting
      Chat.AddMessageToQueue(msg);
    }

    private static bool InitFighter(Chatter fighter)
    {
      if (fighter.Fight is null)
      {
        fighter.Fight = new();
        fighter.Fight.Init(fighter.Name);
      }
      else
      {
        fighter.Fight.CheckStats(fighter.Name);
        if (DateTime.Now - fighter.Fight.LastFight < FIGHTINGTIMEOUT) return true;
      }
      return false;
    }

    private static void GetStats(Chatter fighter)
    {
      InitFighter(fighter);
      Chat.AddMessageToQueue(string.Concat(
        "@", fighter.Name,
        " LVL: ", fighter.Fight.Level,
        ", REQ. EXP: ", MathF.Round(fighter.Fight.RequiredExp - fighter.Fight.CurrentExp, 2),
        ", HP: ", fighter.Fight.CurrentHp,
        ", DMG: ", fighter.Fight.DmgMin, "-", fighter.Fight.DmgMax,
        ", Wins: ", fighter.Fight.Wins,
        ", Looses: ", fighter.Fight.Looses,
        ", Draws: ", fighter.Fight.Draws
      ));
    }

    private static void GetLadder()
    {
      List<(int level, string name)> ladder = new();

      var chatter = Chatter.GetChatters().GetEnumerator();
      Chatter c;
      while (chatter.MoveNext())
      {
        c = chatter.Current.Value;
        if (c.Fight is null) continue;
        if (c.Fight.Wins != 0 || c.Fight.Looses != 0 || c.Fight.Draws != 0)
        {
          ladder.Add((c.Fight.Level, c.Name));
        }
      }

      if (ladder.Count == 0)
      {
        Chat.AddMessageToQueue("peepoBox the ladder is empty Sadge");
        return;
      }

      // Sort the ladder
      ladder.Sort((a, b) => { return b.level - a.level; });

      StringBuilder sb = new();
      sb.Append("peepoBox ladder -> ");

      var ladderEntry = ladder.GetEnumerator();
      int index = 0;
      bool first = true;
      while (ladderEntry.MoveNext())
      {
        if (index >= MAXLADDERENTRIES) break;
        if (!first) sb.Append(" | ");

        sb.Append(ladderEntry.Current.name).Append(": ").Append(ladderEntry.Current.level);

        first = false;
        index++;
      }

      Chat.AddMessageToQueue(sb.ToString());
    }

    public static string GetCommands()
    {
      if (!Enabled) return string.Empty;

      return "!fight <empty/chatter_name/ladder/help>";
    }
  }

  public class FightStats
  {
    private const float OVERPOWEREDMULTI = 1.1f; // :)
    private const int BASEDMG = 40;
    public const int BASEDODGE = 10; // 10% to dodge
    public const int BASECRIT = 5; // 5% to crit (200% dmg)

    public int Level { get; set; }
    public float CurrentExp { get; set; }
    public int CurrentHp { get; set; }
    public int DmgMin;
    public int DmgMax;
    [JsonIgnore]
    public int Dmg => Random.Shared.Next(DmgMin, DmgMax + 1);
    public int RequiredExp;
    public int Wins { get; set; }
    public int Looses { get; set; }
    public int Draws { get; set; }
    public DateTime LastFight { get; set; }

    public void Init(string name)
    {
      Level = 1;
      CheckStats(name);
    }

    public void CheckStats(string name, bool leveledUp = false)
    {
      bool overpowered = Chatter.OverpoweredInFight.Contains(name);

      if (leveledUp || DmgMin == 0 || DmgMax == 0)
      {
        DmgMin = BASEDMG + (5 * Level);
        DmgMax = BASEDMG + (15 * Level);
        if (overpowered)
        {
          DmgMin = (int)(DmgMin * OVERPOWEREDMULTI);
          DmgMax = (int)(DmgMax * OVERPOWEREDMULTI);
        }
      }
      if (leveledUp || RequiredExp == 0)
      {
        RequiredExp = (int)MathF.Floor(0.2f * Level * Level + 3f * Level); // 0,2 * Level^2 + 3 * Level
      }
      if (leveledUp || CurrentHp <= 0)
      {
        int maxHp = (int)((4f * Level * Level) + (60f * Level) + 180f); // 4x^2 + 60x + 180
        if (overpowered) maxHp = (int)(maxHp * OVERPOWEREDMULTI);

        if (CurrentHp <= 0) { CurrentHp = maxHp; }
        else
        {
          // Calculate current hp percentage and convert it to next lvl hp percentage
          int maxHpPrev = (int)((4f * (Level - 1) * (Level - 1)) + (60f * (Level - 1)) + 180f);
          float hpPercent = (float)CurrentHp / (float)maxHpPrev;
          CurrentHp = (int)(maxHp * hpPercent);
        }
      }
    }
  }
}
