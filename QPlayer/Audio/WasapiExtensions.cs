using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;
using QPlayer.ViewModels;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace QPlayer.Audio;

public class WasapiExtensions(WasapiOut wasapi)
{
    public AudioClient AudioClient => Accessor.GetAudioClient(wasapi);
    public IAudioClient2? AudioClientInterface => Accessor.GetIAudioClient(AudioClient) as IAudioClient2;

    public bool SetStreamProperties(AudioClientProperties properties)
    {
        var ptr = Marshal.AllocCoTaskMem(Marshal.SizeOf<AudioClientProperties>());
        if (ptr == 0)
            return false;
        try
        {
            Marshal.StructureToPtr(properties, ptr, false);
            AudioClientInterface?.SetClientProperties(ptr);
        }
        catch (Exception ex)
        {
            MainViewModel.Log($"Couldn't set WASAPI client properties: {ex.Message}", MainViewModel.LogLevel.Warning);
        }
        finally
        {
            Marshal.FreeCoTaskMem(ptr);
        }
        return true;
    }

    private static class Accessor
    {
        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "audioClient")]
        public static extern ref AudioClient GetAudioClient(WasapiOut inst);
        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "audioClientInterface")]
        public static extern ref IAudioClient GetIAudioClient(AudioClient inst);
    }
}
