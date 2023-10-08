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
      {"en_uk_001", "uk1"},
      {"en_uk_003", "uk3"},
      {"en_au_001", "au1"},
      {"en_au_002", "au2"},
      {"en_us_001", "us1"},
      {"en_us_002", "us2"},
      {"en_us_006", "us6"},
      {"en_us_007", "us7"},
      {"en_us_009", "us9"},
      {"en_us_010", "us10"},
      {"en_female_emotional", "Emotional"},
      {"en_female_samc", "Empathetic"},
      {"en_male_cody", "Serious"},
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
      {"en_male_ukneighbor", "ukneighbor"},
      {"en_male_wizard", "Wizard"},
      {"en_male_trevor", "Trevor"},
      {"en_male_deadpool", "Deadpool"},
      {"en_male_ukbutler", "ukbutler"},
      {"en_male_petercullen", "Petercullen"},
      {"en_male_pirate", "Pirate"},
      {"en_male_santa", "Santa2"},
      {"en_male_santa_effect", "Santa3"},
      {"en_female_pansino", "Pansino"},
      {"en_male_grinch", "Trickster"},
      {"en_us_ghostface", "Ghost"},
      {"en_us_chewbacca", "Chewbacca"},
      {"en_us_c3po", "C3PO"},
      {"en_us_stormtrooper", "Storm"},
      {"en_us_stitch", "Stitch"},
      {"en_us_rocket", "Rocket"},
      {"en_female_madam_leota", "Leota"},
      {"en_female_ht_f08_halloween", "Opera"},
      {"en_male_m03_lobby", "Sing"},
      {"en_female_f08_salut_damour", "Sing2"},
      {"en_female_f08_warmy_breeze", "Sing3"},
      {"en_male_m03_sunshine_soon", "Sing4"},
      {"en_female_ht_f08_glorious", "Sing5"},
      {"en_male_sing_funny_it_goes_up", "Sing6"},
      {"en_male_m2_xhxs_m03_silly", "Sing7"},
      {"en_female_ht_f08_wonderful_world", "Sing8"},
      {"en_male_sing_deep_jingle", "Sing9"},
      {"en_male_m03_classical", "Sing10"},
      {"en_male_m2_xhxs_m03_christmas", "Sing11"},
      {"en_female_ht_f08_newyear", "Sing12"},
      {"en_male_sing_funny_thanksgiving", "Sing13"},
      {"en_female_f08_twinkle", "Sing14"},

      // French
      {"fr_001", "fr1"},
      {"fr_002", "fr2"},

      // German
      {"de_001", "de1"},
      {"de_002", "de2"},

      // Indonesian
      {"id_male_darma", "Darma"},
      {"id_female_icha", "Icha"},
      {"id_female_noor", "Noor"},
      {"id_male_putra", "Putra"},

      // Italian
      {"it_male_m18", "itm18"},

      // Japanese
      {"jp_001", "jp1"},
      {"jp_003", "jp3"},
      {"jp_005", "jp5"},
      {"jp_006", "jp6"},
      {"jp_male_osada", "Osada"},
      {"jp_male_matsuo", "Matsuo"},
      {"jp_female_machikoriiita", "Machikoriiita"},
      {"jp_male_matsudake", "Matsudake"},
      {"jp_male_shuichiro", "Shuichiro"},
      {"jp_female_rei", "Rei"},
      {"jp_male_hikakin", "Hikakin"},
      {"jp_female_yagishaki", "YagiShaki"},

      // Korean
      {"kr_002", "kr2"},
      {"kr_003", "kr3"},
      {"kr_004", "kr4"},

      // Portuguese
      {"br_001", "br1"},
      {"br_003", "br3"},
      {"br_004", "br4"},
      {"br_005", "br5"},
      {"pt_female_lhays", "Lhays"},
      {"pt_female_laizza", "Laizza"},
      {"pt_male_transformer", "OptimusPrimePt"},

      // Spanish
      {"es_002", "es2"},
      {"es_male_m3", "esm3"},
      {"es_female_f6", "esf6"},
      {"es_female_fp1", "esfp1"},
      {"es_mx_002", "esm2"},
      {"es_mx_male_transformer", "OptimusPrimeMx"},
      {"es_mx_female_supermom", "SuperMom"},

      {"id_001", "id1"}
    };

    public static string GetVoice(string name)
    {
      if (name is null || name.Length == 0) return string.Empty;

      if (Secret.Data[Secret.Keys.TikTokSessionID].Length == 0) return string.Empty;

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

    public static Stream GetTTS(string _text, string voice)
    {
      if (Secret.Data[Secret.Keys.TikTokSessionID].Length == 0) return null;
      if (_text is null || _text.Length == 0) return null;

      string text = _text;
      text = text.Replace("+", "plus");
      text = text.Replace(" ", "+");
      text = text.Replace("&", "and");

      string url = $"https://api16-normal-v6.tiktokv.com/media/api/text/speech/invoke/?text_speaker={voice}&req_text={text}&speaker_map_type=0&aid=1233";

      TikTokTTSResponse result;
      using HttpRequestMessage request = new(HttpMethod.Post, url);
      request.Headers.Add("User-Agent", "com.zhiliaoapp.musically/2022600030 (Linux; U; Android 7.1.2; es_ES; SM-G988N; Build/NRD90M;tt-ok/3.12.13.1)");
      request.Headers.Add("Cookie", $"sessionid={Secret.Data[Secret.Keys.TikTokSessionID]}");

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
