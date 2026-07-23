using QPlayer.Utilities;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace QPlayer.Views;

/// <summary>
/// A <see cref="StackPanel"/> with better support for virtualization. Based on the Microsoft <see cref="StackPanel"/> implementation.
/// </summary>
/// <remarks>
/// This is only really designed and tested for the log viewer, and is probably a bit broken due to the complexity of implementing a custom <see cref="Panel"/>.
/// </remarks>
public class VirtualStackPanel : VirtualizingPanel, IScrollInfo
{
    private readonly ScrollData scrollData = new();
    private bool invalidateChildren = true;
    private ScrollViewer? scrollHost;
    private ItemsControl? itemsOwner;
    private double itemLength;
    private int prevFirstVisible;

    public Orientation Orientation
    {
        get { return (Orientation)GetValue(OrientationProperty); }
        set { SetValue(OrientationProperty, value); }
    }

    // Using a DependencyProperty as the backing store for Orientation.  This enables animation, styling, binding, etc...
    public static readonly DependencyProperty OrientationProperty =
        DependencyProperty.Register(nameof(Orientation), typeof(Orientation), typeof(VirtualStackPanel), new FrameworkPropertyMetadata(Orientation.Vertical, FrameworkPropertyMetadataOptions.AffectsMeasure, OnOrientationChanged));

    protected override bool HasLogicalOrientation => true;
    protected override Orientation LogicalOrientation => Orientation;
    public bool CanHorizontallyScroll
    {
        get => scrollData.allowHorizontal;
        set => scrollData.allowHorizontal = value;
    }
    public bool CanVerticallyScroll
    {
        get => scrollData.allowVertical;
        set => scrollData.allowVertical = value;
    }
    public double ExtentWidth => scrollData.extent.Width;
    public double ExtentHeight => scrollData.extent.Height;
    public double ViewportWidth => scrollData.viewport.Width;
    public double ViewportHeight => scrollData.viewport.Height;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double HorizontalOffset => scrollData.computedOffset.X;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double VerticalOffset => scrollData.computedOffset.Y;
    public ScrollViewer? ScrollOwner
    {
        get => scrollData.scrollOwner;
        set
        {
            if (scrollData.scrollOwner != value)
            {
                scrollData.scrollOwner = value;
                ResetScrolling();
            }
        }
    }
    protected override bool CanHierarchicallyScrollAndVirtualizeCore => true;

    private bool IsScrolling => scrollData.scrollOwner != null;
    private bool CanMouseWheelVerticallyScroll => SystemParameters.WheelScrollLines > 0;
    private int FirstVisibleItem
    {
        get
        {
            if (scrollHost == null || itemsOwner == null)
                return 0;

            var pos = Orientation == Orientation.Horizontal ? scrollHost.HorizontalOffset : scrollHost.VerticalOffset;
            pos /= itemLength;
            return Math.Max(0, (int)Math.Floor(pos));
        }
    }
    private double FirstVisibleOffset => FirstVisibleItem * itemLength;
    private int LastVisibleItem
    {
        get
        {
            if (scrollHost == null || itemsOwner == null)
                return 0;

            var pos = Orientation == Orientation.Horizontal ? scrollHost.HorizontalOffset : scrollHost.VerticalOffset;
            pos += Orientation == Orientation.Horizontal ? scrollHost.ViewportWidth : scrollHost.ViewportHeight;
            pos /= itemLength;
            return (int)Math.Ceiling(pos) + 1;
        }
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new NullAutomationPeer(this, nameof(VirtualStackPanel), AutomationControlType.List); //base.OnCreateAutomationPeer();

    private void ResetVirtualChildren()
    {
        var prevScrollHost = scrollHost;
        scrollHost = FindScrollHost();
        //ScrollOwner = scrollHost;
        itemsOwner = ItemsControl.GetItemsOwner(this);
        OnClearChildren();
        itemLength = MeasureFirstChild() + 4; // Magic number, no idea why it's needed
        //GenerateChildren();
        scrollHost?.ScrollChanged += ScrollHost_ScrollChanged;
        scrollHost?.SizeChanged += ScrollHost_SizeChanged;
        prevScrollHost?.ScrollChanged -= ScrollHost_ScrollChanged;
        prevScrollHost?.SizeChanged -= ScrollHost_SizeChanged;
    }

    private void ScrollHost_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        InvalidateMeasure();
    }

    private void ScrollHost_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        InvalidateMeasure();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        bool isVertical = Orientation == Orientation.Vertical;
        Size size = default;
        var constraint = availableSize;

        // Ensure the children are generated
        if (IsItemsHost)
        {
            if (invalidateChildren)
            {
                ResetVirtualChildren();
                invalidateChildren = false;
            }
            EnsureVisibleChildren();
        }

        // Constrain our available size based on which scroll bars are enabled.
        if (isVertical)
        {
            availableSize.Height = double.PositiveInfinity;
            if (CanHorizontallyScroll)
                availableSize.Width = double.PositiveInfinity;
        }
        else
        {
            availableSize.Width = double.PositiveInfinity;
            if (CanVerticallyScroll)
                availableSize.Height = double.PositiveInfinity;
        }

        // Measure each child
        foreach (var child in InternalChildren)
        {
            if (child is not UIElement elem)
                continue;
            elem.Measure(availableSize);

            if (isVertical)
            {
                size.Width = Math.Max(size.Width, elem.DesiredSize.Width);
                size.Height += elem.DesiredSize.Height;
            }
            else
            {
                size.Width += elem.DesiredSize.Width;
                size.Height = Math.Max(size.Height, elem.DesiredSize.Height);
            }
        }

        if (isVertical)
            size.Height = itemLength * (itemsOwner?.Items?.Count ?? 1);//+= FirstVisibleOffset + SizeAfterLast;
        else
            size.Width = itemLength * (itemsOwner?.Items?.Count ?? 1);// += FirstVisibleOffset + SizeAfterLast;

        var extent = size;

        // Constrain our measured size
        size.Width = Math.Min(size.Width, constraint.Width);
        size.Height = Math.Min(size.Height, constraint.Height);

        var viewport = size;
        UpdateScrollData(viewport, extent, scrollData.offset);

        return size;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        bool isVertical = Orientation == Orientation.Vertical;
        var children = InternalChildren;

        Rect finalRect = new(finalSize);
        if (IsScrolling)
        {
            if (isVertical)
            {
                finalRect.X = -1.0 * scrollData.ComputedOffset.X;
                finalRect.Y = ComputePhysicalFromLogicalOffset(scrollData.ComputedOffset.Y, fHorizontal: false);
            }
            else
            {
                finalRect.X = ComputePhysicalFromLogicalOffset(scrollData.ComputedOffset.X, fHorizontal: true);
                finalRect.Y = -1.0 * scrollData.ComputedOffset.Y;
            }
        }

        double offset = FirstVisibleOffset;
        foreach (var child in children)
        {
            if (child is not UIElement elem)
                continue;

            // Move the child rect down or across by the size of the last element; set it's dimensions to the desired size.
            if (isVertical)
            {
                finalRect.Y += offset;
                offset = finalRect.Height = itemLength;//elem.DesiredSize.Height;
                finalRect.Width = Math.Max(finalSize.Width, elem.DesiredSize.Width);
            }
            else
            {
                finalRect.X += offset;
                offset = finalRect.Width = itemLength;//elem.DesiredSize.Width;
                finalRect.Height = Math.Max(finalRect.Height, elem.DesiredSize.Height);
            }

            elem.Arrange(finalRect);
        }

        return finalSize;
    }

    #region IScrollInfo

    /// <summary>
    /// Scrolls content by one logical unit upward.
    /// </summary>
    public void LineUp() => SetVerticalOffset(VerticalOffset - ((Orientation == Orientation.Vertical) ? 1.0 : 16.0));

    /// <summary>
    /// Scrolls content downward by one logical unit.
    /// </summary>
    public void LineDown() => SetVerticalOffset(VerticalOffset + ((Orientation == Orientation.Vertical) ? 1.0 : 16.0));

    /// <summary>
    /// Scrolls content by one logical unit to the left.
    /// </summary>
    public void LineLeft() => SetHorizontalOffset(HorizontalOffset - ((Orientation == Orientation.Horizontal) ? 1.0 : 16.0));

    /// <summary>
    /// Scrolls content by one logical unit to the right.
    /// </summary>
    public void LineRight() => SetHorizontalOffset(HorizontalOffset + ((Orientation == Orientation.Horizontal) ? 1.0 : 16.0));

    /// <summary>
    /// Scrolls content logically upward by one page.
    /// </summary>
    public void PageUp() => SetVerticalOffset(VerticalOffset - ViewportHeight);

    /// <summary>
    /// Scrolls content logically downward by one page.
    /// </summary>
    public void PageDown() => SetVerticalOffset(VerticalOffset + ViewportHeight);

    /// <summary>
    /// Scrolls content logically to the left by one page.
    /// </summary>
    public void PageLeft() => SetHorizontalOffset(HorizontalOffset - ViewportWidth);

    /// <summary>
    /// Scrolls content logically to the right by one page.
    /// </summary>
    public void PageRight() => SetHorizontalOffset(HorizontalOffset + ViewportWidth);

    /// <summary>
    /// Scrolls content logically upward in response to a click of the mouse wheel button.
    /// </summary>
    public void MouseWheelUp()
    {
        if (CanMouseWheelVerticallyScroll)
            SetVerticalOffset(VerticalOffset - (double)SystemParameters.WheelScrollLines * ((Orientation == Orientation.Vertical) ? 1.0 : 16.0));
        else
            PageUp();
    }

    /// <summary>
    /// Scrolls content logically downward in response to a click of the mouse wheel button.
    /// </summary>
    public void MouseWheelDown()
    {
        if (CanMouseWheelVerticallyScroll)
            SetVerticalOffset(VerticalOffset + (double)SystemParameters.WheelScrollLines * ((Orientation == Orientation.Vertical) ? 1.0 : 16.0));
        else
            PageDown();
    }

    /// <summary>
    /// Scrolls content logically to the left in response to a click of the mouse wheel button.
    /// </summary>
    public void MouseWheelLeft() => SetHorizontalOffset(HorizontalOffset - 3.0 * ((Orientation == Orientation.Horizontal) ? 1.0 : 16.0));

    /// <summary>
    /// Scrolls content logically to the right in response to a click of the mouse wheel button.
    /// </summary>
    public void MouseWheelRight() => SetHorizontalOffset(HorizontalOffset + 3.0 * ((Orientation == Orientation.Horizontal) ? 1.0 : 16.0));

    /// <summary>
    /// Sets the value of the <see cref="HorizontalOffset"/> property.
    /// </summary>
    /// <param name="offset">The value of the <see cref="HorizontalOffset"/> property.</param>
    public void SetHorizontalOffset(double offset)
    {
        offset = Math.Max(offset, 0);
        if (!AreClose(offset, scrollData.offset.X))
        {
            scrollData.offset.X = offset;
            InvalidateMeasure();
        }
    }

    /// <summary>
    /// Sets the value of the <see cref="VerticalOffset"/> property.
    /// </summary>
    /// <param name="offset">The value of the <see cref="VerticalOffset"/> property.</param>
    public void SetVerticalOffset(double offset)
    {
        offset = Math.Max(offset, 0);
        if (!AreClose(offset, scrollData.offset.Y))
        {
            scrollData.offset.Y = offset;
            InvalidateMeasure();
        }
    }

    /// <summary>
    /// Scrolls to the specified coordinates and makes that part of a <see cref="Visual"/> visible.
    /// </summary>
    /// <param name="visual">The <see cref="Visual"/> that becomes visible.</param>
    /// <param name="rectangle">The <see cref="Rect"/> that represents coordinate space within a visual.</param>
    /// <returns>A <see cref="Rect"/> in the coordinate space that is made visible.</returns>
    public Rect MakeVisible(Visual visual, Rect rectangle)
    {
        //Vector newOffset = default;
        Rect newRect = default;
        if (rectangle.IsEmpty || visual == null || visual == this || !IsAncestorOf(visual))
            return Rect.Empty;

        rectangle = visual.TransformToAncestor(this).TransformBounds(rectangle);
        if (!IsScrolling)
            return rectangle;

        /*MakeVisiblePhysicalHelper(rectangle, ref newOffset, ref newRect);
        int childIndex = FindChildIndexThatParentsVisual(visual);
        MakeVisibleLogicalHelper(childIndex, ref newOffset, ref newRect);
        newOffset.X = ScrollContentPresenter.CoerceOffset(newOffset.X, _scrollData._extent.Width, _scrollData._viewport.Width);
        newOffset.Y = ScrollContentPresenter.CoerceOffset(newOffset.Y, _scrollData._extent.Height, _scrollData._viewport.Height);
        if (!AreClose(newOffset, scrollData.offset))
        {
            scrollData.offset = newOffset;
            InvalidateMeasure();
            OnScrollChange();
        }*/

        return newRect;
    }

    #endregion

    /*protected override void BringIndexIntoView(int index)
    {
        base.BringIndexIntoView(index - FirstVisibleItem);
    }*/

    /*protected override Visual GetVisualChild(int index)
    {
        return base.GetVisualChild(index - FirstVisibleItem);
    }*/

    protected override bool ShouldItemsChangeAffectLayoutCore(bool areItemChangesLocal, ItemsChangedEventArgs args)
    {
        var totalItems = Math.Max(1, itemsOwner?.Items?.Count ?? 0);
        var itemContainerGenerator = ItemContainerGenerator;

        //var minVis = Math.Clamp(FirstVisibleItem, 0, totalItems - 1);
        var maxVis = Math.Clamp(LastVisibleItem, 0, totalItems - 1);
        var pos = itemContainerGenerator.IndexFromGeneratorPosition(args.Position);
        var oldPos = itemContainerGenerator.IndexFromGeneratorPosition(args.OldPosition);

        // Return true if the change is within the visible index
        if (/*pos >= minVis && */pos <= maxVis)
            return true;
        if (/*oldPos >= minVis && */oldPos <= maxVis)
            return true;

        return false;
    }

    protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args)
    {
        base.OnItemsChanged(sender, args);
        // EnsureVisibleChildren();

        switch (args.Action)
        {
            case NotifyCollectionChangedAction.Add:
                AddChildren(args.Position, args.ItemCount);
                break;
            case NotifyCollectionChangedAction.Remove:
                RemoveChildren(args.Position, args.ItemCount);
                break;
            case NotifyCollectionChangedAction.Replace:
                ReplaceChildren(args.Position, args.ItemCount, args.ItemUICount);
                break;
            case NotifyCollectionChangedAction.Move:
                MoveChildren(args.OldPosition, args.Position, args.ItemUICount);
                break;
            case NotifyCollectionChangedAction.Reset:
                OnClearChildren();
                //GenerateChildren();
                EnsureVisibleChildren();
                break;
        }

        //if (IsScrolling)
        //    ResetMaximumDesiredSize();
        InvalidateMeasure();
    }

    private void EnsureVisibleChildren()
    {
        var itemContainerGenerator = ItemContainerGenerator;
        var totalItems = itemsOwner?.Items?.Count ?? 0;
        int prevCount = InternalChildren.Count;

        if (totalItems == 0)
        {
            if (prevCount > 0)
                RemoveInternalChildRange(0, prevCount);
            return;
        }

        var minVis = Math.Clamp(FirstVisibleItem, 0, totalItems - 1);
        var maxVis = Math.Clamp(LastVisibleItem, 0, totalItems - 1);
        int desiredCount = maxVis - minVis + 1;
        if (minVis == maxVis)
        {
            if (prevCount > 0)
                RemoveInternalChildRange(0, prevCount);
            return;
        }

        // 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 
        //       [  ]       ==> orig
        //    [     ]       ===> insert 1 @ 0
        //       [     ]    ===> insert 4 @ 2
        //       []         ===> remove @ 1
        //         []       ===> remove @ 0
        //         [   ]    ===> remove @ 0, insert @ 1

        // Insert/Remove from the start of the list
        if (minVis > prevFirstVisible)
        {
            // Remove old elements from the start
            int removed = Math.Min(prevCount, minVis - prevFirstVisible);
            RemoveInternalChildRange(0, removed);
            // TODO: We get a databinding error logged when opening the log window. This seems to be because the first few log items get destroyed
            // before they get a chance to render, and due to the asynchronous nature of the databinding engine, this causes a race condition.
            //Debug.WriteLine($"[VSP] prev: {prevFirstVisible}:{prevFirstVisible + prevCount - 1} new: {minVis}:{maxVis}    remove from start {minVis - prevFirstVisible}");
        }
        else if (minVis < prevFirstVisible)
        {
            // Append new elements to the start
            int added = prevFirstVisible - minVis;
            added = Math.Min(added, desiredCount);
            var pos = itemContainerGenerator.GeneratorPositionFromIndex(minVis - 1);
            //Debug.WriteLine($"[VSP] prev: {prevFirstVisible}:{prevFirstVisible + prevCount - 1} new: {minVis}:{maxVis}    insert from start {added} ({pos})");
            using (itemContainerGenerator.StartAt(pos, GeneratorDirection.Forward))
            {
                for (int i = 0; i < added; i++)
                {
                    if (itemContainerGenerator.GenerateNext(out _) is UIElement elem)
                    {
                        try
                        {
                            InsertInternalChild(i, elem);
                            itemContainerGenerator.PrepareItemContainer(elem);
                        }
                        catch
                        {
                            //Debug.WriteLine("[VSP] Uh oh tried to generate the wrong item again...");
                        }
                    }
                }
            }
        }

        // Insert/Remove from the end of the list
        int prevLast = prevFirstVisible + prevCount - 1;
        prevCount = InternalChildren.Count;
        if (desiredCount < prevCount)
        {
            // Remove old elements from the end
            int removed = prevCount - desiredCount;
            int start = prevCount - removed;
            RemoveInternalChildRange(start, removed);
            //Debug.WriteLine($"[VSP] prev: {prevFirstVisible}:{prevLast} new: {minVis}:{maxVis}    remove from end {removed} ({prevCount - removed})");
        }
        else if (desiredCount > prevCount)
        {
            // Append new elements to the end
            int added = desiredCount - prevCount;
            var pos = itemContainerGenerator.GeneratorPositionFromIndex(Math.Max(0, minVis + prevCount)); //new GeneratorPosition(Math.Max(-1, prevFirstVisible + prevCount - 1), 0);
            //Debug.WriteLine($"[VSP] prev: {prevFirstVisible}:{prevLast} new: {minVis}:{maxVis}    insert from end {added} ({pos})");
            using (itemContainerGenerator.StartAt(pos, GeneratorDirection.Forward))
            {
                for (int i = 0; i < added; i++)
                {
                    if (itemContainerGenerator.GenerateNext(out _) is UIElement elem)
                    {
                        try
                        {
                            AddInternalChild(elem);
                            itemContainerGenerator.PrepareItemContainer(elem);
                        }
                        catch
                        {
                            //Debug.WriteLine("[VSP] Uh oh tried to generate the wrong item again...");
                        }
                    }
                }
            }
        }

        prevFirstVisible = minVis;
    }

    private void AddChildren(GeneratorPosition pos, int itemCount)
    {
        var itemContainerGenerator = ItemContainerGenerator;
        var minVis = FirstVisibleItem;
        var maxVis = LastVisibleItem;
        int prevCount = InternalChildren.Count;

        // Remove any invalid children or trick EnsureVisibleChildren to do so
        int ind = itemContainerGenerator.IndexFromGeneratorPosition(pos);
        if (ind < minVis)
            prevFirstVisible += itemCount;
        else if (ind > maxVis)
            return;
        else // Within minvis and maxvis
            RemoveInternalChildRange(ind - minVis, prevCount - ind + minVis);

        // Do the generator stuff here
        EnsureVisibleChildren();
    }

    private void RemoveChildren(GeneratorPosition pos, int containerCount)
    {
        var itemContainerGenerator = ItemContainerGenerator;
        var minVis = FirstVisibleItem;
        var maxVis = LastVisibleItem;
        int prevCount = InternalChildren.Count;

        // Remove any invalid children or trick EnsureVisibleChildren to do so
        int ind = itemContainerGenerator.IndexFromGeneratorPosition(pos);
        if (ind < minVis)
            prevFirstVisible -= containerCount;
        else if (ind > maxVis)
            return;
        else // Within minvis and maxvis
            RemoveInternalChildRange(ind, containerCount);

        // Do the generator stuff here
        EnsureVisibleChildren();
    }

    private void ReplaceChildren(GeneratorPosition pos, int itemCount, int containerCount)
    {
        IItemContainerGenerator itemContainerGenerator = ItemContainerGenerator;
        using (itemContainerGenerator.StartAt(pos, GeneratorDirection.Forward, allowStartAtRealizedItem: true))
        {
            for (int i = 0; i < itemCount; i++)
            {
                if (itemContainerGenerator.GenerateNext(out var isNewlyRealized) is UIElement elem && !isNewlyRealized)
                {
                    RemoveInternalChildRange(pos.Index + i, 1);
                    InsertInternalChild(pos.Index + i, elem);
                    //InternalChildren[pos.Index + i] = elem;
                    itemContainerGenerator.PrepareItemContainer(elem);
                }
            }
        }
    }

    private void MoveChildren(GeneratorPosition fromPos, GeneratorPosition toPos, int containerCount)
    {
        RemoveInternalChildRange(0, InternalChildren.Count);
        EnsureVisibleChildren();
        /*var children = InternalChildren;
        if (fromPos == toPos)
            return;

        int startInd = ItemContainerGenerator.IndexFromGeneratorPosition(toPos);
        using var movedChildren = new TemporaryList<UIElement>(containerCount);
        for (int i = 0; i < containerCount; i++)
            movedChildren.Add(children[fromPos.Index + i]);

        RemoveInternalChildRange(fromPos.Index, containerCount);
        for (int j = 0; j < containerCount; j++)
            InsertInternalChild(startInd + j, movedChildren[j]);*/
    }

    protected override void OnClearChildren()
    {
        base.OnClearChildren();
        if (IsItemsHost)
        {
            //var itemsControl = ItemsControl.GetItemsOwner(this);
            ItemContainerGenerator?.RemoveAll();
            //CleanupContainers(int.MaxValue, int.MaxValue, itemsControl);
        }

        RemoveInternalChildRange(0, InternalChildren.Count);
    }

    private double MeasureFirstChild()
    {
        if (itemsOwner?.ItemTemplate?.LoadContent() is not FrameworkElement template)
            return 1;

        template.Measure(new(double.PositiveInfinity, double.PositiveInfinity));
        return Orientation == Orientation.Vertical ? template.DesiredSize.Height : template.DesiredSize.Width;
        /*var itemContainerGenerator = ItemContainerGenerator;
        using (itemContainerGenerator.StartAt(new GeneratorPosition(-1, 0), GeneratorDirection.Forward))
        {
            if (itemContainerGenerator.GenerateNext() is UIElement elem)
            {
                itemContainerGenerator.PrepareItemContainer(elem);
                elem.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                return Orientation == Orientation.Vertical ? elem.RenderSize.Height : elem.RenderSize.Width;
            }
        }
        return 1;*/
    }

    private ScrollViewer? FindScrollHost()
    {
        var itemsOwner = ItemsControl.GetItemsOwner(this);
        DependencyObject obj = this;
        while (obj != itemsOwner && obj != null)
        {
            if (obj is ScrollViewer scrollHost)
                return scrollHost;

            obj = VisualTreeHelper.GetParent(obj);
        }
        return null;
    }

    /// <summary>
    /// Returns the position of the specified item, relative to the <see cref="VirtualStackPanel"/>.
    /// </summary>
    /// <param name="child">The element whose position to find.</param>
    /// <returns>The position of the specified item, relative to the <see cref="VirtualStackPanel"/>.</returns> 
    protected override double GetItemOffsetCore(UIElement? child)
    {
        if (child == null)
            return 0;

        bool isHorizontal = Orientation == Orientation.Horizontal;
        var itemStorageProvider = ItemsControl.GetItemsOwner(this);

        var generator = (ItemContainerGenerator)ItemContainerGenerator;
        IList itemsInternal = InternalChildren;
        int num = generator.IndexFromContainer(child, returnLocalIndex: true);
        double distance = 0.0;

        return distance;
    }

    protected override void OnIsItemsHostChanged(bool oldIsItemsHost, bool newIsItemsHost)
    {
        base.OnIsItemsHostChanged(oldIsItemsHost, newIsItemsHost);
        invalidateChildren = true;
    }

    protected override void OnInitialized(EventArgs e)
    {
        base.OnInitialized(e);
        invalidateChildren = true;
    }

    private double ComputePhysicalFromLogicalOffset(double logicalOffset, bool fHorizontal)
    {
        double offset = 0.0;
        UIElementCollection internalChildren = InternalChildren;
        for (int i = 0; i < logicalOffset; i++)
            offset -= (fHorizontal ? internalChildren[i].DesiredSize.Width : internalChildren[i].DesiredSize.Height);

        return offset;
    }

    private void ResetScrolling()
    {
        InvalidateMeasure();
        if (IsScrolling)
            scrollData.ClearLayout();
    }

    private void UpdateScrollData(Size viewport, Size extent, Vector offset)
    {
        scrollData.offset = offset;
        if (AreClose(viewport, scrollData.viewport) && AreClose(extent, scrollData.extent) && AreClose(offset, scrollData.offset))
            return;

        scrollData.viewport = viewport;
        scrollData.extent = extent;
        scrollData.computedOffset = offset;

        OnScrollChange();
    }

    private static bool AreClose(double a, double b)
    {
        return a == b || Math.Abs(Math.Abs(a - b) + a) == Math.Abs(a);
    }

    private static bool AreClose(Size a, Size b) => AreClose(a.Width, b.Width) && AreClose(a.Height, b.Height);
    private static bool AreClose(Vector a, Vector b) => AreClose(a.X, b.X) && AreClose(a.Y, b.Y);

    private void OnScrollChange()
    {
        ScrollOwner?.InvalidateScrollInfo();
    }

    private static void OnOrientationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        (d as VirtualStackPanel)?.ResetScrolling();
    }

    private class ScrollData
    {
        internal bool allowHorizontal;
        internal bool allowVertical;
        internal Vector offset;
        internal Vector computedOffset = default;
        internal Size viewport;
        internal Size extent;
        internal double physicalViewport;
        internal ScrollViewer? scrollOwner;

        public Vector Offset { get => offset; set => offset = value; }
        public Size Viewport { get => viewport; set => viewport = value; }
        public Size Extent { get => extent; set => extent = value; }
        public Vector ComputedOffset { get => computedOffset; set => computedOffset = value; }

        internal void ClearLayout()
        {
            offset = default;
            viewport = extent = default;
            physicalViewport = 0.0;
        }

        public void SetPhysicalViewport(double value)
        {
            physicalViewport = value;
        }
    }
}
