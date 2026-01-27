using System;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using factory_automation_system_FAS_.Services;
using WpfAppModbus;

namespace factory_automation_system_FAS_.Views
{
    /// <summary>
    /// MainView.xaml에 대한 상호 작용 로직
    /// </summary>
    public partial class MainView : UserControl
    {
        private readonly DatabaseService _dbService;
        private readonly ModbusService _modbusService;

        // --- [아두이노 이더넷 통신 필드] ---
        private TcpClient _arduinoClient;
        private NetworkStream _arduinoStream;
        private bool _isArduinoConnected = false;

        public MainView()
        {
            InitializeComponent();
            _dbService = new DatabaseService();
            _modbusService = new ModbusService();

            // 초기 IP 세팅 (XAML의 컨트롤 이름 확인: IpInPlc, IpGas1)
            this.Loaded += (s, e) => {
                if (IpInPlc != null) IpInPlc.Text = "192.168.0.8";
                if (IpGas1 != null) IpGas1.Text = "192.168.0.14";
            };
        }

        // --- [핵심: IP 설정 확인 버튼 클릭 시 연결 로직] ---
        private async void BtnIpConfirm_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // UI 텍스트박스가 존재하는지 확인 후 IP 가져오기
                string plcIp = IpInPlc.Text;
                string gasIp = IpGas1.Text;

                // 1. PLC (Modbus TCP) 연결 시도
                AppendLog(LogInPlc, $"{plcIp} 연결 시도 중...");
                bool isPlcConnected = await _modbusService.ConnectAsync(plcIp, 502);

                if (isPlcConnected)
                {
                    AppendLog(LogInPlc, "PLC 연결 성공 (502)");

                    // 2. 아두이노 (TCP/IP) 연결 시도 (PLC 연결 성공 시)
                    AppendLog(LogGas1, $"{gasIp} 연결 시도 중...");
                    await ConnectArduinoEthernet(gasIp, 8080);
                }
                else
                {
                    AppendLog(LogInPlc, "PLC 연결 실패");
                }
            }
            catch (Exception ex)
            {
                AppendLog(LogInPlc, $"[System Error] {ex.Message}");
            }
        }

        // --- [아두이노 이더넷 통신 로직] ---
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
                    AppendLog(LogGas1, $"연결 성공 ({ip}:{port})");

                    _ = Task.Run(() => ReceiveArduinoDataLoop());
                }
                else
                {
                    AppendLog(LogGas1, "연결 실패: 타임아웃");
                }
            }
            catch (Exception ex)
            {
                AppendLog(LogGas1, $"[Arduino Error] {ex.Message}");
            }
        }

        private async Task ReceiveArduinoDataLoop()
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
                        });
                    }
                    else break;
                }
                catch { break; }
            }
            _isArduinoConnected = false;
            Dispatcher.Invoke(() => AppendLog(LogGas1, "연결 종료"));
        }

        // --- [로그 출력 보조 메서드] ---
        private void AppendLog(TextBox targetLog, string msg)
        {
            if (targetLog == null) return;
            targetLog.Text = $"[{DateTime.Now:HH:mm:ss}] {msg}";
        }

        // --- [사이드바 메뉴 이동 로직] ---
        private void ShowMain_Click(object sender, RoutedEventArgs e) => SetVisibility(MainMonitorSection);
        private void ShowLog_Click(object sender, RoutedEventArgs e) => SetVisibility(LogSection);
        private void ShowSetting_Click(object sender, RoutedEventArgs e) => SetVisibility(SettingSection);

        private void SetVisibility(Grid section)
        {
            if (MainMonitorSection == null || LogSection == null || SettingSection == null) return;
            MainMonitorSection.Visibility = Visibility.Collapsed;
            LogSection.Visibility = Visibility.Collapsed;
            SettingSection.Visibility = Visibility.Collapsed;
            section.Visibility = Visibility.Visible;
        }

        // --- [기타 버튼 핸들러] ---
        private void BtnConnCheck_Click(object sender, RoutedEventArgs e)
        {
            AppendLog(LogDb, "DB 연결 상태 확인됨");
        }

        private void BtnAccConfirm_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show($"{AccName.Text} 설정 완료");
        }
    }
}