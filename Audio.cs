using NAudio.Wave;
using System.IO;

public class Audio
{
    public static WaveOut PlaySound(object? _stream)
    {
        WaveOut waveOut = new WaveOut();
        waveOut.Init(new Mp3FileReader(_stream as Stream));
        waveOut.Play();

        return waveOut;
    }
}
