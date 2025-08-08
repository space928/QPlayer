using CommunityToolkit.Mvvm.ComponentModel;
using ReactiveUI.Fody.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace QPlayer.ViewModels;

public class ProgressBoxViewModel : ObservableObject
{
    [Reactive] public Visibility Visible { get; set; } = Visibility.Collapsed;
    [Reactive] public string Message { get; set; } = "Loading...";
    [Reactive] public float Progress { get; set; }
}
