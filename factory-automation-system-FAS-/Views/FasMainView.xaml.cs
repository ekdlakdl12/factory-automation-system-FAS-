using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace factory_automation_system_FAS_.Views
{
    public partial class FasMainView : UserControl, INotifyPropertyChanged
    {
        private string _temp = "0.0";
        private string _humi = "0";
        private string _co2 = "0";

        public string Temp { get => _temp; set { _temp = value; OnPropertyChanged(); } }
        public string Humi { get => _humi; set { _humi = value; OnPropertyChanged(); } }
        public string Co2 { get => _co2; set { _co2 = value; OnPropertyChanged(); } }

        public FasMainView()
        {
            InitializeComponent();
            this.DataContext = this;
        }

        // CS8612 경고 해결: Nullable 이벤트 핸들러 선언
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        // 네비게이션 버튼 이벤트
        private void ShowMain_Click(object sender, RoutedEventArgs e) => SetSection(MainMonitorSection);
        private void ShowLog_Click(object sender, RoutedEventArgs e) => SetSection(LogSection);
        private void ShowSetting_Click(object sender, RoutedEventArgs e) => SetSection(SettingSection);

        private void SetSection(Grid activeSection)
        {
            MainMonitorSection.Visibility = Visibility.Collapsed;
            LogSection.Visibility = Visibility.Collapsed;
            SettingSection.Visibility = Visibility.Collapsed;
            activeSection.Visibility = Visibility.Visible;
        }

        // XAML에서 호출하는 이벤트 메서드들 (CS1061 해결)
        private void BtnIpConfirm_Click(object sender, RoutedEventArgs e) => MessageBox.Show("IP 설정 저장");
        private void BtnConnCheck_Click(object sender, RoutedEventArgs e) => MessageBox.Show("연결 확인");
        private void BtnAccConfirm_Click(object sender, RoutedEventArgs e) => MessageBox.Show("계정 확인");
    }
}