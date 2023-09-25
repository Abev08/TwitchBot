using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using NAudio.Wave;

namespace AbevBot
{
  public static class StreamElements
  {
    // Dictionary of <key, value>
    // key: StreamElements internal voice name
    // value: Voice name used on stream and tts messages
    private static readonly Dictionary<string, string> Voices = new()
    {
      // Turkish
      {"Filiz", "Filiz"},

      // Swedish
      {"Astrid", "Astrid"},

      // Russian
      {"Tatyana", "Tatyana"},
      {"Maxim", "Maxim"},

      // Romanian
      {"Carmen", "Carmen"},

      // Portuguese
      {"Ines", "Ines"},
      {"Cristiano", "Cristiano"},
      {"Vitoria", "Vitoria"},
      {"Ricardo", "Ricardo"},

      // Polish
      {"Maja", "Maja"},
      {"Jan", "Jan"},
      {"Jacek", "Jacek"},
      {"Ewa", "Ewa"},

      // Dutch
      {"Ruben", "Ruben"},
      {"Lotte", "Lotte"},

      // Norwegian
      {"Liv", "Liv"},

      // Korean
      {"Seoyeon", "Seoyeon"},

      // Japanese
      {"Takumi", "Takumi"},
      {"Mizuki", "Mizuki"},

      // Italian
      {"Giorgio", "Giorgio"},
      {"Carla", "Carla"},
      {"Bianca", "Bianca"},

      // Icelandic
      {"Karl", "Karl"},
      {"Dora", "Dora"},

      // French
      {"Mathieu", "Mathieu"},
      {"Celine", "Celine"},

      // French, Canadian
      {"Chantal", "Chantal"},

      // Spanish, American
      {"Penelope", "Penelope"},
      {"Miguel", "Miguel"},

      // Spanish, Mexican
      {"Mia", "Mia"},

      // Spanish, European
      {"Enrique", "Enrique"},
      {"Conchita", "Conchita"},

      // English Welsh
      {"Geraint", "Geraint"},

      // English, American
      {"Salli", "Salli"},
      {"Matthew", "Matthew"},
      {"Kimberly", "Kimberly"},
      {"Kendra", "Kendra"},
      {"Justin", "Justin"},
      {"Joey", "Joey"},
      {"Joanna", "Joanna"},
      {"Ivy", "Ivy"},

      // English, Indian
      {"Raveena", "Raveena"},

      // English, Hindi
      {"Aditi", "Aditi"},

      // English, British
      {"Emma", "Emma"},
      {"Brian", "Brian"},
      {"Amy", "Amy"},

      // English, Australian
      {"Russell", "Russell"},
      {"Nicole", "Nicole"},

      // German
      {"Vicki", "Vicki"},
      {"Marlene", "Marlene"},
      {"Hans", "Hans"},

      // Danish
      {"Naja", "Naja"},
      {"Mads", "Mads"},

      // Welsh
      {"Gwyneth", "Gwyneth"},

      // Chinese, Mandarin
      {"Zhiyu", "Zhiyu"},

      // Chinese, Cantonese
      {"Tracy", "Tracy"},
      {"Danny", "Danny"},

      // Chinese, Mandarin
      {"Huihui", "Huihui"},
      {"Yaoyao", "Yaoyao"},
      {"Kangkang", "Kangkang"},

      // Chinese, Taiwanese
      {"HanHan", "HanHan"},
      {"Zhiwei", "Zhiwei"},

      // ??
      {"Asaf", "Asaf"},
      {"An", "An"},

      // Greek
      {"Stefanos", "Stefanos"},

      // Slovak
      {"Filip", "Filip"},

      // Bulgarian
      {"Ivan", "Ivan"},

      // Finnish
      {"Heidi", "Heidi"},

      // Catalan
      {"Herena", "Herena"},

      // Hindi
      {"Kalpana", "Kalpana"},
      {"Hemant", "Hemant"},

      // Croatian
      {"Matej", "Matej"},

      // Indonesian
      {"Andika", "Andika"},

      // Malay
      {"Rizwan", "Rizwan"},

      // Slovenian
      {"Lado", "Lado"},

      // Tamil, India
      {"Valluvar", "Valluvar"},

      // English, Canadian
      {"Linda", "Linda"},
      {"Heather", "Heather"},

      // English, Irish
      {"Sean", "Sean"},

      // German, Austria
      {"Michael", "Michael"},

      // German, Switzerland
      {"Karsten", "Karsten"},

      // French, Switzerland
      {"Guillaume", "Guillaume"},

      // Thai
      {"Pattara", "Pattara"},

      // Czech
      {"Jakub", "Jakub"},

      // Hungarian
      {"Szabolcs", "Szabolcs"},

      // Arabic, Egypt
      {"Hoda", "Hoda"},

      // Arabic, Saudi Arabia
      {"Naayf", "Naayf"}
    };

    public static string GetVoice(string name)
    {
      if (name is null || name.Length == 0) return string.Empty;

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

    public static WaveOut GetTTS(string text, string voice = "Brian", float soundVolume = 1f)
    {
      if (text is null || text.Length == 0) return null;

      Stream stream;
      using HttpRequestMessage request = new(HttpMethod.Get, $"https://api.streamelements.com/kappa/v2/speech?voice={voice}&text={text}");
      stream = Notifications.Client.Send(request).Content.ReadAsStream();

      return Audio.GetMp3Sound(stream, soundVolume);
    }

    /// <summary> Can be used to get available StreamElements voices. The voices are printed to the console. </summary>
    public static void RequestVoices()
    {
      string[] voices = Array.Empty<string>();

      // Ask for speech without specifying the voice, error message will contain all available voices
      using HttpRequestMessage request = new(HttpMethod.Get, "https://api.streamelements.com/kappa/v2/speech?voice=");
      StreamElementsResponse response = StreamElementsResponse.Deserialize(Notifications.Client.Send(request).Content.ReadAsStringAsync().Result);
      if (response?.Message?.Length > 0)
      {
        int startIndex = response.Message.IndexOf("must be one of");
        if (startIndex > 0)
        {
          startIndex = response.Message.IndexOf('[', startIndex) + 1;
          int endIndex = response.Message.IndexOf(']', startIndex);
          if (endIndex > startIndex)
          {
            voices = response.Message.Substring(startIndex, endIndex - startIndex).Split(",", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
          }
        }
      }

      MainWindow.ConsoleWarning(string.Concat(">> StreamElements voices: ", string.Join(", ", voices)));
    }
  }
}
