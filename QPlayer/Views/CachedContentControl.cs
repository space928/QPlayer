using System;
using System.Collections.Generic;
using System.Reflection.Metadata;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace QPlayer.Views;

public class CachedContentControl : ContentControl
{
    private Dictionary<string, FrameworkElement> cachedViews = [];

    public CachedContentControl() : base()
    {

    }

    public object? VMContent
    {
        get => (object?)GetValue(VMContentProperty);
        set { SetValue(VMContentProperty, value); }
    }

    // Using a DependencyProperty as the backing store for VMContent.  This enables animation, styling, binding, etc...
    public static readonly DependencyProperty VMContentProperty =
        DependencyProperty.Register(nameof(VMContent), typeof(object), typeof(CachedContentControl), new PropertyMetadata(null) { PropertyChangedCallback = VMContentChanged });

    private static void VMContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var cc = (CachedContentControl)d;
        var content = cc.VMContent;

        // Try to get a cached view for this content type
        string? contentType = content?.GetType()?.FullName;
        if (contentType != null
            && cc.cachedViews.TryGetValue(contentType, out var cachedView))
        {
            cachedView.DataContext = content;
            cc.Content = cachedView;
            return;
        } 

        // Otherwise, find the corresponding DataTemplate and construct it
        var dt = cc.ContentTemplateSelector.SelectTemplate(content, cc);
        if (dt == null)
        {
            cc.Content = null;
            return;
        }

        var view = (FrameworkElement)dt.LoadContent();
        view.DataContext = content;

        // Cache the constructed view
        if (contentType != null)
            cc.cachedViews.TryAdd(contentType, view);

        cc.Content = view;
    }
}
