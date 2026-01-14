using CommunityToolkit.Mvvm.ComponentModel;
using QPlayer.SourceGenerator;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace QPlayer.ViewModels;

public partial class ProgressBoxViewModel : ObservableObject
{
    [Reactive] private Visibility visible = Visibility.Collapsed;
    [Reactive] private string message = "Loading...";
    [Reactive] private float progress;
}
