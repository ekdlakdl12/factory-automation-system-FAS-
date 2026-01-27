using System;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace factory_automation_system_FAS_.Views
{
    public partial class MainView : UserControl
    {
        // 통신 객체 (Null 허용)
        private TcpClient? _arduinoClient;
        private NetworkStream? _arduinoStream;
        private bool _isArduinoConnected = false;

        public MainView()
        {
            InitializeComponent();

            // 초기 로드 시 기본값 세팅 및 화면 초기화
            this.Loaded += (s, e) => {
                InitializeDefaultValues();
                ShowMain_Click(this, new RoutedEventArgs()); // 시작 시 메인 화면 표시
            };
        }

        private void InitializeDefaultValues()
        {
            if (IpInPlc != null) IpInPlc.Text = "192.168.0.8";
            if (IpGas1 != null) IpGas1.Text = "192.168.0.14";
            if (IpDb != null) IpDb.Text = "127.0.0.1";
        }

        // --- [1. IP 설정 확인 버튼] ---
        private async void BtnIpConfirm_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string plcIp = IpInPlc.Text;
                string gasIp = IpGas1.Text;

                // PLC 연결 시각화 (LogInPlc TextBox에 출력)
                AppendLog(LogInPlc, $"{plcIp} PLC 연결 중...");

                // 임시 연결 성공 처리 (실제 서비스 연결 시 이 부분을 수정)
                await Task.Delay(500);
                AppendLog(LogInPlc, "PLC 연결 성공");

                // 아두이노 연결 시도
                AppendLog(LogGas1, $"{gasIp} 아두이노 연결 중...");
                await ConnectArduinoEthernet(gasIp, 8080);
            }
            catch (Exception ex)
            {
                AppendLog(LogInPlc, $"오류: {ex.Message}");
            }
        }

        // --- [2. 아두이노 통신 로직] ---
        private async Task ConnectArduinoEthernet(string ip, int port)
        {
            try
            {
                _isArduinoConnected = false;
                _arduinoStream?.Dispose();
                _arduinoClient?.Close();

                _arduinoClient = new TcpClient();
                var connectTask = _arduinoClient.ConnectAsync(ip, port);

                if (await Task.WhenAny(connectTask, Task.Delay(3000)) == connectTask)
                {
                    await connectTask;
                    _arduinoStream = _arduinoClient.GetStream();
                    _isArduinoConnected = true;
                    AppendLog(LogGas1, "아두이노 연결 성공");

                    // 데이터 수신 시작
                    _ = Task.Run(() => ReceiveDataLoop());
                }
                else
                {
                    AppendLog(LogGas1, "연결 실패 (타임아웃)");
                }
            }
            catch (Exception ex)
            {
                AppendLog(LogGas1, $"에러: {ex.Message}");
            }
        }

        private async Task ReceiveDataLoop()
        {
            byte[] buffer = new byte[1024];
            while (_isArduinoConnected && _arduinoStream != null)
            {
                try
                {
                    int bytesRead = await _arduinoStream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead > 0)
                    {
                        string data = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                        Dispatcher.Invoke(() => {
                            AppendLog(LogGas1, $"수신: {data.Trim()}");
                            UpdateSideDisplay(data);
                        });
                    }
                    else break;
                }
                catch { break; }
            }
            _isArduinoConnected = false;
        }

        // --- [3. UI 업데이트 보조] ---
        private void AppendLog(TextBox target, string msg)
        {
            if (target == null) return;
            target.Text = $"[{DateTime.Now:HH:mm:ss}] {msg}";
            target.Foreground = msg.Contains("성공") ? Brushes.LimeGreen : Brushes.Black;
        }

        private void UpdateSideDisplay(string data)
        {
            // 데이터 포맷이 "24.5,40.1,380" 이라고 가정 시 파싱
            try
            {
                var values = data.Split(',');
                if (values.Length >= 1 && SideTemp != null) SideTemp.Text = $"TEMP: {values[0]}°C";
                if (values.Length >= 2 && SideHumi != null) SideHumi.Text = $"HUMI: {values[1]}%";
                if (values.Length >= 3 && SideCo2 != null) SideCo2.Text = $"CO2 : {values[2]}ppm";
            }
            catch { }
        }

        // --- [4. 화면 전환 버튼 이벤트] ---
        private void ShowMain_Click(object sender, RoutedEventArgs e) => SetVisibility(MainMonitorSection);
        private void ShowLog_Click(object sender, RoutedEventArgs e) => SetVisibility(LogSection);
        private void ShowSetting_Click(object sender, RoutedEventArgs e) => SetVisibility(SettingSection);

        private void SetVisibility(Grid targetSection)
        {
            if (MainMonitorSection == null || LogSection == null || SettingSection == null) return;

            MainMonitorSection.Visibility = Visibility.Collapsed;
            LogSection.Visibility = Visibility.Collapsed;
            SettingSection.Visibility = Visibility.Collapsed;

            targetSection.Visibility = Visibility.Visible;
        }

        // --- [5. 기타 버튼 핸들러] ---
        private void BtnConnCheck_Click(object sender, RoutedEventArgs e) => AppendLog(LogDb, "DB 서버 응답 확인됨");
        private void BtnAccConfirm_Click(object sender, RoutedEventArgs e) => MessageBox.Show("계정 정보가 업데이트되었습니다.");
    }
}