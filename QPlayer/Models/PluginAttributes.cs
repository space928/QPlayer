using System;
using System.Collections.Generic;
using System.Text;

namespace QPlayer.Models;

/// <summary>
/// Using this attribute on a plugin class implementing <see cref="QPlayerPlugin"/> allows a custom plugin name to be specified.
/// </summary>
/// <param name="name"></param>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class PluginNameAttribute(string name) : Attribute
{
    public string Name => name;
}

/// <summary>
/// Using this attribute on a plugin class implementing <see cref="QPlayerPlugin"/> allows a custom plugin author to be specified.
/// </summary>
/// <param name="name"></param>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class PluginAuthorAttribute(string name) : Attribute
{
    public string Name => name; 
}

/// <summary>
/// Using this attribute on a plugin class implementing <see cref="QPlayerPlugin"/> allows a custom plugin description to be specified.
/// </summary>
/// <param name="description"></param>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class PluginDescriptionAttribute(string description) : Attribute
{
    public string Description => description;
}

/// <summary>
/// Creates a main menu item which invokes this method when clicked.
/// <para/>
/// Only applicable to parameterless methods and <see langword="bool"/> properties on the class implementing <see cref="QPlayerPlugin"/>.
/// </summary>
/// <param name="path">The path to the menu item to be created, eg: 'File/Save'</param>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class MenuItemAttribute(string path) : Attribute
{
    public string Path => path;
}

/// <summary>
/// Registers this class to the project settings panel.
/// <para/>
/// This class must derive from <see cref="QPlayer.ViewModels.BindableViewModel{Model}"/> and be annotated with the 
/// <see cref="SourceGenerator.ModelAttribute"/> and <see cref="SourceGenerator.ViewAttribute"/> attributes.
/// </summary>
/// <param name="heading">The heading name for this section in the project settings panel.</param>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ProjectSettingsAttribute(string? heading = null) : Attribute
{
    public string? Heading => heading;
}
