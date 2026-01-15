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

            // XAML에서 DataContext를 만들었다면 이 줄은 제거해도 됨.
            DataContext = new MainViewModel();
        }
    }
}
