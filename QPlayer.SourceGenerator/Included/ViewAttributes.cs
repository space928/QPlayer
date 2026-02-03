using System;
using System.Collections.Generic;
using System.Text;

namespace QPlayer.SourceGenerator;

/// <summary>
/// Indicates to the source generator that a <see cref="System.Windows.DataTemplate"/> should be generated for this view model.
/// <br/>
/// By default, a view control will be generated for every public property on this class.
/// </summary>
[System.AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
public sealed class GenerateViewAttribute(string? typeName = null) : Attribute
{
    public string? TypeName => typeName;
}

/// <summary>
/// Specifies the label for the generated view control for this property.
/// </summary>
/// <param name="label"></param>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
public sealed class ViewLabelAttribute(string label) : Attribute
{
    public string Label => label;
}

/// <summary>
/// Indicates that a view control should not be generated for this property.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
public sealed class SkipViewAttribute : Attribute { }

/// <summary>
/// For numeric properties, this allows the minimum, maximum, and change rate of the generated view control to be specified.
/// </summary>
/// <param name="minVal"></param>
/// <param name="maxVal"></param>
/// <param name="rate"></param>
/// <param name="clampValue"></param>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
public sealed class RangeAttribute(double minVal = double.MinValue, double maxVal = double.MaxValue, double rate = 0.1, bool clampValue = true) : Attribute 
{
    public double MinVal => minVal;
    public double MaxVal => maxVal;
    public double Rate => rate;
    public bool ClampValue => clampValue;
}

/// <summary>
/// Specifies that a knob control should be generated for this property (as opposed to a Spinbox).
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
public sealed class KnobAttribute : Attribute { }

/// <summary>
/// Specifies that a slider control should be generated for this property (as opposed to a Spinbox).
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
public sealed class SliderAttribute : Attribute { }

/// <summary>
/// Specifies that a file picker control should be generated for this property. 
/// Only valid on string properties.
/// </summary>
/// <param name="commandName">The name of the <c>ICommand</c> to invoke when the file picker button is pressed.</param>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
public sealed class FilePickerAttribute(string commandName) : Attribute 
{
    public string CommandName => commandName;
}

/// <summary>
/// Generates a button control in place of this property which invokes the given command. 
/// The annotated property's value is ignored.
/// </summary>
/// <param name="commandName">The name of the <c>ICommand</c> to invoke when the button is pressed.</param>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
public sealed class ButtonAttribute(string commandName) : Attribute 
{
    public string CommandName => commandName;
}

/// <summary>
/// Generates a heading label before the annotated property in the view.
/// </summary>
/// <param name="title">The title of the heading.</param>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
public sealed class HeadingAttribute(string title) : Attribute 
{
    public string Title => title;
}

/// <summary>
/// Specifies a tooltip to display for the generated view control for this property.
/// </summary>
/// <param name="text"></param>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
public sealed class TooltipAttribute(string text) : Attribute
{
    public string Text => text;
}

