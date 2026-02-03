using QPlayer.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace QPlayer.OSCCuePlugin;

public record OSCCueModel : Cue
{
    public OSCCueModel() : base() { }

    public string command = string.Empty;
}
