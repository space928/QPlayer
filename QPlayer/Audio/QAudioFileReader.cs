using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;

namespace QPlayer.Audio;

/// <summary>
/// A mostly drop in replacement for <see cref="NAudio.Wave.AudioFileReader"/> which removes a few unneeded features
/// </summary>
public class QAudioFileReader : WaveStream, ISampleProvider
{
    public string FileName { get; init; }
    public override long Length => length;
    public override long Position
    {
        get => readerStream!.Position;
        set => readerStream!.Position = Math.Clamp(value, 0, length - 1);
    }
    public override WaveFormat WaveFormat => waveFormat;
    public WaveStream? ReaderStream => readerStream;

    private WaveStream? readerStream;
    private readonly long length;
    private readonly WaveFormat waveFormat;
    private readonly ISampleProvider sampleProvider;

    public QAudioFileReader(string fileName)
    {
        FileName = fileName;
        CreateReaderStream(fileName);
        sampleProvider = ConvertWaveProviderIntoSampleProvider(readerStream);
        length = readerStream.Length;
        waveFormat = sampleProvider.WaveFormat;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        return sampleProvider.Read(buffer, offset, count);
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var floats = Unsafe.As<byte[], float[]>(ref buffer);
        return Read(floats, offset >> 2, count >> 2) << 2;
    }

    [MemberNotNull(nameof(readerStream))]
    private void CreateReaderStream(string fileName)
    {
        if (fileName.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
        {
            readerStream = new WaveFileReader(fileName);
            if (readerStream.WaveFormat.Encoding != WaveFormatEncoding.Pcm && readerStream.WaveFormat.Encoding != WaveFormatEncoding.IeeeFloat)
            {
                readerStream = WaveFormatConversionStream.CreatePcmStream(readerStream);
                readerStream = new BlockAlignReductionStream(readerStream);
            }
        }
        else if (fileName.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
        {
            if (Environment.OSVersion.Version.Major < 6)
            {
                readerStream = new Mp3FileReader(fileName);
            }
            else
            {
                readerStream = new MediaFoundationReader(fileName);
            }
        }
        else if (fileName.EndsWith(".aiff", StringComparison.OrdinalIgnoreCase) || fileName.EndsWith(".aif", StringComparison.OrdinalIgnoreCase))
        {
            readerStream = new AiffFileReader(fileName);
        }
        else
        {
            readerStream = new MediaFoundationReader(fileName);
        }
    }

    public static ISampleProvider ConvertWaveProviderIntoSampleProvider(IWaveProvider waveProvider)
    {
        if (waveProvider.WaveFormat.Encoding == WaveFormatEncoding.Pcm)
        {
            return waveProvider.WaveFormat.BitsPerSample switch
            {
                8 => new Pcm8BitToSampleProvider(waveProvider),
                16 => new Pcm16BitToSampleProvider(waveProvider),
                24 => new Pcm24BitToSampleProvider(waveProvider),
                32 => new Pcm32BitToSampleProvider(waveProvider),
                _ => throw new InvalidOperationException("Unsupported bit depth"),
            };
        }

        if (waveProvider.WaveFormat.Encoding == WaveFormatEncoding.IeeeFloat)
        {
            return waveProvider.WaveFormat.BitsPerSample switch
            {
                64 => new WaveToSampleProvider64(waveProvider),
                32 => new WaveToSampleProvider(waveProvider),
                _ => throw new InvalidOperationException("Unsupported bit depth"),
            };
        }

        throw new ArgumentException("Unsupported source encoding");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && readerStream != null)
        {
            readerStream.Dispose();
            readerStream = null;
        }

        base.Dispose(disposing);
    }
}
