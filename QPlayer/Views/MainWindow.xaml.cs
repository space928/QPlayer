using System;
using System.Collections.Generic;
using System.Diagnostics;
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
using System.Windows.Navigation;
using System.Windows.Shapes;
using QPlayer.ViewModels;

namespace QPlayer
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private Dictionary<(Key key, ModifierKeys modifiers), KeyBinding> keyBindings = new();

        public MainWindow()
        {
            InitializeComponent();
        }

        public void Window_Loaded(object sender, RoutedEventArgs e)
        {
            keyBindings.Clear();
            foreach (object binding in InputBindings)
                if(binding is KeyBinding keyBinding)
                    keyBindings.Add((keyBinding.Key, keyBinding.Modifiers), keyBinding);
        }

        public void Window_Closed(object sender, EventArgs e)
        {
            ((MainViewModel)DataContext).OnExit();
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            switch(e.Key)
            {
                case Key.Space:
                case Key.Up:
                case Key.Down:
                    e.Handled = true;
                    var mods = Keyboard.Modifiers;
                    if (keyBindings.TryGetValue((e.Key, mods), out var binding))
                        binding.Command.Execute(null);
                    break;
            }
        }
    }
}
