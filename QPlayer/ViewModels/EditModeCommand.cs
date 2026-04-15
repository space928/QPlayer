using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace QPlayer.ViewModels;

public class EditModeCommand : IRelayCommand
{
    private readonly Action execute;
    private readonly MainViewModel vm;

    public event EventHandler? CanExecuteChanged;

    public EditModeCommand(Action execute, MainViewModel vm)
    {
        this.execute = execute;
        this.vm = vm;
        vm.ShowModeChanged += NotifyCanExecuteChanged;
    }

    ~EditModeCommand()
    {
        vm.ShowModeChanged -= NotifyCanExecuteChanged;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool CanExecute(object? parameter) => vm.EditMode;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Execute(object? parameter)
    {
        execute();
    }

    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public class EditModeCommand<T> : IRelayCommand<T>
{
    private readonly Action<T?> execute;
    private readonly MainViewModel vm;

    public event EventHandler? CanExecuteChanged;

    public EditModeCommand(Action<T?> execute, MainViewModel vm)
    {
        this.execute = execute;
        this.vm = vm;
        vm.ShowModeChanged += NotifyCanExecuteChanged;
    }

    ~EditModeCommand()
    {
        vm.ShowModeChanged -= NotifyCanExecuteChanged;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool CanExecute(object? parameter) => vm.EditMode;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Execute(object? parameter)
    {
        if (parameter is T || parameter == null)
            execute((T?)parameter);
    }

    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool CanExecute(T? parameter) => vm.EditMode;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Execute(T? parameter)
    {
        execute(parameter);
    }
}
