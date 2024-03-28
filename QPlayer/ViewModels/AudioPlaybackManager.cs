﻿using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace QPlayer.ViewModels
{
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
        private AudioOutputDriver driver;
        private IWavePlayer? device;
        private MixingSampleProvider mixer;
        private readonly Dictionary<ISampleProvider, (ISampleProvider convertedStream, Action<ISampleProvider>? completedCallback)> activeChannels;
        private ManualResetEventSlim deviceClosedEvent;
        private SynchronizationContext? synchronizationContext;

        public AudioPlaybackManager(MainViewModel mainViewModel)
        {
            this.mainViewModel = mainViewModel;
            synchronizationContext = SynchronizationContext.Current;
            activeChannels = new();
            deviceClosedEvent = new(false);
            // TODO: Expose sample rate, channel, and latency controls
            mixer = new(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2));
            mixer.ReadFully = true;
            mixer.MixerInputEnded += (o, e) =>
            {
                if (activeChannels.FirstOrDefault(x=>x.Value.convertedStream == e.SampleProvider) is var channel)
                {
                    activeChannels.Remove(channel.Key);
                    if(synchronizationContext != null)
                        synchronizationContext.Post((state) =>
                        {
                            channel.Value.completedCallback?.Invoke(e.SampleProvider);
                        }, null);
                    else
                        channel.Value.completedCallback?.Invoke(e.SampleProvider);
                }
            };
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
            device.Stop();
            // Wait for the device to finish playing...
            deviceClosedEvent.Wait(200);
            device.Dispose();
        }

        private void DevicePlaybackStopped(object? sender, StoppedEventArgs e)
        {
            if(e.Exception != null)
            {
                MainViewModel.Log($"Audio device error! \n{e.Exception}", MainViewModel.LogLevel.Error);
            }
            deviceClosedEvent.Set();
        }

        public (object key, string identifier)[] GetOutputDevices(AudioOutputDriver driver)
        {
            switch (driver)
            {
                case AudioOutputDriver.Wave:
                    return Enumerable.Range(0, WaveOut.DeviceCount).Select(x => {
                        var caps = WaveOut.GetCapabilities(x);
                        return ((object)x, $"{x}: {caps.ProductName} ({caps.Channels} channels) (Wave)"); 
                    }).ToArray();
                case AudioOutputDriver.DirectSound:
                    return DirectSoundOut.Devices.Select(x => {
                        return ((object)x.Guid, $"{x.Description} (DirectSound)");
                    }).ToArray();
                case AudioOutputDriver.WASAPI:
                    var enumerator = new MMDeviceEnumerator();
                    return enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).Select(x => {
                        return ((object)x, $"{x.FriendlyName} {x.DeviceFriendlyName} (WASAPI)");
                    }).ToArray();
                case AudioOutputDriver.ASIO:
                    return AsioOut.GetDriverNames().Select(x => {
                        return ((object)x, $"{x} (ASIO)");
                    }).ToArray();
                default:
                    return Array.Empty<(object key, string identifier)>();
            }
        }

        public void OpenOutputDevice(AudioOutputDriver driver, object key)
        {
            CloseAudioDevices();

            try
            {
                switch (driver)
                {
                    case AudioOutputDriver.Wave:
                        device = new WaveOutEvent() { DeviceNumber = (int)key  };
                        break;
                    case AudioOutputDriver.DirectSound:
                        device = new DirectSoundOut((Guid)key);
                        break;
                    case AudioOutputDriver.WASAPI:
                        device = new WasapiOut((MMDevice)key, AudioClientShareMode.Shared, true, 40);
                        break;
                    case AudioOutputDriver.ASIO:
                        device = new AsioOut((string)key);
                        break;
                    default:
                        throw new NotImplementedException($"Unsupported audio driver '{driver}'!");
                }
            } catch(Exception ex)
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
                device.Init(mixer);
                device.Play();
            } catch(Exception ex)
            {
                MainViewModel.Log($"Failed to start device '{key}' with driver '{driver}'.\n" + ex,
                    MainViewModel.LogLevel.Error);
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
        private ISampleProvider ConvertToMixerFormat(ISampleProvider input)
        {
            if(input.WaveFormat.SampleRate != mixer.WaveFormat.SampleRate)
            {
                // Resample
                input = new WdlResamplingSampleProvider(input, mixer.WaveFormat.SampleRate);
            }
            if (input.WaveFormat.Channels == mixer.WaveFormat.Channels)
            {
                return input;
            } else if (input.WaveFormat.Channels == 1 && mixer.WaveFormat.Channels == 2)
            {
                return new MonoToStereoSampleProvider(input);
            }
            throw new NotImplementedException("Not yet implemented this channel count conversion");
        }

        /// <summary>
        /// Starts the playback of a sound through the mixer.
        /// </summary>
        /// <param name="provider">the sample stream to play</param>
        /// <param name="onCompleted">a callback raised when the stream is removed from the mixer</param>
        public void PlaySound(ISampleProvider provider, Action<ISampleProvider>? onCompleted = null)
        {
            try
            {
                if (activeChannels.ContainsKey(provider))
                    return;

                var converted = ConvertToMixerFormat(provider);
                activeChannels.Add(provider, (converted, onCompleted));
                mixer.AddMixerInput(converted);
            } catch (Exception ex)
            {
                MainViewModel.Log("Error while trying to play sound! \n" + ex, MainViewModel.LogLevel.Error);
            }
        }

        /// <summary>
        /// Stops the playback of a sound stream.
        /// </summary>
        /// <param name="provider">the sample stream to stop</param>
        public void StopSound(ISampleProvider provider)
        {
            try
            {
                if (activeChannels.TryGetValue(provider, out var channel))
                {
                    mixer.RemoveMixerInput(channel.convertedStream);
                    activeChannels.Remove(provider);
                }
            } catch (Exception ex)
            {
                MainViewModel.Log("Error while trying to stop sound! \n" + ex, MainViewModel.LogLevel.Error);
            }
        }

        /// <summary>
        /// Gets whether a sound stream is currently playing.
        /// </summary>
        /// <param name="provider"></param>
        /// <returns></returns>
        public bool IsPlaying(ISampleProvider provider) => activeChannels.ContainsKey(provider);

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
}
