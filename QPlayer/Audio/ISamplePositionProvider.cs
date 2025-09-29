using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QPlayer.Audio;

/// <summary>
/// A <see cref="ISampleProvider"/> that also provides the position within the sample stream.
/// </summary>
public interface ISamplePositionProvider : ISampleProvider
{
    /// <summary>
    /// The position, in samples, within the sample provider stream.
    /// </summary>
    public long Position { get; set; }
}
