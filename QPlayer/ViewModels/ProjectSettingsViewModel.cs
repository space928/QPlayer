using CommunityToolkit.Mvvm.ComponentModel;
using QPlayer.Models;
using ReactiveUI.Fody.Helpers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QPlayer.ViewModels
{
    public class ProjectSettingsViewModel : ObservableObject, IConvertibleModel<ShowMetadata, ProjectSettingsViewModel>
    {
        #region Bindable Properties
        [Reactive] public string Title { get; set; } = "Untitled";
        [Reactive] public string Description { get; set; } = string.Empty;
        [Reactive] public string Author { get; set; } = string.Empty;
        [Reactive] public DateTime Date { get; set; }

        [Reactive] public AudioOutputDriver AudioOutputDriver { get; set; }
        [Reactive] public int SelectedAudioOutputDeviceIndex { get; set; }

        [Reactive] public static ObservableCollection<AudioOutputDriver>? AudioOutputDriverValues { get; private set; }
        [Reactive, ReactiveDependency(nameof(AudioOutputDriver))]
        public ObservableCollection<string> AudioOutputDevices
        {
            get
            {
                audioOutputDevices = mainViewModel.AudioPlaybackManager.GetOutputDevices(AudioOutputDriver);
                return new(audioOutputDevices.Select(x=>x.identifier));
            } 
        }
        #endregion

        private (object key, string identifier)[] audioOutputDevices = Array.Empty<(object key, string identifier)>();
        private readonly MainViewModel mainViewModel;
        private ShowMetadata? projectSettings;

        public ProjectSettingsViewModel(MainViewModel mainViewModel) 
        {
            this.mainViewModel = mainViewModel;
            AudioOutputDriverValues ??= new(Enum.GetValues<AudioOutputDriver>());
            PropertyChanged += (o, e) =>
            {
                switch (e.PropertyName)
                {
                    case nameof(SelectedAudioOutputDeviceIndex):
                        if(SelectedAudioOutputDeviceIndex >= 0 && SelectedAudioOutputDeviceIndex < audioOutputDevices.Length)
                            mainViewModel.AudioPlaybackManager.OpenOutputDevice(
                                AudioOutputDriver, 
                                audioOutputDevices[SelectedAudioOutputDeviceIndex].key);
                        break;
                }
            };

            _ = AudioOutputDevices; // Update the list of audio devices...
            if(SelectedAudioOutputDeviceIndex < audioOutputDevices.Length)
                mainViewModel.AudioPlaybackManager.OpenOutputDevice(
                    AudioOutputDriver, audioOutputDevices[SelectedAudioOutputDeviceIndex].key);
        }

        public static ProjectSettingsViewModel FromModel(ShowMetadata model, MainViewModel mainViewModel)
        {
            ProjectSettingsViewModel ret = new(mainViewModel);
            ret.Title = model.title;
            ret.Description = model.description;
            ret.Author = model.author;
            ret.Date = model.date;

            ret.AudioOutputDriver = model.audioOutputDriver;
            ret.SelectedAudioOutputDeviceIndex = ret.AudioOutputDevices.IndexOf(model.audioOutputDevice);

            return ret;
        }

        public void Bind(ShowMetadata model)
        {
            projectSettings = model;
            PropertyChanged += (o, e) =>
            {
                ProjectSettingsViewModel vm = (ProjectSettingsViewModel)(o ?? throw new NullReferenceException(nameof(ProjectSettingsViewModel)));
                if (e.PropertyName != null)
                    vm.ToModel(e.PropertyName);
            };
        }

        public void ToModel(ShowMetadata model)
        {
            model.title = Title;
            model.description = Description;
            model.author = Author;
            model.date = Date;

            model.audioOutputDriver = AudioOutputDriver;
            var devices = AudioOutputDevices;
            model.audioOutputDevice = devices[Math.Clamp(SelectedAudioOutputDeviceIndex, 0, devices.Count)];
        }

        public void ToModel(string propertyName)
        {
            if (projectSettings == null)
                return;
            switch(propertyName)
            {
                case nameof(Title):
                    projectSettings.title = Title;
                    break;
                case nameof(Description):
                    projectSettings.description = Description;
                    break;
                case nameof(Author): 
                    projectSettings.author = Author;
                    break;
                case nameof(Date):
                    projectSettings.date = Date;
                    break;
                case nameof(AudioOutputDriver):
                    projectSettings.audioOutputDriver = AudioOutputDriver;
                    break;
                case nameof(SelectedAudioOutputDeviceIndex):
                    projectSettings.audioOutputDevice = AudioOutputDevices[SelectedAudioOutputDeviceIndex];
                    break;
                case nameof(AudioOutputDevices):
                case nameof(AudioOutputDriverValues):
                    break;
                default:
                    throw new ArgumentException($"Couldn't convert property {propertyName} to model!", nameof(propertyName));
            }
        }
    }
}
