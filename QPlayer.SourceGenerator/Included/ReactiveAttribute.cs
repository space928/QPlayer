using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QPlayer.SourceGenerator;

/// <summary>
/// Automatically generates a property for the annotated field which automatically raised 
/// PropertyChanged notifications when set.
/// <br/>
/// This attribute can also be used on existing properties to generate a reactive property for that property. 
/// To this, the annotated property must either be partial (in which case a backing field will also be 
/// generated), or the <paramref name="propName"/> parameter must be specified.
/// </summary>
/// <remarks>
/// If a custom property name is not specified, the generated property will take the name of field
/// with the first letter capitalised, or prefixed with an underscore. IE: <c>int exampleProp</c> -> 
/// <c>int ExampleProp</c>, and <c>int OtherExample</c> -> <c>int _OtherExample</c>
/// </remarks>
/// <param name="propName">The name of the reactive property to generate.</param>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
public sealed class ReactiveAttribute(string? propName = null) : Attribute
{
    public string? PropName => propName;
}

/// <summary>
/// By default, when this property is updated, the old value is compared to the new value using the 
/// <c>==</c> operator, and only if the value are different is the setter and property change 
/// notification invoked. This attribute skips this check, this can be useful if no <c>==</c> is 
/// available or if this equality comparison would be very expensive.
/// <br/>
/// Requires a <see cref="ReactiveAttribute"/> on this same property.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
public sealed class SkipEqualityCheckAttribute : Attribute
{
}

/// <summary>
/// When this property is updated, a property change notification for the specified property is also raised.
/// <br/>
/// Requires a <see cref="ReactiveAttribute"/> on this same property.
/// </summary>
/// <param name="propName">The name of the property to generate a change notifcation for.</param>
[System.AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = false, AllowMultiple = true)]
sealed class ChangesPropAttribute(string propName) : Attribute
{
    public string PropName => propName;
}

/// <summary>
/// Uses the getter and setter from the specified property as a template for this reactive property.
/// <br/>
/// Requires a <see cref="ReactiveAttribute"/> on this same property.
/// </summary>
/// <param name="propName">The name of the property to use to implement this reactive property.</param>
[System.AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
sealed class TemplatePropAttribute(string propName) : Attribute
{
    public string PropName => propName;
}

/// <summary>
/// Marks the setter on the generated property as private.
/// <br/>
/// Requires a <see cref="ReactiveAttribute"/> on this same property.
/// </summary>
[System.AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
public sealed class PrivateSetterAttribute : Attribute
{

}

/// <summary>
/// Specifies that, for the annotated property, the provided delegates should be called to synchronise 
/// data to and from the model for this property.
/// </summary>
/// <param name="vmToModel">The name of a static method in this class to copy this property's value from this instance to the model.</param>
/// <param name="modelToVM">The name of a static method in this class to copy this property's value from the model to this instance.</param>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
public sealed class ModelCustomBindingAttribute(string? vmToModel, string? modelToVM) : Attribute
{
    public string? VMToModel => vmToModel;
    public string? ModelToVM => modelToVM;
}

/// <summary>
/// Specifies the name of the field of the model this property should be bound to.
/// </summary>
/// <param name="modelPath"></param>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
public sealed class ModelBindsToAttribute(string modelPath) : Attribute
{
    public string ModelPath => modelPath;
}

/// <summary>
/// Indicates that this property should not be automatically bound to a corresponding model property. Note that 
/// properties for read-only fields are implicitly skipped from model binding as they cannot be written too.
/// </summary>
[System.AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
public sealed class ModelSkipAttribute : Attribute
{

}

/// <summary>
/// Allows a custom getter function to be used in a property generated using a <see cref="ReactiveAttribute"/>.
/// </summary>
/// <param name="getter">A function which returns the value of the property.</param>
/// <param name="inline">When <see langword="false"/>, the setter parameter expects a method name; 
/// when <see langword="true"/>, the getter parameter takes an expression which is placed inline in the generator getter.</param>
[AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
public sealed class CustomGetAttribute(string getter, bool inline = false) : Attribute
{
    public string Getter { get; } = getter;
    public bool Inline { get; } = inline;
}

/// <summary>
/// Allows a custom setter function to be used in a property generated using a <see cref="ReactiveAttribute"/>.
/// </summary>
/// <param name="setter">A method which takes as input the new value to assign to the property.</param>
/// <param name="inline">When <see langword="false"/>, the setter parameter expects a method name; 
/// when <see langword="true"/>, the setter parameter takes an expression which is placed inline in the generator setter.</param>
[AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
public sealed class CustomSetAttribute(string setter, bool inline = false) : Attribute
{
    public string Setter { get; } = setter;
    public bool Inline { get; } = inline;
}

/// <summary>
/// Allows custom accessibility keywords to be used on a property generated using a <see cref="ReactiveAttribute"/>.
/// </summary>
/// <example>
/// [Reactive, CustomAccessibility("protected virtual")] string prop;
/// </example>
/// <param name="accessibilityModifiers">The accessibility modifiers to add to the generated property.</param>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
public sealed class CustomAccessibilityAttribute(string accessibilityModifiers) : Attribute
{
    public string AccessibilityModifiers { get; } = accessibilityModifiers;
}

/// <summary>
/// Specifies the model type associated with this view model.
/// </summary>
/// <param name="modelType"></param>
[System.AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class ModelAttribute(Type modelType) : Attribute
{
    public Type ModelType => modelType;
}

/// <summary>
/// Specifies the view type associated with this view model.
/// </summary>
/// <param name="viewType"></param>
[System.AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class ViewAttribute(Type viewType) : Attribute
{
    public Type ViewType => viewType;
}
