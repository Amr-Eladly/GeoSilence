using CommunityToolkit.Mvvm.Input;
using GeoSilence.Models;

namespace GeoSilence.PageModels
{
    public interface IProjectTaskPageModel
    {
        IAsyncRelayCommand<ProjectTask> NavigateToTaskCommand { get; }
        bool IsBusy { get; }
    }
}