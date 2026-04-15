using CommunityToolkit.Mvvm.ComponentModel;

namespace QPlayer.ViewModels;

public partial class ObservableStruct<T> : ObservableObject where T : struct
{
    public T Value
    {
        get => value;
        set => SetProperty(ref this.value, value);
    }
    private T value;

    public ObservableStruct() { }

    public ObservableStruct(T value)
    {
        Value = value;
    }
}
