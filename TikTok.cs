using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
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
      {"en_male_cody", "Cody"},
      {"en_male_narration", "Old"},
      {"en_male_funny", "Funny"},
      {"en_male_jarvis", "Jarvis"},
      {"en_male_santa_narration", "Santa"},
      {"en_female_betty", "Betty"},
      {"en_female_makeup", "Makeup"},
      {"en_female_richgirl", "Richgirl"},
      {"en_male_cupid", "Cupid"},
      {"en_female_shenna", "Shenna"},
      {"en_male_ghosthost", "GhostHost"},
      {"en_female_grandma", "Grandma"},
      {"en_male_ukneighbor", "LordCringe"},
      {"en_male_wizard", "Wizard"},
      {"en_male_trevor", "Trevor"},
      {"en_male_deadpool", "Deadpool"},
      {"en_male_ukbutler", "Butler"},
      {"en_male_petercullen", "Peter"},
      {"en_male_pirate", "Pirate"},
      {"en_male_santa", "Santa"},
      {"en_male_santa_effect", "SantaEffect"},
      {"en_female_pansino", "Pansino"},
      {"en_male_grinch", "Grinch"},
      {"en_us_ghostface", "Ghost"},
      {"en_us_chewbacca", "Chewbacca"},
      {"en_us_c3po", "C3PO"},
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

    public static Stream GetTTS(string _text, string voice, float soundVolume = 1f)
    {
      if (Config.Data[Config.Keys.TikTokSessionID].Length == 0) return null;
      if (_text is null || _text.Length == 0) return null;

      string text = _text;
      text = text.Replace("+", "plus");
      text = text.Replace(" ", "+");
      text = text.Replace("&", "and");

      string url = $"https://api16-normal-v6.tiktokv.com/media/api/text/speech/invoke/?text_speaker={voice}&req_text={text}&speaker_map_type=0&aid=1233";

      TikTokTTSResponse result;
      using HttpRequestMessage request = new(HttpMethod.Post, url);
      request.Headers.Add("User-Agent", "com.zhiliaoapp.musically/2022600030 (Linux; U; Android 7.1.2; es_ES; SM-G988N; Build/NRD90M;tt-ok/3.12.13.1)");
      request.Headers.Add("Cookie", $"sessionid={Config.Data[Config.Keys.TikTokSessionID]}");

      result = TikTokTTSResponse.Deserialize(Notifications.Client.Send(request).Content.ReadAsStringAsync().Result);
      if (result?.StatusCode != 0)
      {
        MainWindow.ConsoleWarning($">> TikTok TTS request status: {result?.StatusCode}, error: {result?.StatusMessage}");
        return null;
      }
      else if (result?.Data?.Duration?.Length is null || string.IsNullOrEmpty(result?.Data?.VStr))
      {
        MainWindow.ConsoleWarning(">> TikTok TTS request returned sound with length 0.");
        return null;
      }

      return new MemoryStream(Convert.FromBase64String(result.Data.VStr));
    }
  }
}
