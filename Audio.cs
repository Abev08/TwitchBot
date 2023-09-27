using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.Collections.Generic;
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
      FileInfo file = new(path);
      if (!file.Exists) return null;

      WaveOut waveOut = new();
      waveOut.Init(new WaveFileReader(file.FullName));
      waveOut.Volume = volume;

      return waveOut;
    }

    public static WaveOut GetWavSound(IEnumerable<ISampleProvider> samples, float volume)
    {
      WaveOut outputDevice = new();
      outputDevice.Init(new ConcatenatingSampleProvider(samples));
      outputDevice.Volume = volume;

      return outputDevice;
    }

    public static void AddToSampleProviderList(object sound, ref List<ISampleProvider> sounds, int index = -1)
    {
      if (sound is null) return;

      ISampleProvider sample;
      if (sound is string)
      {
        FileInfo file = new((string)sound);
        if (file.Exists) { sample = new Mp3FileReader(file.FullName).ToSampleProvider(); }
        else { return; }
      }
      else if (sound is Stream) { sample = new Mp3FileReader((Stream)sound).ToSampleProvider(); }
      else if (sound is FileInfo file)
      {
        if (file.Exists) { sample = new Mp3FileReader(file.FullName).ToSampleProvider(); }
        else { return; }
      }
      else
      {
        MainWindow.ConsoleWarning($">> Sound type {sound.GetType()} not supported!");
        return;
      }

      if (sample.WaveFormat.SampleRate != 44100)
      {
        WdlResamplingSampleProvider resampler = new(sample, 44100);
        sample = resampler.ToStereo();
      }
      if (sample.WaveFormat.Channels == 1) { sample = sample.ToStereo(); }

      if (index == -1) { sounds.Add(sample); }
      else { sounds.Insert(index, sample); }
    }
  }
}
