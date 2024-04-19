using System;
using System.Collections.Generic;
using System.IO;

using NAudio.Wave;
using NAudio.Wave.SampleProviders;

using Serilog;

namespace AbevBot
{
  /// <summary> Everything related to playing audio. </summary>
  public static class Audio
  {
    /* Calculation of playback rates
      From    To      Playback rate calc.		Playback rate assigned
      44100   8000    0,181405896           0,15
      44100   11025		0,25                  0,25
      44100   16000		0,362811791           0,35
      44100   22050		0,5                   0,5
      44100   44100		1                     1
      44100   48000		1,088435374           1,5
      44100   88200		2                     2
      44100   96000		2,176870748           2,5
      44100   176400	4                     4
      44100   192000	4,353741497           -
      44100   352800	8                     -
    */
    /// <summary> Sound playback rates for upsounds and downsounds. </summary>
    private static readonly double[] PLAYBACKRATES = { 0.15, 0.25, 0.35, 0.5, 1, 1.5, 2, 2.5, 4 };

    /// <summary> Creates output device that can play provided sound. </summary>
    /// <param name="sound">string, FileInfo, Stream object, ISampleProvider or IEnumerable<ISampleProvider> referencing sound to play.
    /// .mp3 and .wav files are supported. Stream object supports only .mp3.</param>
    /// <param name="volume"> Volume of audio output device. </param>
    /// <returns> Output devide that can play provided sound. </returns>
    public static WaveOut GetSound(object sound, float volume)
    {
      if (sound is null) return null;

      // FIXME: Mp3FileReader, WaveFileReader, etc. doesn't free the files after reading - we need to manually .Dispose() them,
      // So there should be list of xxxFileReader and after the device finished playing something like
      // ```
      // device.Stop();
      // foreach (var file in files) file.Dispose();
      // device.Dispose();
      // ```
      // If it's not being done like this (like right now) after the device is disposed the file is being locked by the program
      // Also using AudioFileReader instead of Mp3FileReader / WaveFileReader may be better?

      ISampleProvider sample;
      if (sound is ISampleProvider) { sample = (ISampleProvider)sound; }
      else if (sound is IEnumerable<ISampleProvider>) { sample = new ConcatenatingSampleProvider((IEnumerable<ISampleProvider>)sound); }
      else if (sound is Stream) { sample = new Mp3FileReader((Stream)sound).ToSampleProvider(); }
      else if (sound is FileInfo || sound is string)
      {
        FileInfo file = sound is FileInfo ? (FileInfo)sound : new((string)sound);
        if (!file.Exists)
        {
          Log.Warning("Sound not found {file}!", file.FullName);
          return null;
        }
        else if (file.Extension.Equals(".mp3")) { sample = new Mp3FileReader(file.FullName).ToSampleProvider(); }
        else if (file.Extension.Equals(".wav")) { sample = new WaveFileReader(file.FullName).ToSampleProvider(); }
        else
        {
          Log.Warning("Sound extension {extension} not supported!", file.Extension);
          return null;
        }
      }
      else if (sound is byte[])
      {
        var ms = new MemoryStream((byte[])sound);
        sample = new WaveFileReader(ms).ToSampleProvider();
      }
      else
      {
        Log.Warning("Sound type {type} not supported!", sound.GetType());
        return null;
      }

      WaveOut outputDevice = new();
      outputDevice.Init(sample);
      outputDevice.Volume = volume;

      return outputDevice;
    }

    /// <summary> Returns wav file data (byte array) from provided sound. </summary>
    /// <param name="sound">string, FileInfo, Stream object, ISampleProvider or IEnumerable<ISampleProvider> referencing sound to play.
    /// .mp3 and .wav files are supported. Stream object supports only .mp3.</param>
    /// <returns>Array with wav file data</returns>
    public static byte[] GetSoundData(object sound)
    {
      if (sound is null) return null;

      WaveStream audioStream = null;
      ISampleProvider sample = null;
      if (sound is ISampleProvider) { sample = (ISampleProvider)sound; }
      else if (sound is IEnumerable<ISampleProvider>) { sample = new ConcatenatingSampleProvider((IEnumerable<ISampleProvider>)sound); }
      else if (sound is Stream) { audioStream = new Mp3FileReader((Stream)sound); }
      else if (sound is FileInfo || sound is string)
      {
        FileInfo file = sound is FileInfo ? (FileInfo)sound : new((string)sound);
        if (!file.Exists)
        {
          Log.Warning("Sound not found {file}!", file.FullName);
          return null;
        }
        else
        {
          audioStream = new AudioFileReader(file.FullName);
        }
      }
      else
      {
        Log.Warning("Sound type {type} not supported!", sound.GetType());
        return null;
      }

      MemoryStream str = new();
      WaveFileWriter.WriteWavFileToStream(str, audioStream is null ? sample.ToWaveProvider() : audioStream);
      audioStream?.Dispose();

      return str.ToArray();
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
          Log.Warning("Sound extension {extension} not supported!", file.Extension);
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
          Log.Warning("Sound extension {extension} not supported!", file.Extension);
          return;
        }
      }
      else if (sound is ISampleProvider) { sample = (ISampleProvider)sound; }
      else
      {
        Log.Warning("Sound type {type} not supported!", sound.GetType());
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

    /// <summary> Creates -upSample or -downSample from privided sample. In the -upSample playback rate increases. In the -downSample playback rate decreases. </summary>
    /// <param name="file"> The origianl sample that should be used. </param>
    /// <param name="downSample"> true if generated sample should be -downSample, otherwise false. </param>
    /// <returns> Generated sample. </returns>
    public static ISampleProvider GetUpOrDownSample(FileInfo file, bool downSample)
    {
      if (!file.Exists || (!file.Extension.Equals(".mp3") && !file.Extension.Equals(".wav")))
      {
        Log.Warning("Sound extension {extension} not supported!", file.Extension);
        return null;
      }

      byte[] sample = File.ReadAllBytes(file.FullName);
      List<ISampleProvider> samples = new();

      if (downSample)
      {
        for (int i = PLAYBACKRATES.Length - 1; i >= 0; i--)
        {
          byte[] sampleCopy = new byte[sample.Length];
          Array.Copy(sample, sampleCopy, sample.Length);

          var provider = new SoundTouch.Net.NAudioSupport.SoundTouchWaveProvider(
            file.Extension.Equals(".mp3") ?
              new Mp3FileReader(new MemoryStream(sampleCopy)).ToSampleProvider().ToWaveProvider() :
              new WaveFileReader(new MemoryStream(sampleCopy)).ToSampleProvider().ToWaveProvider(),
            new SoundTouch.SoundTouchProcessor() { Rate = PLAYBACKRATES[i] }
          );

          samples.Add(provider.ToSampleProvider());
        }
      }
      else
      {
        for (int i = 0; i < PLAYBACKRATES.Length; i++)
        {
          byte[] sampleCopy = new byte[sample.Length];
          Array.Copy(sample, sampleCopy, sample.Length);

          var provider = new SoundTouch.Net.NAudioSupport.SoundTouchWaveProvider(
            file.Extension.Equals(".mp3") ?
              new Mp3FileReader(new MemoryStream(sampleCopy)).ToSampleProvider().ToWaveProvider() :
              new WaveFileReader(new MemoryStream(sampleCopy)).ToSampleProvider().ToWaveProvider(),
            new SoundTouch.SoundTouchProcessor() { Rate = PLAYBACKRATES[i] }
          );

          samples.Add(provider.ToSampleProvider());
        }
      }

      ISampleProvider sampleProvider = new ConcatenatingSampleProvider(samples);
      if (sampleProvider.WaveFormat.SampleRate != 44100)
      {
        WdlResamplingSampleProvider resampler = new(sampleProvider, 44100);
        sampleProvider = resampler.ToStereo();
      }
      if (sampleProvider.WaveFormat.Channels == 1) { sampleProvider = sampleProvider.ToStereo(); }
      return sampleProvider;
    }
  }
}
