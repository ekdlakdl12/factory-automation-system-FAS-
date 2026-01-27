using System.Windows;
using System.Windows.Controls;

namespace factory_automation_system_FAS_.Views
{
    // 1. partial 키워드가 있는지 확인
    // 2. 클래스 이름이 MainView 인지 확인
    // 3. 부모 클래스가 UserControl 인지 확인
    public partial class MainView : UserControl
    {
        public MainView()
        {
            InitializeComponent();
        }
        private void ShowMain_Click(object sender, RoutedEventArgs e)
        {
            MainMonitorSection.Visibility = Visibility.Visible;
            LogSection.Visibility = Visibility.Collapsed;
            SettingSection.Visibility = Visibility.Collapsed;
        }

        private void ShowLog_Click(object sender, RoutedEventArgs e)
        {
            MainMonitorSection.Visibility = Visibility.Collapsed;
            LogSection.Visibility = Visibility.Visible;
            SettingSection.Visibility = Visibility.Collapsed;
        }

        private void ShowSetting_Click(object sender, RoutedEventArgs e)
        {
            MainMonitorSection.Visibility = Visibility.Collapsed;
            LogSection.Visibility = Visibility.Collapsed;
            SettingSection.Visibility = Visibility.Visible;
        }
    }
}