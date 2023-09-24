using System.Collections.Generic;
using System.Text;

namespace AbevBot
{
  public static class TikTok
  {
    // Dictionary of <key, value>
    // key: TikTok internal voice name
    // value: Voice name used on stream and tts messages
    private static readonly Dictionary<string, string> Voices = new()
    {
      // English
      {"en_uk_001", "Chris"},
      {"en_uk_003", "UKMale"},
      {"en_female_emotional", "Peaceful"},
      {"en_au_001", "Eddie"},
      {"en_au_002", "Alex"},
      {"en_us_002", "Jessie"},
      {"en_us_006", "Joey"},
      {"en_us_007", "Professor"},
      {"en_us_009", "Scientist"},
      {"en_us_010", "Confidence"},
      {"en_female_samc", "Empathetic"},
      {"en_male_cody", "Serious"},
      {"en_male_narration", "StoryTeller"},
      {"en_male_funny", "Wacky"},
      {"en_male_jarvis", "Alfred"},
      {"en_male_santa_narration", "Author"},
      {"en_female_betty", "Bae"},
      {"en_female_makeup", "BeautyGuru"},
      {"en_female_richgirl", "Bestie"},
      {"en_male_cupid", "Cupid"},
      {"en_female_shenna", "Debutante"},
      {"en_male_ghosthost", "GhostHost"},
      {"en_female_grandma", "Grandma"},
      {"en_male_ukneighbor", "LordCringe"},
      {"en_male_wizard", "Magician"},
      {"en_male_trevor", "Marty"},
      {"en_male_deadpool", "Deadpool"},
      {"en_male_ukbutler", "Mr.Meticulous"},
      {"en_male_petercullen", "OptimusPrime"},
      {"en_male_pirate", "Pirate"},
      {"en_male_santa", "Santa"},
      {"en_male_santa_effect", "Santa"},
      {"en_female_pansino", "Varsity"},
      {"en_male_grinch", "Trickster"},
      {"en_us_ghostface", "Ghostface"},
      {"en_us_chewbacca", "Chewbacca"},
      {"en_us_c3po", "C-3PO"},
      {"en_us_stormtrooper", "Stormtrooper"},
      {"en_us_stitch", "Stitch"},
      {"en_us_rocket", "Rocket"},
      {"en_female_madam_leota", "MadameLeota"},
      {"en_male_sing_deep_jingle", "SongCaroler"},
      {"en_male_m03_classical", "SongClassicElectric"},
      {"en_female_f08_salut_damour", "SongCottagecore"},
      {"en_male_m2_xhxs_m03_christmas", "SongCozy"},
      {"en_female_f08_warmy_breeze", "SongOpenMic"},
      {"en_female_ht_f08_halloween", "SongOpera"},
      {"en_female_ht_f08_glorious", "SongEuphoric"},
      {"en_male_sing_funny_it_goes_up", "SongHypetrain"},
      {"en_male_m03_lobby", "SongJingle"},
      {"en_female_ht_f08_wonderful_world", "SongMelodrama"},
      {"en_female_ht_f08_newyear", "SongNYE"},
      {"en_male_sing_funny_thanksgiving", "SongThanksgiving"},
      {"en_male_m03_sunshine_soon", "SongToonBeat"},
      {"en_female_f08_twinkle", "SongPopLullaby"},
      {"en_male_m2_xhxs_m03_silly", "SongQuirkyTime"},

      // French
      {"fr_001", "FrenchMale1"},
      {"fr_002", "FrenchMale2"},

      // German
      {"de_001", "GermanFemale"},
      {"de_002", "GermanMale"},

      // Indonesian
      {"id_male_darma", "Darma"},
      {"id_female_icha", "Icha"},
      {"id_female_noor", "Noor"},
      {"id_male_putra", "Putra"},

      // Italian
      {"it_male_m18", "ItalianMale"},

      // Japanese
      {"jp_001", "Miho"},
      {"jp_003", "Keiko"},
      {"jp_005", "Sakura"},
      {"jp_006", "Naoki"},
      {"jp_male_osada", "Morisuke"},
      {"jp_male_matsuo", "Matsuo"},
      {"jp_female_machikoriiita", "Machikoriiita"},
      {"jp_male_matsudake", "Matsudake"},
      {"jp_male_shuichiro", "Shuichiro"},
      {"jp_female_rei", "Maruyama Rei"},
      {"jp_male_hikakin", "Hikakin"},
      {"jp_female_yagishaki", "Yagi Saki"},

      // Korean
      {"kr_002", "KoreanMale1"},
      {"kr_004", "KoreanMale2"},
      {"kr_003", "KoreanFemale"},

      // Portuguese
      {"br_003", "Júlia"},
      {"br_004", "Ana"},
      {"br_005", "Lucas"},
      {"pt_female_lhays", "LhaysMacedo"},
      {"pt_female_laizza", "Laizza"},
      {"pt_male_transformer", "OptimusPrimePt"},

      // Spanish
      {"es_002", "SpanishMale"},
      {"es_male_m3", "Julio"},
      {"es_female_f6", "Alejandra"},
      {"es_female_fp1", "Mariana"},
      {"es_mx_002", "Álex"},
      {"es_mx_male_transformer", "OptimusPrimeMx"},
      {"es_mx_female_supermom", "SuperMamá"}
    };

    public static string GetVoice(string name)
    {
      if (name is null || name.Length == 0) return string.Empty;

      if (Config.Data[Config.Keys.TikTokSessionID].Length == 0) return string.Empty;

      string voiceName = name.ToLower();
      var voice = Voices.GetEnumerator();
      while (voice.MoveNext())
      {
        if (voice.Current.Value.ToLower().Equals(voiceName))
        {
          return voice.Current.Key;
        }
      }

      return string.Empty;
    }

    public static string GetVoices()
    {
      StringBuilder sb = new();
      var voice = Voices.GetEnumerator();
      bool first = true;
      while (voice.MoveNext())
      {
        if (!first) sb.Append(", ");
        sb.Append(voice.Current.Value);
        first = false;
      }

      return sb.ToString();
    }
  }
}
