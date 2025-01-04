using FFmpeg.AutoGen;
using QPlayer.VideoPlugin.Rendering;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;

namespace QPlayer.VideoPlugin;

public unsafe class VideoDecoder
{
    public event Action<Exception>? OnFailure;

    private readonly Thread thread;
    private readonly string filePath;
    private readonly ConcurrentQueue<VideoFrame> destination;

    public VideoDecoder(string sourceFile, ConcurrentQueue<VideoFrame> destination)
    {
        this.filePath = sourceFile;
        this.destination = destination;

        thread = new Thread(DecoderRun);
        thread.IsBackground = true;
        thread.Name = $"VideoDecoder - {sourceFile}";
        thread.Priority = ThreadPriority.Normal;
        thread.Start();
    }

    private void DecoderRun()
    {
        AVPacket* pkt = null;
        AVFormatContext* fmtCtx = null;
        try
        {
            using var file = new BinaryReader(new FileStream(filePath, FileMode.Open));

            ThrowOnError(ffmpeg.avformat_open_input(&fmtCtx, filePath, null, null));
            // ffmpeg.av_dump_format();

            ThrowOnNull(pkt = ffmpeg.av_packet_alloc());

            int res;
            while ((res = ffmpeg.av_read_frame(fmtCtx, pkt)) >= 0)
            {
                ThrowOnError(res, "Failed to read frame.");
                // if (pkt->stream_index ==)
            }
        }
        catch (Exception e)
        {
            OnFailure?.Invoke(e);
        }
        finally
        {
            if (pkt != null)
                ffmpeg.av_packet_free(&pkt);
            if (fmtCtx != null)
                ffmpeg.avformat_free_context(fmtCtx);
        }
    }

    private static void ThrowOnError(int result, string? message = null, [CallerArgumentExpression(nameof(result))] string? expr = null)
    {
        if (result < 0)
            throw new Exception(message == null ? $"Failure in '{expr}' Result code: {result}" : (message + $"  / Result code: {result}"));
    }

    private static void ThrowOnNull(void* result, string? message = null, [CallerArgumentExpression(nameof(result))] string? expr = null)
    {
        if (result == null)
            throw new Exception(message ?? $"Failure in '{expr}' Result was null");
    }
}

public struct VideoFrame
{

}
