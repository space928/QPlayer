using QPlayer.Models;
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
        RegisterAssembly(Assembly.GetExecutingAssembly());
    }

    public static ICollection<RegisteredCueType> RegisteredCueTypes => registeredCueTypes.Values;

    public static Cue? CreateCue(string typeName)
    {
        if (registeredCueTypes.TryGetValue(typeName, out var registered))
            return Activator.CreateInstance(registered.modelType) as Cue;
        return null;
    }

    public static CueViewModel? CreateViewModel(string typeName, MainViewModel mainViewModel)
    {
        if (registeredCueTypes.TryGetValue(typeName, out var registered))
            return Activator.CreateInstance(registered.viewModelType, mainViewModel) as CueViewModel;
        return null;
    }

    public static CueViewModel? CreateViewModelForCue(Cue cue, MainViewModel mainViewModel)
    {
        var vm = CreateViewModel(cue.GetType().Name, mainViewModel);
        if (vm == null)
            return null;

        vm.Bind(cue);
        vm.SyncFromModel();

        return vm;
    }

    public static Cue? CreateCueForViewModel(CueViewModel vm)
    {
        var vmType = vm.GetType();
        if (vmType.GetCustomAttribute<ModelAttribute>() is not ModelAttribute modelAttr)
            return null;

        var cue = Activator.CreateInstance(modelAttr.ModelType) as Cue;
        vm.Bind(cue);
        vm.SyncToModel();

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
            else
            {
                viewType = ViewGenerator.GenerateView(vmType);
            }

            modelType = modelAttr.ModelType;

            RegisteredCueType cueDetails = new(modelType.Name, modelType, vmType, viewType);

            registeredCueTypes.Add(cueDetails.name, cueDetails);
            viewModelToCueType.Add(cueDetails.viewModelType, cueDetails);
            registered.Add(cueDetails);
        }

        MainViewModel.Log($"Registered {registered.Count} cue types from {assembly.FullName}.");
        return registered.ToArray();
    }

    public readonly struct RegisteredCueType(string name, Type modelType, Type viewModelType, Type viewType)
    {
        public readonly string name = name;
        public readonly Type modelType = modelType;
        public readonly Type viewModelType = viewModelType;
        public readonly Type viewType = viewType;
    }
}

internal static class ViewGenerator
{
    public static Type GenerateView(Type vmType)
    {
        //Xam
        return typeof(object);
    }
}
