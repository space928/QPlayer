using CommunityToolkit.Mvvm.ComponentModel;
using QPlayer.Utilities;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace QPlayer.ViewModels;

public interface IConvertibleModel<Model, ViewModel>
    where ViewModel : ObservableObject
{
    /// <summary>
    /// Creates a new ViewModel from the given Model, without binding it.
    /// </summary>
    /// <param name="model">the model to copy properties from</param>
    /// <param name="mainViewModel">the main view model</param>
    /// <returns>a new ViewModel for the given Model.</returns>
    public abstract static ViewModel FromModel(Model model, MainViewModel mainViewModel);
    /// <summary>
    /// Copies the properties in this ViewModel to the given Model object.
    /// </summary>
    /// <param name="model">the model to copy to</param>
    public abstract void ToModel(Model model);
    /// <summary>
    /// Copies the value of a given property to the bound Model.
    /// </summary>
    /// <param name="propertyName">the property to copy</param>
    public abstract void ToModel(string propertyName);
    /// <summary>
    /// Binds this view model to a given model, such that updates from the view model are propagated to the model (but NOT vice versa).
    /// </summary>
    /// <param name="model">the model to bind to</param>
    public abstract void Bind(Model model);
}
