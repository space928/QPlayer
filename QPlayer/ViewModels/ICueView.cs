using System.Windows;

namespace QPlayer.ViewModels;

public interface ICueView
{
    static abstract DataTemplate CreateDataTemplate();
    //static abstract Type GenerateView();
}
