using QPlayer.SourceGenerator;
using QPlayer.ViewModels;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace QPlayer.LauncherPlugin
{

    [Model(typeof(LauncherModel))]
    [GenerateView]
    [View(typeof(LauncherViewModelView))]
    [DisplayName("Launcher Cue")]
    [Icon("IconLauncherCue", typeof(Icons))]
    public partial class LauncherViewModel : CueViewModel
    {
        [Reactive/*, ChangesProp(nameof(OSCMessageValid))*/]
        [Tooltip("The File that should be launched.")]
        private string fileName = "";

        [Reactive/*, ChangesProp(nameof(OSCMessageValid))*/]
        [Tooltip("The Arguments the files should be launched with.")]
        private string arguments = "";


        public LauncherViewModel(MainViewModel mainViewModel) : base(mainViewModel)
        { }

        public override async void Go()
        {
            base.Go();
            await BackgroundLauch();
        }

        private async Task BackgroundLauch()
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                MainViewModel.Log($"Failed to launch program. The file path is invalid: {fileName}", MainViewModel.LogLevel.Error);
                return;
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    Arguments = arguments,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true,
                };

                var proc = Process.Start(psi);
                if (proc is null) return;

                await proc.WaitForExitAsync();

                string errorOutput = await proc.StandardError.ReadToEndAsync();
                string standardOutput = await proc.StandardOutput.ReadToEndAsync();
                if (proc.ExitCode != 0)
                    throw new Exception("netsh exit code: " + proc.ExitCode.ToString() + " " + (!string.IsNullOrEmpty(errorOutput) ? " " + errorOutput : "") + " " + (!string.IsNullOrEmpty(standardOutput) ? " " + standardOutput : ""));

                MainViewModel.Log($"Successful Launch: {standardOutput}", MainViewModel.LogLevel.Info);

            }
            catch (Exception ex)
            {
                MainViewModel.Log($"Failed to launch program: {ex.Message}", MainViewModel.LogLevel.Error);
            }
        }

    }
}
