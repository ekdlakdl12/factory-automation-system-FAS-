using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace factory_automation_system_FAS_.Views
{
    public partial class FasMainView : UserControl, INotifyPropertyChanged
    {
        // 속성 정의 (온도, 습도, CO2)
        private string _temp = "0";
        private string _humi = "0";
        private string _co2 = "0";

        public string Temp
        {
            get => _temp;
            set { _temp = value; OnPropertyChanged(); }
        }

        public string Humi
        {
            get => _humi;
            set { _humi = value; OnPropertyChanged(); }
        }

        public string Co2
        {
            get => _co2;
            set { _co2 = value; OnPropertyChanged(); }
        }

        public FasMainView()
        {
            InitializeComponent();
            this.DataContext = this;
        }

        // --- 이벤트 핸들러 영역 ---

        // 로그 조회 버튼 클릭 (에러 발생했던 부분)
        private void BtnLogSearch_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show($"조회 기간: {TxtLogDateStart.Text} ~ {TxtLogDateEnd.Text}\n조회를 시작합니다.");
            // 여기에 DB 조회 로직을 추가하면 됩니다.
        }

        // IP 설정 확인 버튼
        private void BtnIpConfirm_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("IP 설정이 저장되었습니다.");
        }

        // 디바이스 연결 확인 버튼
        private void BtnConnCheck_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("장치 연결 상태를 갱신합니다.");
        }

        // 계정 추가 버튼
        private void BtnAccConfirm_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(AccName.Text))
            {
                UserList.Items.Add($"{UserList.Items.Count + 1}  {AccName.Text}  {AccAuth.Text}");
                AccName.Clear();
                AccPw.Clear();
                AccAuth.Clear();
            }
        }

        // --- 섹션 전환 버튼 영역 ---

        private void ShowMain_Click(object sender, RoutedEventArgs e)
        {
            MainMonitorSection.Visibility = Visibility.Visible;
            LogSection.Visibility = Visibility.Collapsed;
            SettingSection.Visibility = Visibility.Collapsed;
            UpdateMenuButtons(sender as Button);
        }

        private void ShowLog_Click(object sender, RoutedEventArgs e)
        {
            MainMonitorSection.Visibility = Visibility.Collapsed;
            LogSection.Visibility = Visibility.Visible;
            SettingSection.Visibility = Visibility.Collapsed;
            UpdateMenuButtons(sender as Button);
        }

        private void ShowSetting_Click(object sender, RoutedEventArgs e)
        {
            MainMonitorSection.Visibility = Visibility.Collapsed;
            LogSection.Visibility = Visibility.Collapsed;
            SettingSection.Visibility = Visibility.Visible;
            UpdateMenuButtons(sender as Button);
        }

        // 메뉴 버튼 색상 강조 로직
        private void UpdateMenuButtons(Button? selectedButton)
        {
            if (selectedButton == null) return;

            BtnShowMain.Background = System.Windows.Media.Brushes.White;
            BtnShowMain.Foreground = System.Windows.Media.Brushes.Black;
            BtnShowLog.Background = System.Windows.Media.Brushes.White;
            BtnShowLog.Foreground = System.Windows.Media.Brushes.Black;
            BtnShowSetting.Background = System.Windows.Media.Brushes.White;
            BtnShowSetting.Foreground = System.Windows.Media.Brushes.Black;

            selectedButton.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(74, 118, 197));
            selectedButton.Foreground = System.Windows.Media.Brushes.White;
        }

        // MVVM PropertyChanged
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null!)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}