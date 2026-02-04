using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QPlayer.ViewModels;

public static class UndoManager
{
    /// <summary>
    /// 
    /// </summary>
    /// <param name="path"></param>
    /// <param name="oldValue"></param>
    private static void RegisterAction(string path, object oldValue)
    {
        // Path examples:
        // Cues[qid].Volume
        // ProjectSettings.RXPort
    }
}
