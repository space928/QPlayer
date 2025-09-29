using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using QPlayer.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace QPlayer.Audio;

public enum AudioOutputDriver
{
    Wave,
    DirectSound,
    WASAPI,
    ASIO
}

public class AudioPlaybackManager : IDisposable
{
    private readonly MainViewModel mainViewModel;
    private readonly MixerSampleProvider mixer;
    private readonly MeteringSampleProviderVec meteringProvider;
    private readonly Dictionary<ISamplePositionProvider, (ISamplePositionProvider convertedStream, Action<ISampleProvider>? completedCallback)> activeChannels;
    private readonly ManualResetEventSlim deviceClosedEvent;
    private readonly SynchronizationContext? synchronizationContext;

    private AudioOutputDriver driver;
    private IWavePlayer? device;
    private int restartAudioDeviceDelay = 100;
    private CancellationTokenSource cancelAudioDeviceRestart;

    public event Action<MeteringEvent> OnMixerMeter
    {
        add
        {
            meteringProvider.OnMeter += value;
        }
        remove
        {
            meteringProvider.OnMeter -= value;
        }
    }

    public AudioPlaybackManager(MainViewModel mainViewModel)
    {
        this.mainViewModel = mainViewModel;
        synchronizationContext = SynchronizationContext.Current;
        cancelAudioDeviceRestart = new();
        activeChannels = [];
        deviceClosedEvent = new(false);
        // TODO: Expose sample rate, channel, and latency controls
        mixer = new(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2));
        //mixer.ReadFully = true;
        mixer.MixerInputEnded += (o, e) =>
        {
            if (activeChannels.FirstOrDefault(x => x.Value.convertedStream == e.SampleProvider) is var channel)
            {
                activeChannels.Remove(channel.Key);
                if (synchronizationContext != null)
                    synchronizationContext.Post((state) =>
                    {
                        channel.Value.completedCallback?.Invoke(e.SampleProvider);
                    }, null);
                else
                    channel.Value.completedCallback?.Invoke(e.SampleProvider);

                // Need to put this somewhere...
                restartAudioDeviceDelay = 100;
            }
        };

        meteringProvider = new(mixer);
        // 30 notifications per second
        meteringProvider.SamplesPerNotification = mixer.WaveFormat.SampleRate / 30;
    }

    public void Stop()
    {
        device?.Stop();
    }

    public void Dispose()
    {
        CloseAudioDevices();
        deviceClosedEvent.Dispose();
    }

    private void CloseAudioDevices()
    {
        if (device == null)
            return;

        MainViewModel.Log("Closing audio device...");
        try
        {
            device.Stop();
        }
        catch { }
        // Wait for the device to finish playing...
        deviceClosedEvent.Wait(200);
        device?.Dispose();
        device = null;
    }

    private void DevicePlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception != null)
        {
            MainViewModel.Log($"Audio device error! \n{e.Exception}", MainViewModel.LogLevel.Error);

            restartAudioDeviceDelay *= 2;
            restartAudioDeviceDelay = Math.Min(restartAudioDeviceDelay, 60 * 1000);
            cancelAudioDeviceRestart = new();
            Task.Delay(restartAudioDeviceDelay, cancelAudioDeviceRestart.Token).ContinueWith(_ =>
            {
                synchronizationContext?.Post(_ =>
                {
                    MainViewModel.Log($"Automatically restarting audio driver...");
                    mainViewModel.OpenAudioDevice();
                }, null);
            });
        }
        deviceClosedEvent.Set();
    }

    public async Task<(object? key, string identifier)[]> GetOutputDevices(AudioOutputDriver driver)
    {
        return await Task.Run(() =>
        {
            switch (driver)
            {
                case AudioOutputDriver.Wave:
                    return Enumerable.Range(0, WaveOut.DeviceCount).Select(x =>
                    {
                        var caps = WaveOut.GetCapabilities(x);
                        return ((object?)x, $"{x}: {caps.ProductName} ({caps.Channels} channels) (Wave)");
                    }).ToArray();
                case AudioOutputDriver.DirectSound:
                    return DirectSoundOut.Devices.Select(x =>
                    {
                        return ((object?)x.Guid, $"{x.Description} (DirectSound)");
                    }).ToArray();
                case AudioOutputDriver.WASAPI:
                    var enumerator = new MMDeviceEnumerator();
                    return enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).Select(x =>
                    {
                        return ((object?)x.ID, $"{x.FriendlyName} {x.DeviceFriendlyName} (WASAPI)");
                    }).ToArray();
                case AudioOutputDriver.ASIO:
                    return AsioOut.GetDriverNames().Select(x =>
                    {
                        return ((object?)x, $"{x} (ASIO)");
                    }).ToArray();
                default:
                    return [];
            }
        });
    }

    public void OpenOutputDevice(AudioOutputDriver driver, object? key, int desiredLatency = 40)
    {
        CloseAudioDevices();
        desiredLatency = Math.Max(desiredLatency, 0);
        if (key == null)
            return;

        cancelAudioDeviceRestart.Cancel();

        try
        {
            device = driver switch
            {
                AudioOutputDriver.Wave => new WaveOutEvent() { DeviceNumber = (int)key, DesiredLatency = desiredLatency },
                AudioOutputDriver.DirectSound => new DirectSoundOut((Guid)key, desiredLatency),
                AudioOutputDriver.WASAPI => new WasapiOut(new MMDeviceEnumerator().GetDevice((string)key), AudioClientShareMode.Shared, true, desiredLatency),
                AudioOutputDriver.ASIO => new AsioOut((string)key),
                _ => throw new NotImplementedException($"Unsupported audio driver '{driver}'!"),
            };
        }
        catch (Exception ex)
        {
            MainViewModel.Log($"Failed to open audio device '{key}' with driver '{driver}'.\n" + ex,
                MainViewModel.LogLevel.Error);
            return;
        }

        this.driver = driver;
        device.PlaybackStopped += DevicePlaybackStopped;
        deviceClosedEvent.Reset();
        try
        {
            device.Init(meteringProvider);
            device.Play();
        }
        catch (Exception ex)
        {
            MainViewModel.Log($"Failed to start device '{key}' with driver '{driver}'.\n" + ex,
                MainViewModel.LogLevel.Error);
            return;
        }
        /*var sig = new SignalGenerator();
        PlaySound(sig);*/
        MainViewModel.Log($"Opened sound device '{key}' with driver '{driver}'!", MainViewModel.LogLevel.Info);
    }

    /// <summary>
    /// Creates an ISampleProvider which converts the given sample stream to one compatible with the mixer.
    /// 
    /// This converts both Mono to Stereo and resamples the input stream.
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    /// <exception cref="NotImplementedException"></exception>
    public ISamplePositionProvider ConvertToMixerFormat(ISamplePositionProvider input)
    {
        if (input.WaveFormat.SampleRate != mixer.WaveFormat.SampleRate)
        {
            // Resample
            input = new WdlResamplingProviderVec(input, mixer.WaveFormat.SampleRate, input.WaveFormat.Channels);
        }

        if (input.WaveFormat.Channels == mixer.WaveFormat.Channels)
        {
            return input;
        }
        else if (input.WaveFormat.Channels == 1 && mixer.WaveFormat.Channels == 2)
        {
            return new MonoToStereoSampleProviderVec(input);
        }
        throw new NotImplementedException("Not yet implemented this channel count conversion");
    }

    /// <summary>
    /// Starts the playback of a sound through the mixer.
    /// </summary>
    /// <param name="provider">the sample stream to play</param>
    /// <param name="onCompleted">a callback raised when the stream is removed from the mixer</param>
    public void PlaySound(ISamplePositionProvider provider, Action<ISampleProvider>? onCompleted = null)
    {
        try
        {
            if (activeChannels.ContainsKey(provider))
                return;

            var converted = ConvertToMixerFormat(provider);
            activeChannels.Add(provider, (converted, onCompleted));
            mixer.AddMixerInput(converted);
        }
        catch (Exception ex)
        {
            MainViewModel.Log("Error while trying to play sound! \n" + ex, MainViewModel.LogLevel.Error);
        }
    }

    /// <summary>
    /// Stops the playback of a sound stream.
    /// </summary>
    /// <param name="provider">the sample stream to stop</param>
    public void StopSound(ISamplePositionProvider provider)
    {
        try
        {
            if (activeChannels.TryGetValue(provider, out var channel))
            {
                mixer.RemoveMixerInput(channel.convertedStream);
                activeChannels.Remove(provider);
            }
        }
        catch (Exception ex)
        {
            MainViewModel.Log("Error while trying to stop sound! \n" + ex, MainViewModel.LogLevel.Error);
        }
    }

    /// <summary>
    /// Gets whether a sound stream is currently playing.
    /// </summary>
    /// <param name="provider"></param>
    /// <returns></returns>
    public bool IsPlaying(ISamplePositionProvider provider) => activeChannels.ContainsKey(provider);

    /// <summary>
    /// Stops all sound sources.
    /// </summary>
    public void StopAllSounds()
    {
        mixer.RemoveAllMixerInputs();
        foreach (var channel in activeChannels)
            channel.Value.completedCallback?.Invoke(channel.Key);
        activeChannels.Clear();
    }
}

public static partial class AVRTLib
{
    [LibraryImport("avrt")]
    internal static partial nint AvSetMmThreadCharacteristicsW([MarshalAs(UnmanagedType.LPWStr)] string taskName, ref int taskIndex);
    [LibraryImport("avrt")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool AvSetMmThreadPriority(nint AvrtHandle, AVRT_PRIORITY Priority);

    public enum AVRT_PRIORITY : int
    {
        AVRT_PRIORITY_NORMAL = 0,
        AVRT_PRIORITY_CRITICAL = 2,
        AVRT_PRIORITY_HIGH = 1,
        AVRT_PRIORITY_LOW = -1,
    }
}
