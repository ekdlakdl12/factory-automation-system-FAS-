using factory_automation_system_FAS_.ViewModels; //  이 줄 건들면안됌
using System.Windows;

namespace factory_automation_system_FAS_.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            // MainViewModel을 DataContext로 설정
            this.DataContext = new MainViewModel();
        }
    }
}