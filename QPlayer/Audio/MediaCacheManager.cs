using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QPlayer.Audio;

internal class MediaCacheManager
{
    private ConcurrentDictionary<string, CachedMediaStream> cacheDict;

    public MediaCacheManager()
    {
        cacheDict = new();
    }
}

internal class CachedMediaStream : MemoryStream, IDisposable
{
    protected int references = 0;

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
    }

    void IDisposable.Dispose() 
    {
        
    }
}
