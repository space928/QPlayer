using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Automation.Peers;

namespace QPlayer.Views;

internal class NullAutomationPeer(FrameworkElement owner, string name = "Control", AutomationControlType type = AutomationControlType.Custom) : FrameworkElementAutomationPeer(owner)
{
    private static readonly List<AutomationPeer> emptyPeers = [];

    private readonly string name = name;
    private readonly AutomationControlType type = type;

    protected override string GetNameCore() => name;

    protected override AutomationControlType GetAutomationControlTypeCore() => type;
    
    protected override List<AutomationPeer> GetChildrenCore() => emptyPeers;
}
