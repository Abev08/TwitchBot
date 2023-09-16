using System;

namespace AbevBot
{
  public static class MinigameFight
  {
    public static void NewFight(long userID, string userName, string message)
    {

    }
  }

  public class FightStats
  {
    private const int BASEHP = 100;
    private const int BASEXP = 100;
    private const int BASEDMG = 100;
    private const int BASEDODGE = 20; // 20% to dodge
    private const int BASECRIT = 5; // 5% to crit (200% dmg)

    public int Level { get; set; }
    public int CurrentXp { get; set; }
    public int CurrentHp { get; set; }
    public int Wins { get; set; }
    public int Looses { get; set; }
    public int Draws { get; set; }
    public DateTime LastFight { get; set; }

    public void Init()
    {
      Level = 1;
    }
  }
}
