using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace AbevBot
{
  public static class MinigameFight
  {
    private const int MAXLADDERENTRIES = 10;
    private static readonly TimeSpan FIGHTINGTIMEOUT = new(0, 10, 0);
    public static bool Enabled { get; set; }

    public static void NewFight(long userID, string userName, string message)
    {
      if (!Enabled) return;

      Chatter fighter1 = Chatter.GetChatterByID(userID, userName);

      if (string.IsNullOrEmpty(message)) { GetStats(fighter1); return; }
      else if (message.Trim().ToLower().Equals("ladder")) { GetLadder(); return; }

      Chatter fighter2 = Chatter.GetChatterByName(message);

      if (fighter1 is null || fighter2 is null) return;
      if (fighter1.Name.Equals(fighter2.Name)) return; // FIXME: ID comparasion would be better, for now lets leave it like this
      if (InitFighters(fighter1, fighter2))
      {
        return; // One of the fighters can't fight
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
        fighter1.AddFightExp((int)(fighter2.Fight.Level * 0.2f));
        fighter2.AddFightExp((int)(f1level * 0.2f));
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
        fighter2.Fight.CheckStats();
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
        fighter1.Fight.CheckStats();
        fighter2.AddFightExp(fighter1.Fight.Level);
        msg = string.Concat(
          fighter2.Name, " won with ", hp, " hp left! The fight took ", rounds, " rounds",
          level != fighter2.Fight.Level ? $". {fighter2.Name} leveled up to level {fighter2.Fight.Level}" : ""
        );
      }

      Chat.AddMessageToQueue(msg);
    }

    private static bool InitFighters(Chatter fighter1, Chatter fighter2)
    {
      if (fighter1.Fight is null)
      {
        fighter1.Fight = new();
        fighter1.Fight.Init();
      }
      else
      {
        if (DateTime.Now - fighter1.Fight.LastFight < FIGHTINGTIMEOUT) return true;
        fighter1.Fight.CheckStats();
      }
      if (fighter2.Fight is null)
      {
        fighter2.Fight = new();
        fighter2.Fight.Init();
      }
      else
      {
        if (DateTime.Now - fighter2.Fight.LastFight < FIGHTINGTIMEOUT) return true;
        fighter2.Fight.CheckStats();
      }

      return false;
    }

    private static void GetStats(Chatter fighter)
    {
      fighter.Fight.CheckStats();
      Chat.AddMessageToQueue(string.Concat(
        "@", fighter.Name,
        " LVL: ", fighter.Fight.Level,
        ", REQ. EXP: ", fighter.Fight.RequiredExp - fighter.Fight.CurrentExp,
        ", HP: ", fighter.Fight.CurrentHp,
        ", DMG: ", fighter.Fight.DmgMin, "-", fighter.Fight.DmgMax,
        ", Wins: ", fighter.Fight.Wins,
        ", Looses: ", fighter.Fight.Looses,
        ", Draws: ", fighter.Fight.Draws
      ));
    }

    private static void GetLadder()
    {
      SortedList<int, string> ladder = new(MinigameGamba.GambaLadderComparer);

      var chatter = Chatter.GetChatters().GetEnumerator();
      Chatter c;
      while (chatter.MoveNext())
      {
        c = chatter.Current.Value;
        if (c.Fight is null) continue;
        if (c.Fight.Wins != 0 || c.Fight.Looses != 0 || c.Fight.Draws != 0)
        {
          ladder.Add(c.Fight.Level, c.Name);
        }
      }

      if (ladder.Count == 0) return;

      StringBuilder sb = new();
      sb.Append("peepoBox ladder -> ");

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

  public class FightStats
  {
    private const int BASEDMG = 40;
    public const int BASEDODGE = 10; // 10% to dodge
    public const int BASECRIT = 5; // 5% to crit (200% dmg)

    public int Level { get; set; }
    public int CurrentExp { get; set; }
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

    public void Init()
    {
      Level = 1;
      CheckStats();
    }

    public void CheckStats(bool leveledUp = false)
    {
      if (leveledUp || DmgMin == 0 || DmgMax == 0)
      {
        DmgMin = BASEDMG + (5 * Level);
        DmgMax = BASEDMG + (15 * Level);
      }
      if (leveledUp || RequiredExp == 0)
      {
        RequiredExp = (int)MathF.Floor(0.2f * Level * Level + 3f * Level); // 0,2 * Level^2 + 3 * Level
      }
      if (leveledUp || CurrentHp <= 0)
      {
        int maxHp = (int)((4f * Level * Level) + (60f * Level) + 180f); // 4x^2 + 60x + 180
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