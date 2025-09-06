using QPlayer.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace QPlayer.Views;

/// <summary>
/// Interaction logic for WaveFormWindow.xaml
/// </summary>
public partial class WaveFormWindow : Window
{
    public WaveFormWindow(MainViewModel? vm, InputBindingCollection inputBindings)
    {
        InitializeComponent();
        // Convert the input bindings from from the MainWindow to this window
        foreach (var binding in inputBindings.Cast<InputBinding>())
        {
            var newBinding = (InputBinding)binding.Clone();
            var expr = BindingOperations.GetBindingExpression(newBinding, InputBinding.CommandProperty);
            var command = new Binding();
            command.Source = vm;
            command.Path = expr.ParentBinding.Path;
            BindingOperations.SetBinding(newBinding, InputBinding.CommandProperty, command);
            InputBindings.Add(newBinding);
        }
    }
}
