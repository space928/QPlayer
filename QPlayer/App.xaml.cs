using QPlayer.Views;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace QPlayer;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public App()
    {
        QPlayerSplashScreen splashScreen = new("QPlayer.Resources.SplashV2.png");
        splashScreen.Show(true);
    }
}
