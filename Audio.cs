using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.Collections.Generic;
using System.IO;

namespace AbevBot
{
  /// <summary> Everything related to playing audio. </summary>
  public static class Audio
  {
    /// <summary> Creates output device that can play provided sound. </summary>
    /// <param name="sound"><para> string, FileInfo, Stream object, ISampleProvider or IEnumerable<ISampleProvider> referencing sound to play. </para>
    /// <para> .mp3 and .wav files are supported. Stream object supports only .mp3. </para></param>
    /// <param name="volume"> Volume of audio output device. </param>
    /// <returns> Output devide that can play provided sound. </returns>
    public static WaveOut GetSound(object sound, float volume)
    {
      if (sound is null) return null;

      ISampleProvider sample;
      if (sound is ISampleProvider) { sample = (ISampleProvider)sound; }
      else if (sound is IEnumerable<ISampleProvider>) { sample = new ConcatenatingSampleProvider((IEnumerable<ISampleProvider>)sound); }
      else if (sound is string)
      {
        FileInfo file = new((string)sound);
        if (file.Exists == true && file.Extension.Equals(".mp3")) { sample = new Mp3FileReader(file.FullName).ToSampleProvider(); }
        else if (file.Exists == true && file.Extension.Equals(".wav")) { sample = new WaveFileReader(file.FullName).ToSampleProvider(); }
        else
        {
          MainWindow.ConsoleWarning($">> Sound extension {file.Extension} not supported!");
          return null;
        }
      }
      else if (sound is Stream) { sample = new Mp3FileReader((Stream)sound).ToSampleProvider(); }
      else if (sound is FileInfo file)
      {
        if (file.Exists == true && file.Extension.Equals(".mp3")) { sample = new Mp3FileReader(file.FullName).ToSampleProvider(); }
        else if (file.Exists == true && file.Extension.Equals(".wav")) { sample = new WaveFileReader(file.FullName).ToSampleProvider(); }
        else
        {
          MainWindow.ConsoleWarning($">> Sound extension {file.Extension} not supported!");
          return null;
        }
      }
      else
      {
        MainWindow.ConsoleWarning($">> Sound type {sound.GetType()} not supported!");
        return null;
      }

      WaveOut outputDevice = new();
      outputDevice.Init(sample);
      outputDevice.Volume = volume;

      return outputDevice;
    }

    /// <summary><para> Adds provided sound to provided list of samples. </para>
    /// <para> If index is provided the sound instead of adding is inserted at provided index. </para></summary>
    /// <param name="sound"><para> string, FileInfo or Stream object referencing sound to play. </para>
    /// <para> .mp3 and .wav files are supported. Stream object supports only .mp3. </para></param>
    /// <param name="sounds"> List of sounds to which provided sound should be added. </param>
    /// <param name="index"> Index at which the provided sound should be added to sounds. If not provided it would bet added at the end. </param>
    public static void AddToSampleProviderList(object sound, ref List<ISampleProvider> sounds, int index = -1)
    {
      if (sound is null) return;

      ISampleProvider sample;
      if (sound is string)
      {
        FileInfo file = new((string)sound);
        if (file.Exists == true && file.Extension.Equals(".mp3")) { sample = new Mp3FileReader(file.FullName).ToSampleProvider(); }
        else if (file.Exists == true && file.Extension.Equals(".wav")) { sample = new WaveFileReader(file.FullName).ToSampleProvider(); }
        else
        {
          MainWindow.ConsoleWarning($">> Sound extension {file.Extension} not supported!");
          return;
        }
      }
      else if (sound is Stream) { sample = new Mp3FileReader((Stream)sound).ToSampleProvider(); }
      else if (sound is FileInfo file)
      {
        if (file.Exists == true && file.Extension.Equals(".mp3")) { sample = new Mp3FileReader(file.FullName).ToSampleProvider(); }
        else if (file.Exists == true && file.Extension.Equals(".wav")) { sample = new WaveFileReader(file.FullName).ToSampleProvider(); }
        else
        {
          MainWindow.ConsoleWarning($">> Sound extension {file.Extension} not supported!");
          return;
        }
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

    /// <summary><para> Plays the provided sound and forgets about it (doesn't keep reference to it). </para>
    /// <para> This means it can't be stopped, paused, etc. it has to finish on its own. </para></summary>
    /// <param name="sound"><para> string, FileInfo or Stream object referencing sound to play. </para>
    /// <para> .mp3 and .wav files are supported. Stream object supports only .mp3. </para></param>
    /// <param name="volume"> Volume at which the audio should be played. </param>
    public static void PlayAndForget(object sound, float volume = 1f)
    {
      if (sound is null) return;

      WaveOut outputDevice = GetSound(sound, volume);
      if (outputDevice is null) return;

      outputDevice.PlaybackStopped += (sender, e) => { ((WaveOut)sender).Dispose(); };
      outputDevice.Play();
    }
  }
}
