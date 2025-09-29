using NAudio.Utils;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace QPlayer.Audio;

public class MixerSampleProvider : ISampleProvider
{
    private readonly List<ISampleProvider> mixerInputs;
    private const int maxInputs = 1024;
    private float[] sourceBuffer = [];
    private bool firstRead = true;

    public WaveFormat WaveFormat { get; private set; }
    public event EventHandler<SampleProviderEventArgs>? MixerInputEnded;

    public MixerSampleProvider(WaveFormat waveFormat)
    {
        WaveFormat = waveFormat;
        mixerInputs = [];
    }

    public int Read(float[] buffer, int offset, int count)
    {
        if (firstRead)
        {
            firstRead = false;
            SetThreadPriority();
        }

        sourceBuffer = BufferHelpers.Ensure(sourceBuffer, count);
        lock (mixerInputs)
        {
            if (mixerInputs.Count == 0)
            {
                // Why are we using this instead of Array.Clear?
                // Because this float array is actually (usually) a byte array in disguise.
                // But since the array knows this, it ends up nott clearing enough items.
                // Hence, we treat it as a span which behaves as expected.
                buffer.AsSpan(offset, count).Clear();
                return count;
            }

            // Copy the first input to the output buffer
            int read = mixerInputs[0].Read(buffer, offset, count);
            if (read < count)
            {
                InputEnded(0);
                buffer.AsSpan(offset + read, count - read).Clear();
            }

            // Read each subsequant input and add them to the buffer
            for (int i = 1; i < mixerInputs.Count; i++)
            {
                read = mixerInputs[i].Read(sourceBuffer, 0, count);
                if (read < count)
                    InputEnded(i);

                ref var srcBuf = ref MemoryMarshal.GetArrayDataReference(sourceBuffer);
                ref var dstBuf = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(buffer), offset);
                int s = 0;
                if (Vector256.IsHardwareAccelerated)
                {
                    for (; s <= read - Vector256<float>.Count; s += Vector256<float>.Count)
                    {
                        var a = Vector256.LoadUnsafe(ref srcBuf);
                        var b = Vector256.LoadUnsafe(ref dstBuf);

                        var res = Vector256.Add(a, b);
                        ref var dstByte = ref Unsafe.As<float, byte>(ref dstBuf);
                        Unsafe.WriteUnaligned(ref dstByte, res);

                        dstBuf = ref Unsafe.Add(ref dstBuf, Vector<float>.Count);
                        srcBuf = ref Unsafe.Add(ref srcBuf, Vector<float>.Count);
                    }
                }

                for (; s < read; s++)
                {
                    dstBuf += srcBuf;
                    dstBuf = ref Unsafe.Add(ref dstBuf, 1);
                    srcBuf = ref Unsafe.Add(ref srcBuf, 1);
                }
            }
        }

        return count;

        void InputEnded(int input)
        {
            this.MixerInputEnded?.Invoke(this, new SampleProviderEventArgs(mixerInputs[input]));
            mixerInputs.RemoveAt(input);
        }
    }

    public void AddMixerInput(IWaveProvider input)
    {
        AddMixerInput(ConvertWaveProviderIntoSampleProvider(input));
    }

    public void AddMixerInput(ISampleProvider input)
    {
        if (WaveFormat.SampleRate != input.WaveFormat.SampleRate || WaveFormat.Channels != input.WaveFormat.Channels)
            throw new ArgumentException("All mixer inputs must have the same WaveFormat");

        lock (mixerInputs)
        {
            if (mixerInputs.Count >= maxInputs)
                throw new InvalidOperationException("Too many mixer inputs");

            mixerInputs.Add(input);
        }
    }

    /*public bool RemoveMixerInput(IWaveProvider input)
    {
        mixerInputs.OfType<SampleProviderConverterBase>().First(x=>x.source)
    }*/

    public bool RemoveMixerInput(ISampleProvider input)
    {
        lock (mixerInputs)
        {
            return mixerInputs.Remove(input);
        }
    }

    public void RemoveAllMixerInputs()
    {
        lock (mixerInputs)
        {
            mixerInputs.Clear();
        }
    }

    public bool Contains(ISampleProvider sampleProvider)
    {
        lock (mixerInputs)
        {
            return mixerInputs.Contains(sampleProvider);
        }
    }

    private static void SetThreadPriority()
    {
        int task = 0;
        var handle = AVRTLib.AvSetMmThreadCharacteristicsW("Pro Audio", ref task);
        AVRTLib.AvSetMmThreadPriority(handle, AVRTLib.AVRT_PRIORITY.AVRT_PRIORITY_HIGH);
        //Thread.CurrentThread.Priority = ThreadPriority.Highest;
        Thread.CurrentThread.Name = "Audio Thread";
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
                _ => new WaveToSampleProvider(waveProvider)
            };
        }

        throw new ArgumentException("Unsupported source encoding");
    }
}
