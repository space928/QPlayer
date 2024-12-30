using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace QPlayer.Views;

/// <summary>
/// Interaction logic for HiddenTextbox.xaml
/// </summary>
public partial class HiddenTextbox : UserControl
{
    private bool editing = false;

    public bool IsEditing => editing;

    public HiddenTextbox()
    {
        InitializeComponent();
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(HiddenTextbox), new FrameworkPropertyMetadata
    {
        BindsTwoWayByDefault = true,
        DefaultUpdateSourceTrigger = UpdateSourceTrigger.LostFocus
    });

    private void Label_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        editing = true;
        TextFieldInst.Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, TextFieldInst.TextBox.Focus);
    }

    private void Label_MouseDown(object sender, MouseButtonEventArgs e)
    {

    }

    private void TextBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        editing = false;
        TextFieldInst.Visibility = Visibility.Collapsed;
    }
}
