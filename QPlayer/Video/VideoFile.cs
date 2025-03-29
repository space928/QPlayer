using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QPlayer.Video;

public class VideoFile
{
    public string? Path { get; set; }
    public TimeSpan Duration { get; set; }
    public (int width, int height) Resolution { get; set; }
    public float FrameRate { get; set; }

    public TimeSpan CurrentTime { get; set; }
    public bool IsReady { get; set; }
}
