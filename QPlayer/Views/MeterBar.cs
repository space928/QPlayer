using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Threading;
using Expression = System.Linq.Expressions.Expression;

namespace QPlayer.Views;

/// <summary>
/// A progress bar which is a little more GC friendly. This is mostly just a hack to prevent WPF from allocating so much garbage when updating this frequently.
/// </summary>
public class MeterBar : ProgressBar
{
    private static readonly Action? clearAutomAction;

    static MeterBar()
    {
        clearAutomAction = GenerateClearAutomationEventsFunc();
    }

    protected override AutomationPeer? OnCreateAutomationPeer() => new NullAutomationPeer(this, "ProgressBar", AutomationControlType.ProgressBar);

    protected override Size MeasureOverride(Size constraint)
    {
        ClearAutomationEvents();
        return base.MeasureOverride(constraint);
    }

    private static void ClearAutomationEvents()
    {
        clearAutomAction?.Invoke();
    }

    private static Action? GenerateClearAutomationEventsFunc()
    {
        // Use reflection and expression trees to generate a function which clears the internal automation event list.
        var assembly = typeof(UIElement).Assembly;
        var clmType = assembly.GetType("System.Windows.ContextLayoutManager", false);
        var eventListType = assembly.GetType("System.Windows.LayoutEventList", false);
        var eventItemType = eventListType?.GetNestedType("ListItem", BindingFlags.NonPublic);
        if (clmType == null || eventListType == null || eventItemType == null)
            return null;

        var fromMethod = clmType.GetMethod("From", BindingFlags.Static | BindingFlags.NonPublic);
        var getAutomEventList = clmType.GetProperty("AutomationEvents", BindingFlags.Instance | BindingFlags.NonPublic);
        var removeItemMethod = eventListType.GetMethod("Remove", BindingFlags.Instance | BindingFlags.NonPublic);
        var headField = eventListType.GetField("_head", BindingFlags.Instance | BindingFlags.NonPublic);
        var nextField = eventItemType.GetField("Next", BindingFlags.Instance | BindingFlags.NonPublic);
        var curDispatcher = typeof(Dispatcher).GetProperty(nameof(Dispatcher.CurrentDispatcher), BindingFlags.Public | BindingFlags.Static);
        if (fromMethod == null || getAutomEventList == null || removeItemMethod == null
            || headField == null || nextField == null || curDispatcher == null)
            return null;

        // The expression tree below implements the following code:
        /*
        var automList = ContextLayoutManager.From(Dispatcher.CurrentDispatcher).AutomationEvents;
        var item = automList._head;
        while (true)
        {
            if (item == null)
                return;
            automList.Remove(item);
            item = item.Next;
        }
        */

        var automList = Expression.Variable(eventListType);
        var item = Expression.Variable(eventItemType);
        var breakTarget = Expression.Label();

        var getDisptcher = Expression.Property(null, curDispatcher);
        var clm = Expression.Call(fromMethod, getDisptcher);
        var getAutomList = Expression.Property(clm, getAutomEventList);
        var getHead = Expression.Field(automList, headField);
        var getNext = Expression.Field(item, nextField);

        var body = Expression.Block([automList, item],
            Expression.Assign(automList, getAutomList),
            Expression.Assign(item, getHead),
            Expression.Loop(Expression.Block(
                Expression.IfThen(Expression.Equal(item, Expression.Constant(null)),
                    Expression.Break(breakTarget)),
                Expression.Call(automList, removeItemMethod, item),
                Expression.Assign(item, getNext)
            ), breakTarget)
        );
        var func = (Action)Expression.Lambda(body).Compile();

        return func;
    }
}
