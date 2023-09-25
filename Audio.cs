using NAudio.Wave;
using System.IO;

namespace AbevBot
{
  public static class Audio
  {
    public static WaveOut GetMp3Sound(object _stream, float volume)
    {
      if (_stream is null) return null;

      WaveOut waveOut = new();
      waveOut.Init(new Mp3FileReader(_stream as Stream));
      waveOut.Volume = volume;

      return waveOut;
    }

    public static WaveOut GetWavSound(string path, float volume)
    {
      WaveOut waveOut = new();
      waveOut.Init(new WaveFileReader(new FileInfo(path).FullName));
      waveOut.Volume = volume;

      return waveOut;
    }
  }
}
