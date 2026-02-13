using System;
using System.Numerics;

namespace QPlayer.Audio;

public class DelayBuffer
{
    private readonly float[] delayBuff;
    protected readonly int maxDelayTime;
    private int headPos;

    public DelayBuffer(uint maxDelayTime)
    {
        this.maxDelayTime = (int)maxDelayTime;
        delayBuff = new float[BitOperations.RoundUpToPowerOf2(maxDelayTime) << 1];
    }

    public Span<float> GetDelayed(int count, int delaySamples, out Span<float> secondHalf)
    {
        // Rewind the head based on how much we're delaying and how many samples we need to retrieve
        int pos = headPos - delaySamples - count;
        int available = headPos - pos;
        pos &= delayBuff.Length - 1; // Wrap the position
        int toRead = Math.Min(count, available);
        secondHalf = default;

        int firstHalf = delayBuff.Length - pos;
        if (toRead >= firstHalf)
        {
            // Split span
            secondHalf = delayBuff.AsSpan(0, toRead - firstHalf);
            return delayBuff.AsSpan(pos, firstHalf);
        }
        else
        {
            // Contiguous span
            return delayBuff.AsSpan(pos, toRead);
        }
    }

    /// <summary>
    /// Pushes the span of samples into this delay buffer.
    /// </summary>
    /// <param name="values"></param>
    public void Push(ReadOnlySpan<float> values)
    {
        int firstHalf = delayBuff.Length - headPos;
        if (values.Length >= firstHalf)
        {
            // Split copy
            values[..firstHalf].CopyTo(delayBuff.AsSpan(headPos, firstHalf));
            values[firstHalf..].CopyTo(delayBuff.AsSpan(0));
        }
        else
        {
            // Contiguous copy
            values.CopyTo(delayBuff.AsSpan(headPos));
        }
        headPos = (headPos + values.Length) & (delayBuff.Length - 1);
    }
}