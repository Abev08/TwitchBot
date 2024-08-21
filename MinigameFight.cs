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
      var sb = new StringBuilder();
      sb.Append(fighter1.Name).Append(" (").Append(fighter1.Fight.Level).Append(')');
      sb.Append(" is fighting ");
      sb.Append(fighter2.Name).Append(" (").Append(fighter2.Fight.Level).Append(')');
      sb.Append("... peepoBox ");
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
      sb.Append("After some time the arena went quiet. The fight lasted ");
      sb.Append(rounds).Append(rounds > 1 ? " rounds. " : " round. ");

      // End
      int f1level = fighter1.Fight.Level;
      int f2level = fighter2.Fight.Level;
      int hpLeft = -1;
      float percentHpLeft = -1;
      if (fighter1.Fight.CurrentHp <= 0 && fighter2.Fight.CurrentHp <= 0)
      {
        // Draw, both of the fighter gain 20% exp
        fighter1.Fight.Draws++;
        fighter2.Fight.Draws++;
        fighter1.AddFightExp(fighter2.Fight.Level * 0.2f);
        fighter2.AddFightExp(f1level * 0.2f);

        sb.Append("Nobody is standing on their own, it was a DRAW Deadge");
      }
      else if (fighter2.Fight.CurrentHp <= 0)
      {
        // Fighter 1 won
        hpLeft = fighter1.Fight.CurrentHp;
        percentHpLeft = (float)hpLeft / (float)fighter1.Fight.GetMaxHp();
        fighter1.Fight.Wins++;
        fighter2.Fight.Looses++;
        fighter2.Fight.CheckStats(fighter2.Name);
        fighter1.AddFightExp(fighter2.Fight.Level);
        sb.Append("Loud battle cires echo through the arena as the winner ");
        sb.Append(fighter1.Name).Append(" shouts them with all his might peepoEvil");
      }
      else if (fighter1.Fight.CurrentHp <= 0)
      {
        // Fighter 2 won
        hpLeft = fighter2.Fight.CurrentHp;
        percentHpLeft = (float)hpLeft / (float)fighter1.Fight.GetMaxHp();
        fighter1.Fight.Looses++;
        fighter2.Fight.Wins++;
        fighter1.Fight.CheckStats(fighter1.Name);
        fighter2.AddFightExp(fighter1.Fight.Level);
        sb.Append(fighter2.Name).Append("'s terrifying laughter can be heard PepeLaugh as he stands victorious");
      }

      if (percentHpLeft > 0)
      {
        if (percentHpLeft <= 0.2)
        {
          // Less than 20% hp left
          sb.Append(" The winner can barely stand on his own with only ").Append(hpLeft).Append(" hp left!");
        }
        else if (percentHpLeft <= 0.6)
        {
          // Less than 60% hp left
          sb.Append(" The winner has a lot of battle marks on his body, the fight left him with ").Append(hpLeft).Append(" hp left!");
        }
        else
        {
          // Healthy
          sb.Append(" The winner jumping around the arena seems to be very healthy with ").Append(hpLeft).Append(" hp left!");
        }
      }

      // Add lvl up info
      if (f1level != fighter1.Fight.Level) { sb.Append(' ').Append(fighter1.Name).Append(" leveled up to level ").Append(fighter1.Fight.Level).Append('.'); }
      if (f2level != fighter2.Fight.Level) { sb.Append(' ').Append(fighter2.Name).Append(" leveled up to level ").Append(fighter2.Fight.Level).Append('.'); }

      Chat.AddMessageToQueue(sb.ToString());
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
        ", HP: ", fighter.Fight.CurrentHp, " (", fighter.Fight.GetMaxHp(), ")",
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
      int maxHp = GetMaxHp();
      if (leveledUp || CurrentHp <= 0)
      {
        if (overpowered) maxHp = (int)(maxHp * OVERPOWEREDMULTI);

        if (CurrentHp <= 0) { CurrentHp = maxHp; }
        else
        {
          // Calculate current hp percentage and convert it to next lvl hp percentage
          int maxHpPrev = GetMaxHp(Level - 1);
          float hpPercent = (float)CurrentHp / (float)maxHpPrev;
          CurrentHp = (int)(maxHp * hpPercent);
        }
      }
      if (CurrentHp > maxHp) { CurrentHp = maxHp; } // Just a sanity check
    }

    /// <summary> Returns maximum hp for current (or when provided the specified) level. </summary>
    public int GetMaxHp(int level = -1)
    {
      int l = level >= 0 ? level : Level;
      return (int)((4f * l * l) + (60f * l) + 180f);// 4x^2 + 60x + 180
    }
  }
}
