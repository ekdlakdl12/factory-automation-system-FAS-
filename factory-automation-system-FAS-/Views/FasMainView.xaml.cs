using System.Windows;
using System.Windows.Controls;

namespace factory_automation_system_FAS_.Views
{
    public partial class FasMainView : UserControl
    {
        public FasMainView()
        {
            InitializeComponent();
            // DataContext = this; 를 삭제해야 MainWindow에서 넣어주는 ViewModel이 작동합니다.
        }

        private void BtnLogSearch_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show($"조회 기간: {TxtLogDateStart.Text} ~ {TxtLogDateEnd.Text}\n조회를 시작합니다.");
        }

        private void BtnIpConfirm_Click(object sender, RoutedEventArgs e) => MessageBox.Show("IP 설정이 저장되었습니다.");
        private void BtnConnCheck_Click(object sender, RoutedEventArgs e) => MessageBox.Show("장치 연결 상태를 갱신합니다.");

        private void BtnAccConfirm_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(AccName.Text))
            {
                UserList.Items.Add($"{UserList.Items.Count + 1}  {AccName.Text}  {AccAuth.Text}");
                AccName.Clear(); AccPw.Clear(); AccAuth.Clear();
            }
        }

        private void ShowMain_Click(object sender, RoutedEventArgs e) { SetSection(Visibility.Visible, Visibility.Collapsed, Visibility.Collapsed, sender); }
        private void ShowLog_Click(object sender, RoutedEventArgs e) { SetSection(Visibility.Collapsed, Visibility.Visible, Visibility.Collapsed, sender); }
        private void ShowSetting_Click(object sender, RoutedEventArgs e) { SetSection(Visibility.Collapsed, Visibility.Collapsed, Visibility.Visible, sender); }

        private void SetSection(Visibility main, Visibility log, Visibility set, object sender)
        {
            MainMonitorSection.Visibility = main;
            LogSection.Visibility = log;
            SettingSection.Visibility = set;
            UpdateMenuButtons(sender as Button);
        }

        private void UpdateMenuButtons(Button? selectedButton)
        {
            if (selectedButton == null) return;
            BtnShowMain.Background = BtnShowLog.Background = BtnShowSetting.Background = System.Windows.Media.Brushes.White;
            BtnShowMain.Foreground = BtnShowLog.Foreground = BtnShowSetting.Foreground = System.Windows.Media.Brushes.Black;
            selectedButton.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(74, 118, 197));
            selectedButton.Foreground = System.Windows.Media.Brushes.White;
        }
    }
}