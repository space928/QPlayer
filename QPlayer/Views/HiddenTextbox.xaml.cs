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
    private Binding? defaultPreviewTextBinding = null;

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
        //DefaultUpdateSourceTrigger = UpdateSourceTrigger.LostFocus,
        //PropertyChangedCallback = TextPropertyChanged
    });

    public string? PreviewText
    {
        get { return (string)GetValue(PreviewTextProperty); }
        set { SetValue(PreviewTextProperty, value); }
    }

    // Using a DependencyProperty as the backing store for PreviewText.  This enables animation, styling, binding, etc...
    public static readonly DependencyProperty PreviewTextProperty =
        DependencyProperty.Register(nameof(PreviewText), typeof(string), typeof(HiddenTextbox), new PropertyMetadata(null));

    public bool CanEdit
    {
        get { return (bool)GetValue(CanEditProperty); }
        set { SetValue(CanEditProperty, value); }
    }

    // Using a DependencyProperty as the backing store for CanEdit.  This enables animation, styling, binding, etc...
    public static readonly DependencyProperty CanEditProperty =
        DependencyProperty.Register(nameof(CanEdit), typeof(bool), typeof(HiddenTextbox), new PropertyMetadata(true));


    private void Edit()
    {
        if (!CanEdit || editing)
            return;
        editing = true;
        TextFieldInst.Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () =>
        {
            TextFieldInst.TextBox.Focus();
            TextFieldInst.TextBox.SelectAll();
        });
    }

    private void Label_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        Edit();
    }

    private void Label_MouseDown(object sender, MouseButtonEventArgs e)
    {

    }

    private void TextBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        editing = false;
        TextFieldInst.Visibility = Visibility.Collapsed;
    }

    private void HiddenTextboxInst_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        Edit();
    }

    private void HiddenTextboxInst_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (BindingOperations.GetBinding(this, PreviewTextProperty) is Binding binding
            && binding != defaultPreviewTextBinding)
        {
            // If the previewText has a valid binding, don't override it.
            return;
        }

        // Make a new default binding
        defaultPreviewTextBinding = new(nameof(Text));
        defaultPreviewTextBinding.Source = this;
        BindingOperations.SetBinding(this, PreviewTextProperty, defaultPreviewTextBinding);
    }
}
