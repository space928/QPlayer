using QPlayer.Models;
using QPlayer.ViewModels;
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace QPlayer.MagicQCTRLPlugin;

[PluginName("MagicQCTRL Plugin")]
[PluginAuthor("Thomas Mathieson")]
[PluginDescription("This plugins allows the MagicQCTRL pad to directly control QPlayer.")]
public class MagicQCTRLPlugin : QPlayerPlugin
{
    private MainViewModel? vm;
    private USBDriver? driver = null;
    private MagicQCTRLProfile profile;
    private readonly SynchronizationContext syncContext;
    private readonly StringBuilder numpadNum = new();

    public MagicQCTRLPlugin()
    {
        CreateConfig();
        syncContext = SynchronizationContext.Current!;
    }

    public override void OnLoad(MainViewModel mainViewModel)
    {
        base.OnLoad(mainViewModel);
        this.vm = mainViewModel;

        Task.Run(MagicQCTRLTask);
    }

    private void MagicQCTRLTask()
    {
        while (true)
        {
            try
            {
                driver = new();
                var connected = driver.USBConnect();
                if (!connected)
                {
                    Thread.Sleep(500);
                    continue;
                }

                SendConfig();
                driver.OnMessageReceived += Driver_OnMessageReceived;

                while (driver.IsConnected)
                {
                    Thread.Sleep(100);
                }

                if (!driver.IsConnected)
                {
                    throw new Exception("USB disconnected!");
                }
            }
            catch (Exception ex)
            {
                driver?.OnMessageReceived -= Driver_OnMessageReceived;
                MainViewModel.Log($"MagicQCTRL disconnected due to an error: {ex.Message}", MainViewModel.LogLevel.Warning);
            }
        }
    }

    private void CreateConfig()
    {
        // Initialise the profile
        profile = new()
        {
            pressedBrightness = 1.25f,
            baseBrightness = 0.5f
        };
        profile.pages = new MagicQCTRLPage[USBDriver.MAX_PAGES];
        for (int p = 0; p < profile.pages.Length; p++)
        {
            profile.pages[p].keys = new MagicQCTRLKey[USBDriver.BUTTON_COUNT];
            for (int k = 0; k < USBDriver.BUTTON_COUNT; k++)
            {
                profile.pages[p].keys[k].name = string.Empty;
            }
        }

        // Define some keys
        SetKey(0, 0, 3, new()
        {
            name = "STOP",
            keyColourOn = new(255, 20, 5),
            onPress = Sync(() => vm?.StopExecute()),
        });

        SetKey(0, 2, 3, new()
        {
            name = "GO",
            keyColourOn = new(20, 255, 50),
            onPress = Sync(() => vm?.GoExecute()),
        });

        SetKey(0, 1, 0, new()
        {
            name = "Pause",
            keyColourOn = new(230, 190, 0),
            onPress = Sync(() => vm?.PauseExecute()),
        });

        SetKey(0, 2, 0, new()
        {
            name = "Play",
            keyColourOn = new(230, 190, 0),
            onPress = Sync(() => vm?.UnpauseExecute()),
        });

        SetKey(0, 2, 1, new()
        {
            name = "Prload",
            keyColourOn = new(0, 170, 180),
            onPress = Sync(() => vm?.PreloadExecute()),
        });

        SetKey(0, 0, 0, new()
        {
            name = "Up",
            keyColourOn = new(0, 20, 140),
            onPress = Sync(() =>
            {
                if (vm != null)
                    vm.SelectedCueInd--;
            }),
        });

        SetKey(0, 0, 1, new()
        {
            name = "Down",
            keyColourOn = new(0, 20, 140),
            onPress = Sync(() =>
            {
                if (vm != null)
                    vm.SelectedCueInd++;
            }),
        });

        // Numpad
        for (int y = 0; y < 3; y++)
        {
            for (int x = 0; x < 3; x++)
            {
                int n = (2 - y) * 3 + x + 1;
                SetKey(1, x, y, new()
                {
                    name = n.ToString(),
                    keyColourOn = new(60, 130, 150),
                    onPress = () => PressNum(n),
                });
            }
        }
        SetKey(1, 0, 3, new()
        {
            name = "0",
            keyColourOn = new(60, 130, 150),
            onPress = () => PressNum(0),
        });
        SetKey(1, 1, 3, new()
        {
            name = ".",
            keyColourOn = new(130, 130, 130),
            onPress = () => PressNum(-1),
        });
        SetKey(1, 2, 3, new()
        {
            name = "Select",
            keyColourOn = new(170, 130, 60),
            onPress = () => PressNum(-2),
        });

        // Top right encoder
        SetKey(0, 4, 4, new()
        {
            name = "Scroll",
            onRotate = SyncRot(delta => vm?.ScrollCueList(new(0, delta * 5f))),
        });

        // Volume/Pan - top left and right encoders
        SetKey(1, 0, 4, new()
        {
            name = "Volume",
            onRotate = SyncRot(delta =>
            {
                if (vm?.SelectedCue is SoundCueViewModel soundCue)
                    soundCue.Volume = Math.Clamp(soundCue.Volume + delta * 0.3f, -50, 50);
                else if (vm?.SelectedCue is VolumeCueViewModel volCue)
                    volCue.Volume = Math.Clamp(volCue.Volume + delta * 0.3f, -50, 50);
            }),
        });
        SetKey(1, 4, 4, new()
        {
            name = "Pan",
            onRotate = SyncRot(delta =>
            {
                if (vm?.SelectedCue is SoundCueViewModel soundCue)
                    soundCue.Pan = Math.Clamp(soundCue.Pan + delta * 0.025f, -1, 1);
            }),
        });

        SetKey(1, 3, 4, new()
        {
            name = "Start Time",
            onRotate = SyncRot(delta =>
            {
                if (vm?.SelectedCue is SoundCueViewModel soundCue)
                    soundCue.StartTime = AdjustTime(soundCue.StartTime, delta * 0.05f);
            }),
        });
        SetKey(1, 7, 4, new()
        {
            name = "Duration",
            onRotate = SyncRot(delta =>
            {
                if (vm?.SelectedCue is SoundCueViewModel soundCue)
                    soundCue.PlaybackDuration = AdjustTime(soundCue.PlaybackDuration, delta * 0.05f);
            }),
        });
        SetKey(0, 2, 4, new()
        {
            name = "Prload",
            onRotate = SyncRot(delta =>
            {
                vm?.PreloadTime = AdjustTime(vm.PreloadTime, delta * 0.1f);
            }),
        });
        SetKey(1, 2, 4, new()
        {
            name = "Prload",
            onRotate = SyncRot(delta =>
            {
                vm?.PreloadTime = AdjustTime(vm.PreloadTime, delta * 0.1f);
            }),
        });
        SetKey(1, 1, 4, new()
        {
            name = "Seek",
            onRotate = SyncRot(delta =>
            {
                if (vm?.SelectedCue is SoundCueViewModel soundCue)
                    soundCue.PlaybackTime = AdjustTime(soundCue.PlaybackTime, delta * 0.2f);
            }),
        });

        // EQ
        SetKey(2, 0, 4, new()
        {
            name = "EQ L Freq",
            onRotate = SyncRot(delta =>
            {
                if (vm?.SelectedCue is SoundCueViewModel soundCue)
                    soundCue.EQ.LowFreq = AdjustFreq(soundCue.EQ.LowFreq, delta);
            }),
        });
        SetKey(2, 1, 4, new()
        {
            name = "EQ LMFreq",
            onRotate = SyncRot(delta =>
            {
                if (vm?.SelectedCue is SoundCueViewModel soundCue)
                    soundCue.EQ.LowMidFreq = AdjustFreq(soundCue.EQ.LowMidFreq, delta);
            }),
        });
        SetKey(2, 2, 4, new()
        {
            name = "EQ HMFreq",
            onRotate = SyncRot(delta =>
            {
                if (vm?.SelectedCue is SoundCueViewModel soundCue)
                    soundCue.EQ.HighMidFreq = AdjustFreq(soundCue.EQ.HighMidFreq, delta);
            }),
        });
        SetKey(2, 3, 4, new()
        {
            name = "EQ H Freq",
            onRotate = SyncRot(delta =>
            {
                if (vm?.SelectedCue is SoundCueViewModel soundCue)
                    soundCue.EQ.HighFreq = AdjustFreq(soundCue.EQ.HighFreq, delta);
            }),
        });
        SetKey(2, 4, 4, new()
        {
            name = "EQ L Gain",
            onRotate = SyncRot(delta =>
            {
                if (vm?.SelectedCue is SoundCueViewModel soundCue)
                    soundCue.EQ.LowGain = Math.Clamp(soundCue.EQ.LowGain + delta * 0.2f, -20, 20);
            }),
        });
        SetKey(2, 5, 4, new()
        {
            name = "EQ LMGain",
            onRotate = SyncRot(delta =>
            {
                if (vm?.SelectedCue is SoundCueViewModel soundCue)
                    soundCue.EQ.LowMidGain = Math.Clamp(soundCue.EQ.LowMidGain + delta * 0.2f, -20, 20);
            }),
        });
        SetKey(2, 6, 4, new()
        {
            name = "EQ HMGain",
            onRotate = SyncRot(delta =>
            {
                if (vm?.SelectedCue is SoundCueViewModel soundCue)
                    soundCue.EQ.HighMidGain = Math.Clamp(soundCue.EQ.HighMidGain + delta * 0.2f, -20, 20);
            }),
        });
        SetKey(2, 7, 4, new()
        {
            name = "EQ H Gain",
            onRotate = SyncRot(delta =>
            {
                if (vm?.SelectedCue is SoundCueViewModel soundCue)
                    soundCue.EQ.HighGain = Math.Clamp(soundCue.EQ.HighGain + delta * 0.2f, -20, 20);
            }),
        });

        // Copy the on colours to the off colors
        for (int page = 0; page < USBDriver.MAX_PAGES; page++)
        {
            for (int id = 0; id < USBDriver.COLOUR_BUTTON_COUNT; id++)
                profile.pages[page].keys[id].keyColourOff = profile.pages[page].keys[id].keyColourOn;
        }

        void SetKey(int page, int x, int y, MagicQCTRLKey key)
        {
            profile.pages[page].keys[x + y * 3] = key;
        }

        Action Sync(Action action)
        {
            return () => syncContext.Post(_ => action(), null);
        }

        Action<sbyte> SyncRot(Action<sbyte> action)
        {
            return delta => syncContext.Post(_ => action(delta), null);
        }
    }

    private void PressNum(int i)
    {
        if (i >= 0)
        {
            numpadNum.Append(i);
        }
        else if (i == -1)
        {
            // Double pressing . clears
            if (numpadNum.Length > 0 && numpadNum[^1] == '.')
                numpadNum.Clear();
            else
                numpadNum.Append('.');
        }
        else if (i == -2)
        {
            // Select
            string num = numpadNum.ToString();
            syncContext.Post(_ =>
            {
                if (vm?.FindCue(num, out var cue) ?? false)
                    vm.SelectedCue = cue;
            }, null);
            MainViewModel.Log($"Selecting cue: {numpadNum}");
            numpadNum.Clear();
            return;
        }
        MainViewModel.Log($"Enter QID: {numpadNum}");
    }

    private void SendConfig()
    {
        if (driver == null)
            return;

        for (int page = 0; page < USBDriver.MAX_PAGES; page++)
        {
            for (int id = 0; id < USBDriver.COLOUR_BUTTON_COUNT; id++)
                driver.SendColourConfig(page, id, profile);
            for (int id = 0; id < USBDriver.BUTTON_COUNT; id++)
                driver.SendKeyNameMessage(page, id, profile);
        }
    }

    private void Driver_OnMessageReceived()
    {
        try
        {
            while (driver?.RXMessages?.TryDequeue(out var msg) ?? false)
            {
                MagicQCTRLKey key = default;
                sbyte delta = 0;
                switch (msg.msgType)
                {
                    case MagicQCTRLMessageType.Key:
                        if (msg.value == 1)
                            key = profile.pages[msg.page].keys[msg.keyCode];
                        break;
                    case MagicQCTRLMessageType.Button:
                        if (msg.value == 1)
                            key = profile.pages[msg.page].keys[msg.keyCode + USBDriver.COLOUR_BUTTON_COUNT];
                        break;
                    case MagicQCTRLMessageType.Encoder:
                        key = profile.pages[msg.page].keys[msg.keyCode + USBDriver.COLOUR_BUTTON_COUNT];
                        delta = (sbyte)-msg.delta;
                        break;
                    default:
                        break;
                }

                if (delta == 0)
                    key.onPress?.Invoke();
                else
                    key.onRotate?.Invoke(delta);
            }
        }
        catch (Exception ex)
        {
            MainViewModel.Log($"Error while processing MagicQCTRL command: {ex}");
        }
    }

    private static float AdjustFreq(float freq, float delta)
    {
        var val = UnApplyPower(freq, 20, 20000, 3);
        val += delta * 20;
        val = Math.Clamp(val, 20, 20000);
        return ApplyPower(val, 20, 20000, 3);
    }

    private static TimeSpan AdjustTime(TimeSpan x, float delta)
    {
        var ticks = x.Ticks;
        ticks = Math.Clamp(ticks + TimeSpan.FromSeconds(delta).Ticks, 0, long.MaxValue);
        return new(ticks);
    }

    private static float ApplyPower(float x, float min, float max, float power)
    {
        if (power == 1)
            return x;

        if (x >= 0)
        {
            min = MathF.Max(min, 0);
            x = (x - min) / (max - min);
            return MathF.Pow(x, power) * (max - min) + min;
        }
        else
        {
            max = MathF.Min(max, 0);
            x = 1 - (x - min) / (max - min);
            return (1 - MathF.Pow(x, power)) * (max - min) + min;
        }
    }

    private static float UnApplyPower(float x, float min, float max, float power)
    {
        if (power == 1)
            return x;

        if (x >= 0)
        {
            min = MathF.Max(min, 0);
            x = (x - min) / (max - min);
            return MathF.Pow(x, 1 / power) * (max - min) + min;
        }
        else
        {
            max = MathF.Min(max, 0);
            x = 1 - (x - min) / (max - min);
            return (1 - MathF.Pow(x, 1 / power)) * (max - min) + min;
        }
    }
}
