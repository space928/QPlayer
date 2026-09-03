using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace QPlayer.Views;

internal class CueListItemsControl : ItemsControl
{
    protected override AutomationPeer OnCreateAutomationPeer() => new CueListItemsControlAutomationPeer(this);
}

internal class CueListItemsControlAutomationPeer : FrameworkElementAutomationPeer//, IItemContainerProvider
{
    public CueListItemsControlAutomationPeer(FrameworkElement owner) : base(owner)
    {

    }

    /*protected override string GetNameCore()
    {
        var name = base.GetNameCore();
        if (string.IsNullOrEmpty(name))
            name = DataContext?.NamePreview ?? string.Empty;
        return name;
    }*/

    protected override string GetClassNameCore() => "CueListItemsControl";

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.List;

    public override object? GetPattern(PatternInterface patternInterface)
    {
        return patternInterface switch
        {
            //PatternInterface.ItemContainer => this,
            PatternInterface.Scroll => GetScrollProvider(),
            _ => base.GetPattern(patternInterface),
        };
    }

    private IScrollProvider? GetScrollProvider()
    {
        var itemsControl = (CueListItemsControl)Owner;
        if (GetScrollHost(itemsControl) is not ScrollViewer scroll)
            return null;

        AutomationPeer automationPeer = CreatePeerForElement(scroll);
        if (automationPeer is not IScrollProvider provider)
            return null;

        automationPeer.EventsSource = this;
        return provider;
    }

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_ScrollHost")]
    private static extern ScrollViewer GetScrollHost(ItemsControl control);

    protected override List<AutomationPeer> GetChildrenCore()
    {
        List<AutomationPeer> peers = [];
        var control = (CueListItemsControl)Owner;
        var count = control.Items.Count;
        var generator = control.ItemContainerGenerator;
        for (int i = 0; i < count; i++)
        {
            var container = generator.ContainerFromIndex(i);
            if (VisualTreeHelper.GetChildrenCount(container) == 0)
                continue;

            if (VisualTreeHelper.GetChild(container, 0) is not CueDataControl child)
                continue;

            if ((FromElement(child) ?? CreatePeerForElement(child)) is not AutomationPeer peer)
                continue;

            peers.Add(peer);
        }

        return peers;
    }

    /*public IRawElementProviderSimple? FindItemByProperty(IRawElementProviderSimple startAfter, int propertyId, object value)
    {
        if (startAfter == null)
            return null;

        var control = (CueListItemsControl)Owner;
        var items = control.Items;
        var count = items.Count;
        var generator = control.ItemContainerGenerator;

        var peer = PeerFromProvider(startAfter) as ItemAutomationPeer;
        if (peer?.Item is not object item)
            return null;

        int start = items.IndexOf(item);
        if (start == -1 || start >= count - 1)
            return null;

        if (propertyId == 0)
        {
            if (items[start] is not UIElement elem)
                return null;
            if ((FromElement(elem) ?? CreatePeerForElement(elem)) is AutomationPeer childPeer)
                return ProviderFromPeer(childPeer);
            return null;
        }

        object obj = null;
        for (int j = num; j < itemCollection.Count; j++)
        {
            ItemAutomationPeer itemAutomationPeer2 = FindOrCreateItemAutomationPeer(itemCollection[j]);
            if (itemAutomationPeer2 == null)
            {
                continue;
            }

            try
            {
                peer.getprop
                obj = GetSupportedPropertyValue(itemAutomationPeer2, propertyId);
            }
            catch (Exception ex)
            {
                if (ex is ElementNotAvailableException)
                {
                    continue;
                }
            }

            if (value == null || obj == null)
            {
                if (obj == null && value == null && itemCollection.IndexOf(itemCollection[j]) == j)
                {
                    return ProviderFromPeer(itemAutomationPeer2);
                }
            }
            else if (value.Equals(obj) && itemCollection.IndexOf(itemCollection[j]) == j)
            {
                return ProviderFromPeer(itemAutomationPeer2);
            }
        }
    }

        return null;
    }*/
}
