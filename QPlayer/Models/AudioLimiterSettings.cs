using System;
using System.Collections.Generic;
using System.Text;

namespace QPlayer.Models;

public record AudioLimiterSettings
{
    public bool enabled;
    public float inputGain;
    public float threshold;
    public float attack;
    public float release;
}
