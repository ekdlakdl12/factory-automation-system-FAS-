// Views/MainWindow.xaml.cs
using System.Windows;
using factory_automation_system_FAS_.ViewModels;
using System.ComponentModel;

namespace factory_automation_system_FAS_.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

           
            DataContext = new MainViewModel();
        }
        private void MainWindow_OnClosing(object? sender, CancelEventArgs e)
        {
            if (DataContext is factory_automation_system_FAS_.ViewModels.MainViewModel vm)
            {
                vm.ExportMapEntitiesToJson();
            }
        }

    }
}
