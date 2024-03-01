using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace QPlayer.ViewModels
{
    internal class FadingSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider source;
        private readonly object lockObj = new();
        private FadeState state;
        private long fadeTime;
        private long fadeDuration;
        private float startVolume = 1;
        private float endVolume = 1;
        private FadeType fadeType;
        private Action<bool>? onCompleteAction;
        private SynchronizationContext? synchronizationContext;

        public FadingSampleProvider(ISampleProvider source, bool startSilent = false)
        {
            this.source = source;
            state = FadeState.Ready;
            if (startSilent)
                startVolume = 0;
            else
                startVolume = 1;
        }

        public WaveFormat WaveFormat => source.WaveFormat;

        public float Volume
        {
            get => startVolume;
            set => startVolume = value;
        }

        public int Read(float[] buffer, int offset, int count)
        {
            int numSource = source.Read(buffer, offset, count);
            int num = numSource;
            lock (lockObj)
            {
                if (state == FadeState.Fading)
                {
                    int numFaded = FadeSamples(buffer, offset, numSource);
                    offset += numFaded;
                    num -= numFaded;
                }

                if (startVolume == 0)
                {
                    Array.Clear(buffer, offset, num);
                    return numSource;
                } else if (startVolume == 1)
                {
                    return numSource;
                }

                for (int x = offset; x < offset + num; x++)
                    buffer[x] *= startVolume;
            }

            return numSource;
        }

        /// <summary>
        /// Starts a new fade operation, cancelling any active fade operation.
        /// </summary>
        /// <param name="volume">The volume to fade to</param>
        /// <param name="durationMS">The time to fade over in milliseconds</param>
        /// <param name="fadeType">The type of fade to use</param>
        /// <param name="onComplete">Optionally, an event to raise when the fade is completed. <c>true</c> is passed to the 
        /// event handler if the fade completed normally, <c>false</c> if it was cancelled. The event is invoked on the 
        /// thread that called this method.</param>
        public void BeginFade(float volume, double durationMS, FadeType fadeType = FadeType.Linear, Action<bool>? onComplete = null) 
        {
            lock (lockObj)
            {
                EndFade();

                fadeTime = 0;
                fadeDuration = (int)(durationMS * source.WaveFormat.SampleRate / 1000.0);
                endVolume = volume;
                this.fadeType = fadeType;
                onCompleteAction = onComplete;
                synchronizationContext = SynchronizationContext.Current;
                state = FadeState.Fading;
            }
        }

        /// <summary>
        /// Cancels the active fade operation.
        /// </summary>
        public void EndFade()
        {
            if (state != FadeState.Fading)
                return;

            lock (lockObj)
            {
                state = FadeState.Ready;
                float t = GetFadeFraction();
                startVolume = endVolume * t + startVolume * (1 - t);
                if (synchronizationContext != null)
                    synchronizationContext.Post(x => onCompleteAction?.Invoke(false), null);
                else
                    onCompleteAction?.Invoke(false);
                //onCompleteAction = null;
                //synchronizationContext = null;
            }
        }

        private int FadeSamples(float[] buffer, int offset, int count)
        {
            int i = 0;
            int channels = source.WaveFormat.Channels;
            while (i < count)
            {
                if (fadeTime >= fadeDuration)
                {
                    startVolume = endVolume;
                    state = FadeState.Ready;
                    if (synchronizationContext != null)
                        synchronizationContext.Post(x => onCompleteAction?.Invoke(true), null);
                    else
                        onCompleteAction?.Invoke(true);
                    //onCompleteAction = null;
                    //synchronizationContext = null;

                    break;
                }

                float t = GetFadeFraction();

                for (int c = 0; c < channels; c++)
                    buffer[offset + i + c] *= endVolume * t + startVolume * (1 - t);

                i += channels;
                fadeTime++;
            }

            return i;
        }

        private float GetFadeFraction()
        {
            float t = fadeTime / (float)fadeDuration;
            switch (fadeType)
            {
                case FadeType.SCurve:
                    // Cubic hermite spline
                    float t2 = t * t;
                    float t3 = t2 * t;
                    t = -2 * t3 + 3 * t2;
                    break;
                case FadeType.Square:
                    t *= t;
                    break;
                case FadeType.InverseSquare:
                    t = MathF.Sqrt(t);
                    break;
                case FadeType.Linear:
                default:
                    break;
            }
            return t;
        }
    }

    internal enum FadeState
    {
        Ready, 
        Fading
    }

    public enum FadeType
    {
        Linear,
        SCurve,
        Square,
        InverseSquare,
    }
}
