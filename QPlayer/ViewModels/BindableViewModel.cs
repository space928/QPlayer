using CommunityToolkit.Mvvm.ComponentModel;
using QPlayer.Utilities;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace QPlayer.ViewModels;

/// <summary>
/// The base class for any view model which can be bound onto a separate model instance.
/// <para/>
/// This class implements all the necessary logic to automatically synchronise changes 
/// from a view model to a model. The behaviour of the automatic bindings can be controlled
/// using the various model attribtues (<see cref="ModelSkipAttribute"/>, 
/// <see cref="ModelCustomBindingAttribute"/>, <see cref="ModelBindsToAttribute"/>).
/// Implementing classes should be annotated with <see cref="ModelAttribute"/> and 
/// <see cref="ViewAttribute"/> where applicable.
/// </summary>
/// <remarks>
/// The default automatic binding behaviour is to bind all properties with a public getter 
/// and setter on the ViewModel to all public fields with matching (case insensitive) names 
/// on the Model.
/// </remarks>
/// <typeparam name="Model">The type of the model to bind to.</typeparam>
public abstract class BindableViewModel<Model> : ObservableObject
    where Model : class
{
    protected Model? boundModel;
    private BindingsCollection? modelBindings;

    private static readonly ConcurrentDictionary<Type, BindingsCollection> typeBindingsCache = [];

    /// <summary>
    /// Binds this <see cref="ObservableObject"/> to the specified model instance. Automatically propagates proeprties 
    /// changes from this object to the model, but not the other way around.
    /// </summary>
    /// <param name="model">The model to bind to, or <see langword="null"/> to unbind.</param>
    public void Bind(Model? model)
    {
        if (model == boundModel)
            return;
        boundModel = model;

        if (model == null)
            PropertyChanged -= BindableViewModel_PropertyChanged;
        else
        {
            // Note that, to support polymorphism, we need to always use instance GetType and not typeof.
            // If C# generics/interfaces were more flexible then maybe we could avoid this...
            Type vmType = this.GetType();
            if (typeBindingsCache.TryGetValue(vmType, out var cachedBindings))
            {
                modelBindings = cachedBindings;
            }
            else
            {
                Type modelType = model.GetType();
                modelBindings = BuildBindings(vmType, modelType);
                typeBindingsCache.TryAdd(vmType, modelBindings);
            }

            PropertyChanged += BindableViewModel_PropertyChanged;
        }
    }

    private void BindableViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not string prop || boundModel == null || modelBindings == null)
            return;

        if (modelBindings.propToModel.TryGetValue(prop, out var f))
            f(this, boundModel);
    }

    /// <summary>
    /// Copies all bound property values on this instance from the bound model.
    /// </summary>
    public virtual void SyncFromModel()
    {
        if (boundModel == null || modelBindings == null)
            return;

        foreach (var sync in modelBindings.fieldToViewModel)
            sync(this, boundModel);
    }

    /// <summary>
    /// Copies all bound property values on this instance to the bound model.
    /// </summary>
    public virtual void SyncToModel()
    {
        if (boundModel == null || modelBindings == null)
            return;

        foreach (var sync in modelBindings.propToModel.Values)
            sync(this, boundModel);
    }

    private static BindingsCollection BuildBindings(Type vmType, Type modelType)
    {
        // TODO: This could be made more efficient and introduce compile-time checks if we
        // did this with source-generation instead. 
        StringDict<BindingDelegate> vmToModelBindings = [];
        List<BindingDelegate> modelToVmBindings = [];
        BindingsCollection bindings = new(vmType, modelType, vmToModelBindings, modelToVmBindings);

        // Find all the fields and properties to match together
        var vmProps = vmType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            // VM props must have a public getter and setter to be considered for automatic bindings.
            .Where(x => (x.GetMethod?.IsPublic ?? false) && (x.SetMethod?.IsPublic ?? false));
        var mFields = modelType.GetFields(BindingFlags.Instance | BindingFlags.Public);
        var mFieldsDict = new StringDict<FieldInfo>(mFields.Select(x => new KeyValuePair<string, FieldInfo>(x.Name.ToLowerInvariant(), x)));

        foreach (var vmProp in vmProps)
        {
            if (vmProp.GetCustomAttribute<ModelSkipAttribute>() != null)
                continue;

            string fName = vmProp.Name.ToLowerInvariant();

            FieldInfo? mField = null;
            IEnumerable<FieldInfo> subFields = [];
            if (vmProp.GetCustomAttribute<ModelBindsToAttribute>() is ModelBindsToAttribute modelBindsToAttr)
            {
                var pathParts = modelBindsToAttr.ModelPath.Split('.');
                subFields = FindSubFields(modelType, pathParts);

                if (subFields.FirstOrDefault() is FieldInfo first)
                {
                    mField = first;
                    subFields = subFields.Skip(1);
                }
            }

            if (mField == null && !mFieldsDict.TryGetValue(fName, out mField))
            {
#if DEBUG
                MainViewModel.Log($"Property '{vmProp.Name}' of type {vmType.Name} has no corresponding field in the " +
                    $"model '{modelType.Name}'. Consider decorating this property with a [ModelSkip] attribute if this " +
                    $"was intentional.", MainViewModel.LogLevel.Warning);
#endif
                continue;
            }

            /*
             propToModel = (vm, m) => m.xx = vm.xx;
             modelToProp = (vm, m) => vm.xx = m.xx;

                            case nameof(Path): scue.path = MainViewModel?.ResolvePath(MainViewModel.ResolvePath(Path), false) ?? Path; break;

            if (scue.eq == null)
            {
                scue.eq = new();
                // Use the default values from the vm
                vm.EQ.ToModel(scue.eq);
            }
            else
            {
                vm.EQ.FromModel(scue.eq);
            }

            model.band1 = new(LowFreq, LowGain, .7f, EQBandShape.LowShelf);

                        case nameof(Colour): cueModel.colour = (SerializedColour)Colour; break;

                    cue.parent = Parent?.QID;

             */

            // Because we don't know the specific model and view model types statically, we can't specialise
            // beyond `object`, hence we have to convert every time. For reference types though, this
            // shouldn't matter much.
            var vm = Expression.Parameter(typeof(object));
            var m = Expression.Parameter(typeof(object));
            var vmConv = Expression.Convert(vm, vmType);
            var mConv = Expression.Convert(m, modelType);

            // Create an expression to map the vm to the m and vice versa
            var vmPropExp = Expression.Property(vmConv, vmProp);
            var mFieldExp = Expression.Field(mConv, mField);

            foreach (var subField in subFields)
                mFieldExp = Expression.Field(mFieldExp, subField);

            Expression vmToModelExp;
            Expression modelToVmExp;

            Type? propVmType = null;
            try
            {
                propVmType = typeof(BindableViewModel<>).MakeGenericType(mField.FieldType);
            }
            catch { }

            if (propVmType != null && vmProp.PropertyType.IsAssignableTo(propVmType))
            {
                // Special case where the bound property is itself a bindable view model
                // Here the vmToModel should construct a new model instance if needed and
                // call SyncToModel, and the modelToVm should construct a new vm and bind
                // it to the model.

                // vm.prop.SyncToModel();
                vmToModelExp = Expression.Call(vmPropExp, propVmType.GetMethod(nameof(SyncToModel))!);
                /*
                 var p = new propVmType();
                 p.Bind(cue);
                 p.SyncFromModel();
                 vm.prop = p;
                */
                var propVmInstVar = Expression.Variable(vmProp.PropertyType);
                modelToVmExp = Expression.Block([propVmInstVar],
                    Expression.Assign(propVmInstVar, Expression.New(vmProp.PropertyType)),
                    Expression.Call(propVmInstVar, propVmType.GetMethod(nameof(Bind))!, mFieldExp),
                    Expression.Call(propVmInstVar, propVmType.GetMethod(nameof(SyncFromModel))!),
                    Expression.Assign(vmPropExp, propVmInstVar)
                );
            }
            else
            {
                var customAttr = vmProp.GetCustomAttribute<ModelCustomBindingAttribute>();

                // Use custom bindings if available
                if (customAttr?.VMToModel != null && TryFindStaticMethod(vmType, customAttr.VMToModel, out var vmToModelMI))
                    vmToModelExp = Expression.Call(vmToModelMI, vmConv, mConv);
                else
                    vmToModelExp = Expression.Assign(mFieldExp, vmPropExp);

                if (customAttr?.ModelToVM != null && TryFindStaticMethod(vmType, customAttr.ModelToVM, out var modelToVmMI))
                    modelToVmExp = Expression.Call(modelToVmMI, vmConv, mConv);
                else
                    modelToVmExp = Expression.Assign(vmPropExp, mFieldExp);
            }

            // Compile the expression to IL for efficient evaluation.
            BindingDelegate vmToModelFunc = Expression.Lambda<BindingDelegate>(vmToModelExp, [vm, m]).Compile();
            BindingDelegate modelToVmFunc = Expression.Lambda<BindingDelegate>(modelToVmExp, [vm, m]).Compile();

            vmToModelBindings.Add(vmProp.Name, vmToModelFunc);
            modelToVmBindings.Add(modelToVmFunc);
        }

        return bindings;

        /// <summary>
        /// Searches the type and it's ancestors for a static method by the given name.
        /// </summary>
        static bool TryFindStaticMethod(Type type, string name, [NotNullWhen(true)] out MethodInfo? method)
        {
            var customBindingFlags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
            method = null;
            Type? testType = type;
            while (testType != null && method == null)
            {
                method = testType.GetMethod(name, customBindingFlags);
                testType = testType.BaseType;
            }
            return method != null;
        }

        static IEnumerable<FieldInfo> FindSubFields(Type baseType, IEnumerable<string> parts)
        {
            var type = baseType;
            foreach (var field in parts)
            {
                var fld = type.GetField(field, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (fld == null)
                    throw new FieldAccessException($"Couldn't find field '{field}' in type {type.Name}!");
                yield return fld;
                type = fld.FieldType;
            }
        }
    }
}

public delegate void BindingDelegate(object viewModel, object model);

internal class BindingsCollection(Type vmType, Type modelType, StringDict<BindingDelegate> propToModel, List<BindingDelegate> fieldToViewModel)
{
    public readonly Type vmType = vmType;
    public readonly Type modelType = modelType;
    public readonly StringDict<BindingDelegate> propToModel = propToModel;
    public readonly List<BindingDelegate> fieldToViewModel = fieldToViewModel;
}

public interface ITypedConverter<TFrom, TTo>
{
    public static abstract TTo Convert(TFrom from);
    public static abstract TFrom ConvertBack(TTo to);
}

/*[System.AttributeUsage(AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
public sealed class ModelConverterAttribute<TFrom, TTo>(Func<TFrom, TTo>? vmToModel, Func<TTo, TFrom>? modelToVM) : Attribute
{
    public Func<TFrom, TTo>? VMToModel => vmToModel;
    public Func<TTo, TFrom>? ModelToVM => modelToVM;
}*/

/// <summary>
/// Indicates that, for the annotated property, the provided delegates should be called to synchronise 
/// data to and from the model for this property.
/// </summary>
/// <param name="vmToModel">The name of a static method in this class to copy this property's value from this instance to the model.</param>
/// <param name="modelToVM">The name of a static method in this class to copy this property's value from the model to this instance.</param>
[AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
public sealed class ModelCustomBindingAttribute(string? vmToModel, string? modelToVM) : Attribute
{
    public string? VMToModel => vmToModel;
    public string? ModelToVM => modelToVM;
}

/// <summary>
/// Specifies the name of the field of the model this property should be bound to.
/// </summary>
/// <param name="modelPath"></param>
[AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
public sealed class ModelBindsToAttribute(string modelPath) : Attribute
{
    public string ModelPath => modelPath;
}

/// <summary>
/// Indicates that this property should not be automatically bound to a corresponding model property.
/// </summary>
[System.AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
public sealed class ModelSkipAttribute : Attribute
{

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
