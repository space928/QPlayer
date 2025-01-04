using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QPlayer.Audio;

internal class EQSampleProvider : ISampleProvider
{
    public WaveFormat WaveFormat => throw new NotImplementedException();

    public int Read(float[] buffer, int offset, int count)
    {
        throw new NotImplementedException();
    }
}
