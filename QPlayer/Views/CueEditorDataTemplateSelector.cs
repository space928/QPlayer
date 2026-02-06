using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace QPlayer.Views;

class CueEditorDataTemplateSelector : DataTemplateSelector
{
    public FrameworkElement? TemplateSource { get; set; }
    private static Dictionary<string, DataTemplate> templateCache = [];

    public override DataTemplate SelectTemplate(object item, DependencyObject container)
    {
        if (item == null || container == null)
            return base.SelectTemplate(item, container);

        var itemType = item.GetType();

        if (templateCache.TryGetValue(itemType.FullName ?? string.Empty, out var cached))
            return cached;

        DataTemplate? template = null;
        var fe = (FrameworkElement)container;
        while (fe != null && template == null)
        {
            foreach (var res in fe.Resources.Values)
            {
                if (res is DataTemplate dt && itemType == (Type)dt.DataType)
                {
                    template = dt;
                    break;
                }
            }
            fe = fe.TemplatedParent as FrameworkElement;
        }
        if (template != null && !template.IsSealed)
            template.Seal();

        if (template != null)
            templateCache.Add(itemType.FullName ?? string.Empty, template);

        return template ?? base.SelectTemplate(item, container);
    }
}
