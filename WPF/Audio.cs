using NAudio.Wave;
using System.IO;
using System.Threading;

public class Audio
{
    public static void PlaySound(object? _stream)
    {
        using (var reader = new Mp3FileReader(_stream as Stream))
        {
            using (var waveOut = new WaveOut())
            {
                waveOut.Init(reader);
                waveOut.Play();

                while (waveOut.PlaybackState == PlaybackState.Playing) Thread.Sleep(1); // Wait for audio to finish
            }
        }
    }
}
