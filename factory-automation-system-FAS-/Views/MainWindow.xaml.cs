using System;
using System.Net.Sockets; // 이더넷 통신을 위해 필수 추가
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using factory_automation_system_FAS_.Services;
using WpfAppModbus;

namespace factory_automation_system_FAS_
{
    public partial class MainWindow : Window
    {
        private readonly DatabaseService _dbService;
        private readonly ModbusService _modbusService;

        // --- [이더넷 통신 (Arduino) 필드] ---
        private TcpClient _arduinoClient;
        private NetworkStream _arduinoStream;
        private bool _isArduinoConnected = false;

        public MainWindow()
        {
            InitializeComponent();
            _dbService = new DatabaseService();
            _modbusService = new ModbusService();

            // 아두이노 초기화 로직은 Connect 시점에 수행되도록 통합
        }

        // --- [아두이노 이더넷 통신 로직] ---
        private async Task ConnectArduinoEthernet(string ip, int port)
        {
            try
            {
                // 기존 연결이 있다면 정리
                _isArduinoConnected = false;
                _arduinoStream?.Close();
                _arduinoClient?.Close();

                _arduinoClient = new TcpClient();

                // 비동기 연결 시도 (3초 타임아웃 설정)
                var connectTask = _arduinoClient.ConnectAsync(ip, port);
                if (await Task.WhenAny(connectTask, Task.Delay(3000)) == connectTask)
                {
                    await connectTask;
                    _arduinoStream = _arduinoClient.GetStream();
                    _isArduinoConnected = true;
                    AppendLog($"[Arduino] 이더넷 연결 성공 ({ip}:{port})");

                    // 데이터 수신 루프를 백그라운드에서 실행
                    _ = Task.Run(() => ReceiveArduinoDataLoop());
                }
                else
                {
                    AppendLog("[Arduino] 연결 실패: 타임아웃 (IP/포트 확인 요망)");
                }
            }
            catch (Exception ex)
            {
                AppendLog($"[Arduino TCP Error] {ex.Message}");
            }
        }

        private async Task ReceiveArduinoDataLoop()
        {
            byte[] buffer = new byte[1024];
            while (_isArduinoConnected && _arduinoStream != null)
            {
                try
                {
                    // 비동기로 데이터 읽기
                    int bytesRead = await _arduinoStream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead > 0)
                    {
                        string data = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                        // UI 스레드에서 로그 출력
                        Dispatcher.Invoke(() => {
                            AppendLog($"[Arduino 수신] {data.Trim()}");
                        });
                    }
                    else
                    {
                        // 연결이 종료됨
                        break;
                    }
                }
                catch
                {
                    break;
                }
            }
            _isArduinoConnected = false;
            Dispatcher.Invoke(() => AppendLog("[Arduino] 연결이 해제되었습니다."));
        }

        // --- [통합 연결/해제 로직] ---
        private async void BtnPlcConnect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                AppendLog($"{TxtIp.Text} 연결 시도 중...");
                bool isConnected = await _modbusService.ConnectAsync(TxtIp.Text, int.Parse(TxtPort.Text));

                if (isConnected)
                {
                    AppendLog("PLC 연결 성공 (Modbus TCP)");
                    LedConn.Fill = Brushes.LimeGreen;
                    BtnPlcConnect.IsEnabled = false;
                    BtnPlcDisconnect.IsEnabled = true;

                    // 요청하신 아두이노 IP: 192.168.0.14, Port: 8080으로 연결
                    await ConnectArduinoEthernet("192.168.0.14", 8080);
                }
                else
                {
                    AppendLog("PLC 연결 실패!");
                    MessageBox.Show("PLC 네트워크 상태를 확인하세요.");
                }
            }
            catch (Exception ex)
            {
                AppendLog($"[Connect Error] {ex.Message}");
            }
        }

        private void BtnPlcDisconnect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _modbusService.Disconnect();

                // 아두이노 이더넷 해제
                _isArduinoConnected = false;
                _arduinoStream?.Close();
                _arduinoClient?.Close();

                AppendLog("모든 연결 해제됨");
                LedConn.Fill = Brushes.Gray;
                BtnPlcConnect.IsEnabled = true;
                BtnPlcDisconnect.IsEnabled = false;
            }
            catch (Exception ex)
            {
                AppendLog($"[Disconnect Error] {ex.Message}");
            }
        }

        // --- [모드버스 제어 로직 - 안정화 버전] ---
        private async void BtnModbusRead_Click(object sender, RoutedEventArgs e)
        {
            if (!_modbusService.IsConnected) return;

            try
            {
                // 입력값 파싱 시 에러 방지
                if (!ushort.TryParse(TxtAddr.Text, out ushort addr))
                {
                    AppendLog("[Error] 올바른 주소를 입력하세요.");
                    return;
                }

                // 2개 레지스터 읽기 유지
                var data = await _modbusService.ReadRegistersAsync(addr, 2);

                if (data != null) AppendLog($"[PLC READ] Addr {addr}: {data[0]}");
                else AppendLog("[PLC READ] 읽기 실패");
            }
            catch (Exception ex)
            {
                AppendLog($"[Read Error] {ex.Message}");
            }
        }

        private async void BtnModbusWrite_Click(object sender, RoutedEventArgs e)
        {
            if (!_modbusService.IsConnected) return;

            try
            {
                if (!ushort.TryParse(TxtAddr.Text, out ushort addr) ||
                    !ushort.TryParse(TxtWriteVal.Text, out ushort val))
                {
                    AppendLog("[Error] 주소 또는 값을 확인하세요.");
                    return;
                }

                bool success = await _modbusService.WriteRegisterAsync(addr, val);

                if (success) AppendLog($"[PLC WRITE] Addr {addr} -> {val} 성공");
                else AppendLog("[PLC WRITE] 쓰기 실패");
            }
            catch (Exception ex)
            {
                AppendLog($"[Write Error] {ex.Message}");
            }
        }

        // --- [데이터베이스 및 기타 로직] ---
        private async void BtnImportJson_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                bool result = await _dbService.InsertVisionEventFromJsonAsync(1);
                if (result)
                {
                    AppendLog("Vision 데이터 DB 저장 성공!");
                    dgVisionLogs.ItemsSource = await _dbService.GetRecentVisionEventsAsync();
                }
                else
                {
                    MessageBox.Show("저장 실패! 파일 경로를 확인하세요.");
                }
            }
            catch (Exception ex)
            {
                AppendLog($"[DB Error] {ex.Message}");
            }
        }

        private void AppendLog(string msg)
        {
            TxtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}{Environment.NewLine}");
            TxtLog.ScrollToEnd();
        }

        // 프로그램 종료 시 자원 해제
        protected override void OnClosed(EventArgs e)
        {
            _modbusService.Disconnect();
            _isArduinoConnected = false;
            _arduinoStream?.Close();
            _arduinoClient?.Close();
            base.OnClosed(e);
        }
    }
}