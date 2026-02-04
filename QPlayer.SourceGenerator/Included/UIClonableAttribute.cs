using System;
using System.Collections.Generic;
using System.Text;

namespace QPlayer.SourceGenerator;

/// <summary>
/// When used on a class which derives from <see cref="ArgonUI.UIElements.UIElement"/>, generates an implementation 
/// for <see cref="ArgonUI.UIElements.UIElement.Clone()"/> and <see cref="ArgonUI.UIElements.UIElement.Clone(UIElements.UIElement)"/>
/// based on all fields marked as [<see cref="ReactiveAttribute"/>].
/// <para/>
/// Use the [<see cref="UICloneableFieldAttribute"/>] and [<see cref="UICloneableSkipAttribute"/>] to add/remove fields 
/// from inclusion in the <c>Clone()</c> implementation.
/// <para/>
/// To mix both a automatically generated clone implementation with custom behaviour, the <paramref name="invokeCustomCloner"/>
/// parameter can be set. This allows the generated implementation to call a custom clone method after it has cloned all the
/// automatically implemented properties. The custom clone method is always called after all other fields have been cloned
/// and must have the following signature:
/// <code>
/// // This method CAN be declared as private, T represents the type of this attribute is decorating.
/// void CustomClone(T target);
/// </code>
/// <para/>
/// If a field's type declares a <c>public T Clone();</c> method (or one is provided by an extension method) then this 
/// method will be called when copying that field. This behaviour must be enabled via the 
/// <see cref="UICloneableFieldAttribute.UseCloneMethod"/> property.
/// </summary>
/// <remarks>
/// Generates the following code:
/// <code>
/// public partial class Label
/// {
///     public override UIElement Clone() => Clone(new Label());
/// 
///     public override UIElement Clone(UIElement target)
///     {
///         base.Clone(target);
///         if (target is Label t)
///         {
///             t.text = text;
///             t.size = size;
///             t.colour = colour;
///             // For the sake of example, if Font defined a Clone() method it would be called as shown.
///             t.font = font.Clone();
///         }
///         return target;
///     }
/// }
/// </code>
/// </remarks>
/// <param name="invokeCustomCloner">When <see langword="true"/>, calls a method on this </param>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class UIClonableAttribute(bool invokeCustomCloner = false) : Attribute
{
    private readonly bool invokeCustomCloner = invokeCustomCloner;

    public bool InvokeCustomCloner => invokeCustomCloner;
}

/// <summary>
/// Marks a field to be included for the automatic <see cref="ArgonUI.UIElements.UIElement.Clone()"/> 
/// implementation.
/// </summary>
/// <remarks>
/// See <seealso cref="UIClonableAttribute"/>.
/// </remarks>
/// <param name="useCloneMethod">If a field's type declares a <c>public T Clone();</c> method (or one is provided by 
/// an extension method) then this method will be called when copying the annotated field.</param>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class UICloneableFieldAttribute(bool useCloneMethod = false) : Attribute
{ 
    private readonly bool useCloneMethod = useCloneMethod;

    public bool UseCloneMethod => useCloneMethod;
}

/// <summary>
/// Marks a field to be excluded from the automatic <see cref="ArgonUI.UIElements.UIElement.Clone()"/> 
/// implementation.
/// </summary>
/// <remarks>
/// See <seealso cref="UIClonableAttribute"/>.
/// </remarks>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class UICloneableSkipAttribute : Attribute
{ }
