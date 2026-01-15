// ViewModels/MainViewModel.cs
using factory_automation_system_FAS_.Models;
using factory_automation_system_FAS_.Utils;
using System.Windows.Input;

namespace factory_automation_system_FAS_.ViewModels
{
    public sealed class MainViewModel
    {
        public MapViewportModel Viewport { get; } = new();

        public ICommand ZoomCommand { get; }
        public ICommand BeginPanCommand { get; }
        public ICommand PanCommand { get; }
        public ICommand EndPanCommand { get; }
        public ICommand ResetViewCommand { get; }

        public MainViewModel()
        {
            ZoomCommand = new RelayCommand<ZoomArgs>(a => Viewport.ZoomAt(a.Delta, a.MousePosOnCanvas));
            BeginPanCommand = new RelayCommand<PanArgs>(a => Viewport.BeginPan(a.MousePosOnHost));
            PanCommand = new RelayCommand<PanArgs>(a => Viewport.PanTo(a.MousePosOnHost));
            EndPanCommand = new RelayCommand(() => Viewport.EndPan());
            ResetViewCommand = new RelayCommand(() => Viewport.Reset());
        }
    }
}
