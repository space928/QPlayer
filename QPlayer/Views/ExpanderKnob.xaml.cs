using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace QPlayer.Views
{
    /// <summary>
    /// Interaction logic for ExpanderKnob.xaml
    /// </summary>
    public partial class ExpanderKnob : UserControl
    {
        public bool IsCollapsed
        {
            get { return (bool)GetValue(IsExpandedProperty); }
            set { SetValue(IsExpandedProperty, value); }
        }

        // Using a DependencyProperty as the backing store for IsExpanded.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty IsExpandedProperty =
            DependencyProperty.Register(nameof(IsCollapsed), typeof(bool), typeof(ExpanderKnob), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, IsCollapsedChanged));

        public ExpanderKnob()
        {
            InitializeComponent();
        }

        private void Grid_MouseDown(object sender, MouseButtonEventArgs e)
        {
            IsCollapsed ^= true;
        }

        private void UpdateVisual()
        {
            // At 0 degrees, the knob has a downwards (expanded) arrow
            // At -90 degrees, the knob points right (collapsed)
            ArrowRotateTransform.Angle = IsCollapsed ? -90 : 0;
        }

        private static void IsCollapsedChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
        {
            if (target is not ExpanderKnob inst)
                return;

            inst.UpdateVisual();
        }

        private void Grid_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateVisual();
        }
    }
}
