using QPlayer.VideoPlugin.Rendering;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;

namespace QPlayer.VideoPlugin;

public class VideoObject
{
    private readonly VertexArrayObject<float, int> mesh;
    private readonly Material mat;
    private VideoDecoder? decoder;
    private readonly ConcurrentQueue<VideoDecoder> videoFrames;
    private readonly VideoCueViewModel vm;

    public float ZIndex { get; private set; }
}
