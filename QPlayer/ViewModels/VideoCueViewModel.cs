using QPlayer.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QPlayer.ViewModels;

public class VideoCueViewModel : CueViewModel, IConvertibleModel<Cue, CueViewModel>, IDisposable
{
    public VideoCueViewModel(MainViewModel mainViewModel) : base(mainViewModel)
    {

    }

    public void Dispose()
    {
        throw new NotImplementedException();
    }
}
