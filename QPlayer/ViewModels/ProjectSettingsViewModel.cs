using CommunityToolkit.Mvvm.ComponentModel;
using QPlayer.Models;
using ReactiveUI.Fody.Helpers;
using System;
using System.Collections.Generic;
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
        #endregion

        private readonly MainViewModel mainViewModel;
        private ShowMetadata? projectSettings;

        public ProjectSettingsViewModel(MainViewModel mainViewModel) 
        {
            this.mainViewModel = mainViewModel;
        }

        public static ProjectSettingsViewModel FromModel(ShowMetadata model, MainViewModel mainViewModel)
        {
            ProjectSettingsViewModel ret = new(mainViewModel);
            ret.Title = model.title;
            ret.Description = model.description;
            ret.Author = model.author;
            ret.Date = model.date;

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
                default:
                    throw new ArgumentException(null, nameof(propertyName));
            }
        }
    }
}
