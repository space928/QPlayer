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
