// Views/MainWindow.xaml.cs
using System.Windows;
using factory_automation_system_FAS_.ViewModels;

namespace factory_automation_system_FAS_.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

           
            DataContext = new MainViewModel();
        }
    }
}
