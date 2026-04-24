using CommunityToolkit.Mvvm.ComponentModel;
using QPlayer.SourceGenerator;
using QPlayer.Utilities;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;

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

    internal Model? BoundModel => boundModel;

    /// <summary>
    /// Binds this <see cref="ObservableObject"/> to the specified model instance. Automatically propagates proeprties 
    /// changes from this object to the model, but not the other way around.
    /// <br/>
    /// When using the source generator, this method is automatically implemented so long as the deriving 
    /// class defines at least one reactive property (see <see cref="ReactiveAttribute"/>).
    /// </summary>
    /// <param name="model">The model to bind to, or <see langword="null"/> to unbind.</param>
    public virtual void Bind(Model? model)
    {
        if (model == boundModel)
            return;
        boundModel = model;
    }

    /// <summary>
    /// Copies the value of the named property from the <paramref name="__src"/> view model to this one. Only copies bindable properties.
    /// </summary>
    /// <param name="__src">The view model to copy from.</param>
    /// <param name="__prop">The property to copy.</param>
    /// <returns><see langword="true"/> if the property was copied</returns>
    public virtual bool CopyRemoteProperty(BindableViewModel<Model> __src, string __prop)
    {
        return false;
    }

    /// <summary>
    /// Checks if a given property on this object is automatically registers undo actions.
    /// </summary>
    /// <param name="__prop">The property name to check.</param>
    /// <returns><see langword="true"/> if the given property is undoable.</returns>
    public virtual bool IsPropertyUndoable(string __prop)
    {
        return false;
    }

    /// <summary>
    /// Copies all bound property values on this instance from the bound model.
    /// <br/>
    /// When using the source generator, this method is automatically implemented so long as the deriving 
    /// class defines at least one reactive property (see <see cref="ReactiveAttribute"/>).
    /// </summary>
    public virtual void SyncFromModel()
    {
        OnSyncFromModel();
    }

    /// <summary>
    /// Copies all bound property values on this instance to the bound model.
    /// <br/>
    /// When using the source generator, this method is automatically implemented so long as the deriving 
    /// class defines at least one reactive property (see <see cref="ReactiveAttribute"/>).
    /// </summary>
    public virtual void SyncToModel()
    {
        OnSyncToModel();
    }

    /// <summary>
    /// When using a source generator, this method is automatically implemented and should be called in <see cref="SyncFromModel"/>.
    /// </summary>
    protected virtual void OnSyncFromModel() { }
    /// <summary>
    /// When using a source generator, this method is automatically implemented and should be called in <see cref="SyncToModel"/>.
    /// </summary>
    protected virtual void OnSyncToModel() { }
}

public interface ITypedConverter<TFrom, TTo>
{
    public static abstract TTo Convert(TFrom from);
    public static abstract TFrom ConvertBack(TTo to);
}
