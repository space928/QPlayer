using QPlayer.Models;
using QPlayer.SourceGenerator;
using QPlayer.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace QPlayer.ViewModels;

public static class CueFactory
{
    private static readonly StringDict<RegisteredCueType> registeredCueTypes = [];
    private static readonly Dictionary<Type, RegisteredCueType> viewModelToCueType = [];
    private static readonly ReadOnlyDictionary<Type, RegisteredCueType> viewModelToCueTypeRO;

    public static ReadOnlyDictionary<Type, RegisteredCueType> ViewModelToCueType => viewModelToCueTypeRO;

    static CueFactory()
    {
        viewModelToCueTypeRO = new(viewModelToCueType);
    }

    public static ICollection<RegisteredCueType> RegisteredCueTypes => registeredCueTypes.Values;

    /// <summary>
    /// Creates a new instance of a cue of the given type. Available cue types are those registered in <see cref="RegisteredCueTypes"/>.
    /// </summary>
    /// <param name="typeName"></param>
    /// <returns></returns>
    public static Cue? CreateCue(string typeName)
    {
        if (registeredCueTypes.TryGetValue(typeName, out var registered))
            return Activator.CreateInstance(registered.modelType) as Cue;
        return null;
    }

    /// <summary>
    /// Creates a new instance of a cue view model of the given type. Available cue types are those registered in <see cref="RegisteredCueTypes"/>.
    /// End users should preferentially use <see cref="MainViewModel.CreateCue(string?, int, bool, CueViewModel?)"/>.
    /// </summary>
    /// <param name="typeName"></param>
    /// <param name="mainViewModel"></param>
    /// <returns></returns>
    public static CueViewModel? CreateViewModel(string typeName, MainViewModel mainViewModel)
    {
        if (registeredCueTypes.TryGetValue(typeName, out var registered))
            return Activator.CreateInstance(registered.viewModelType, mainViewModel) as CueViewModel;
        return null;
    }

    /// <summary>
    /// Creates a new instance of a cue view model for a given cue and copies all of it's properties.
    /// </summary>
    /// <param name="cue"></param>
    /// <param name="mainViewModel"></param>
    /// <returns></returns>
    public static CueViewModel? CreateViewModelForCue(Cue cue, MainViewModel mainViewModel)
    {
        var vm = CreateViewModel(cue.GetType().Name, mainViewModel);
        if (vm == null)
            return null;

        vm.Bind(cue);
        vm.SyncFromModel();

        return vm;
    }

    /// <summary>
    /// Creates a new instance of a cue for a given cue view modeland copies all of it's properties.
    /// </summary>
    /// <param name="vm">The view model to create a model for.</param>
    /// <param name="copy"><see langword="false"/> to bind the <paramref name="vm"/> to the newly created 
    /// model, <see langword="true"/> to only copy it's parameter.</param>
    /// <returns></returns>
    public static Cue? CreateCueForViewModel(CueViewModel vm, bool copy = false)
    {
        var vmType = vm.GetType();
        if (vmType.GetCustomAttribute<ModelAttribute>() is not ModelAttribute modelAttr)
            return null;

        var cue = Activator.CreateInstance(modelAttr.ModelType) as Cue;
        var oldModel = vm.BoundModel;
        vm.Bind(cue);
        vm.SyncToModel();

        // In copy mode, restore the original binding
        if (copy)
            vm.Bind(oldModel);

        return cue;
    }

    internal static RegisteredCueType[] RegisterAssembly(Assembly assembly)
    {
        var vmBaseType = typeof(CueViewModel);
        var mBaseType = typeof(Cue);
        var types = assembly.GetTypes();
        var vmTypes = types.Where(vmBaseType.IsAssignableFrom);
        List<RegisteredCueType> registered = [];

        foreach (var vmType in vmTypes)
        {
            if (vmType == vmBaseType)
                continue;

            Type modelType;
            Type viewType;

            if (vmType.GetCustomAttribute<ModelAttribute>() is not ModelAttribute modelAttr)
            {
                MainViewModel.Log($"failed to register cue type '{vmType.Name}' as it does not specify an associated model type. " +
                    $"(See the [Model(...)] attribute for details.)", MainViewModel.LogLevel.Error);
                continue;
            }

            if (vmType.GetCustomAttribute<ViewAttribute>() is ViewAttribute viewAttr)
            {
                viewType = viewAttr.ViewType;
            }
            /*else if (vmType.GetCustomAttribute<GenerateViewAttribute>() is GenerateViewAttribute generateViewAttr)
            {
                viewType = vmType.Assembly.GetType(generateViewAttr.TypeName ?? (vmType.Name + "View"), true) ?? throw new Exception();
            }*/
            else
            {
                throw new Exception();
            }

            modelType = modelAttr.ModelType;
            string name = modelType.Name;
            string displayName = name;

            if (vmType.GetCustomAttribute<DisplayNameAttribute>() is DisplayNameAttribute displayNameAttr)
                displayName = displayNameAttr.Name;

            var icon = vmType.GetCustomAttribute<IconAttribute>();

            RegisteredCueType cueDetails = new(modelType.Name, displayName, modelType, vmType, viewType, assembly.FullName ?? string.Empty, icon?.Name, icon?.ResourceDictionary);

            registeredCueTypes.Add(cueDetails.name, cueDetails);
            viewModelToCueType.Add(cueDetails.viewModelType, cueDetails);
            registered.Add(cueDetails);
        }

        MainViewModel.Log($"Registered {registered.Count} cue types from {assembly.FullName}.");
        return registered.ToArray();
    }

    public readonly struct RegisteredCueType(string name, string displayName, Type modelType, Type viewModelType, 
        Type viewType, string assembly, string? iconName, Type? iconResourceDict)
    {
        public readonly string name = name;
        public readonly string displayName = displayName;
        public readonly Type modelType = modelType;
        public readonly Type viewModelType = viewModelType;
        public readonly Type viewType = viewType;
        public readonly string assembly = assembly;
        public readonly string? iconName = iconName;
        public readonly Type? iconResourceDict = iconResourceDict;
    }
}
