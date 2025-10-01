using System;
using System.Collections;
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
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace QPlayer.Views;

/// <summary>
/// Interaction logic for HiddenComboBox.xaml
/// </summary>
public partial class HiddenComboBox : UserControl
{
    private bool editing = false;

    public bool IsEditing => editing;

    public HiddenComboBox()
    {
        InitializeComponent();
    }

    public object InactiveContent
    {
        get { return (object)GetValue(InactiveContentProperty); }
        set { SetValue(InactiveContentProperty, value); }
    }

    // Using a DependencyProperty as the backing store for InactiveContent.  This enables animation, styling, binding, etc...
    public static readonly DependencyProperty InactiveContentProperty =
        DependencyProperty.Register("InactiveContent", typeof(object), typeof(HiddenComboBox), new PropertyMetadata(null));

    public DataTemplate InactiveContentTemplate
    {
        get { return (DataTemplate)GetValue(InactiveContentTemplateProperty); }
        set { SetValue(InactiveContentTemplateProperty, value); }
    }

    // Using a DependencyProperty as the backing store for InactiveContentTemplate.  This enables animation, styling, binding, etc...
    public static readonly DependencyProperty InactiveContentTemplateProperty =
        DependencyProperty.Register("InactiveContentTemplate", typeof(DataTemplate), typeof(HiddenComboBox), new PropertyMetadata(null));

    public IEnumerable ItemsSource
    {
        get { return (IEnumerable)GetValue(ItemsSourceProperty); }
        set { SetValue(ItemsSourceProperty, value); }
    }

    // Using a DependencyProperty as the backing store for ItemsSource.  This enables animation, styling, binding, etc...
    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register("ItemsSource", typeof(IEnumerable), typeof(HiddenComboBox), new PropertyMetadata(Enumerable.Empty<object>()));

    public int SelectedIndex
    {
        get { return (int)GetValue(SelectedIndexProperty); }
        set { SetValue(SelectedIndexProperty, value); }
    }

    // Using a DependencyProperty as the backing store for SelectedIndex.  This enables animation, styling, binding, etc...
    public static readonly DependencyProperty SelectedIndexProperty =
        DependencyProperty.Register("SelectedIndex", typeof(int), typeof(HiddenComboBox), new PropertyMetadata(0));

    private void Edit()
    {
        if (editing)
            return;

        editing = true;
        ComboBoxInst.Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () =>
        {
            ComboBoxInst.Focus();
            ComboBoxInst.IsDropDownOpen = true;
        });
    }

    private void Close()
    {
        editing = false;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () =>
        {
            ComboBoxInst.IsDropDownOpen = false;
            ComboBoxInst.Visibility = Visibility.Collapsed;
        });
        //ComboBoxInst.Visibility = Visibility.Collapsed;
    }

    private void Label_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        Edit();
    }

    private void Label_MouseDown(object sender, MouseButtonEventArgs e)
    {

    }

    private void ComboBoxInst_DropDownClosed(object sender, EventArgs e)
    {
        Close();
    }

    private void Grid_IsEnabledChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if ((bool)e.NewValue == false)
            Close();
    }

    private void HiddenComboBoxInst_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender == this)
            return;

        Edit();
    }
}
