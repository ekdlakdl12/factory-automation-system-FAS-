using System;
using System.ComponentModel;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using factory_automation_system_FAS_.Services;
using WpfAppModbus;

namespace factory_automation_system_FAS_
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private string _arduinoData = "0";
        public string ArduinoData
        {
            get => _arduinoData;
            set { _arduinoData = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null!)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public partial class MainWindow : Window
    {
        private DatabaseService? _dbService;
        private ModbusService? _modbusService;
        private MainViewModel _viewModel;

        private TcpClient? _arduinoClient;
        private NetworkStream? _arduinoStream;
        private bool _isArduinoConnected = false;

        public MainWindow()
        {
            InitializeComponent();
            _viewModel = new MainViewModel();
            this.DataContext = _viewModel;

            try
            {
                _dbService = new DatabaseService();
                _modbusService = new ModbusService();
                AppendLog("시스템 서비스 초기화 성공.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"서비스 로드 실패: {ex.Message}");
            }
        }

        private async Task ConnectArduinoEthernet(string ip, int port)
        {
            try
            {
                _isArduinoConnected = false;
                _arduinoStream?.Dispose(); // Close 대신 Dispose 권장
                _arduinoClient?.Close();

                _arduinoClient = new TcpClient();
                var connectTask = _arduinoClient.ConnectAsync(ip, port);

                if (await Task.WhenAny(connectTask, Task.Delay(10000)) == connectTask)
                {
                    await connectTask;
                    _arduinoStream = _arduinoClient.GetStream();
                    _isArduinoConnected = true;
                    AppendLog($"[Arduino] 연결 성공 ({ip}:{port})");
                    _ = Task.Run(() => ReceiveArduinoDataLoop());
                }
                else { AppendLog("[Arduino] 연결 실패: 타임아웃"); }
            }
            catch (Exception ex) { AppendLog($"[Arduino TCP Error] {ex.Message}"); }
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
                        string data = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();
                        Dispatcher.Invoke(() => {
                            AppendLog($"[Arduino 수신] {data}");
                            _viewModel.ArduinoData = data;

                            string[] splitData = data.Split(',');
                            if (splitData.Length >= 3)
                            {
                                MainControl.Temp = splitData[0];
                                MainControl.Humi = splitData[1];
                                MainControl.Co2 = splitData[2];
                            }
                            else if (splitData.Length == 1)
                            {
                                MainControl.Temp = splitData[0];
                            }
                        });
                    }
                    else break;
                }
                catch { break; }
            }
            _isArduinoConnected = false;
        }

        private async void BtnPlcConnect_Click(object sender, RoutedEventArgs e)
        {
            if (_modbusService == null) return;
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
                    // 아두이노 연결 시도 - 하나더있음 두개 평균내서 하나 내보낼거임192.168.0.221
                    await ConnectArduinoEthernet("192.168.0.220", 8080);
                }
                else { MessageBox.Show("PLC 네트워크 상태를 확인하세요."); }
            }
            catch (Exception ex) { AppendLog($"[Connect Error] {ex.Message}"); }
        }

        private void BtnPlcDisconnect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _modbusService?.Disconnect();
                _isArduinoConnected = false;
                _arduinoStream?.Dispose();
                _arduinoClient?.Close();
                AppendLog("모든 연결 해제됨");
                LedConn.Fill = Brushes.Gray;
                BtnPlcConnect.IsEnabled = true;
                BtnPlcDisconnect.IsEnabled = false;
            }
            catch (Exception ex) { AppendLog($"[Disconnect Error] {ex.Message}"); }
        }

        // 수정한 부분: 데이터 읽기 핸들러 예외 처리 강화
        private async void BtnModbusRead_Click(object sender, RoutedEventArgs e)
        {
            if (_modbusService == null || !_modbusService.IsConnected)
            {
                AppendLog("PLC가 연결되어 있지 않습니다.");
                return;
            }

            if (!ushort.TryParse(TxtAddr.Text, out ushort addr)) return;

            try
            {
                // 읽기 시도 시 버튼 비활성화 (중복 클릭 방지)
                BtnModbusRead.IsEnabled = false;
                var data = await _modbusService.ReadRegistersAsync(addr, 2);
                if (data != null && data.Length > 0)
                {
                    AppendLog($"[PLC READ] Addr {addr}: {data[0]}");
                }
            }
            catch (Exception ex)
            {
                AppendLog($"[PLC READ ERROR] {ex.Message}");
                // 연결 끊김 여부 확인
                if (!_modbusService.IsConnected) LedConn.Fill = Brushes.Red;
            }
            finally
            {
                BtnModbusRead.IsEnabled = true;
            }
        }

        // 수정한 부분: 데이터 쓰기 핸들러 예외 처리 강화
        private async void BtnModbusWrite_Click(object sender, RoutedEventArgs e)
        {
            if (_modbusService == null || !_modbusService.IsConnected) return;
            if (!ushort.TryParse(TxtAddr.Text, out ushort addr) || !ushort.TryParse(TxtWriteVal.Text, out ushort val)) return;

            try
            {
                BtnModbusWrite.IsEnabled = false;
                bool success = await _modbusService.WriteRegisterAsync(addr, val);
                if (success)
                    AppendLog($"[PLC WRITE] Addr {addr} -> {val} 성공");
                else
                    AppendLog($"[PLC WRITE] Addr {addr} 실패 (응답 없음)");
            }
            catch (Exception ex)
            {
                AppendLog($"[PLC WRITE ERROR] {ex.Message}");
            }
            finally
            {
                BtnModbusWrite.IsEnabled = true;
            }
        }

        private async void BtnImportJson_Click(object sender, RoutedEventArgs e)
        {
            if (_dbService == null) return;
            try
            {
                bool result = await _dbService.InsertVisionEventFromJsonAsync(1);
                if (result) dgVisionLogs.ItemsSource = await _dbService.GetRecentVisionEventsAsync();
            }
            catch (Exception ex) { AppendLog($"[DB Error] {ex.Message}"); }
        }

        private void AppendLog(string msg)
        {
            if (TxtLog == null) return;
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => AppendLog(msg)));
                return;
            }
            TxtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}{Environment.NewLine}");
            TxtLog.ScrollToEnd();
        }

        protected override void OnClosed(EventArgs e)
        {
            _isArduinoConnected = false;
            _modbusService?.Disconnect();
            _arduinoStream?.Dispose();
            _arduinoClient?.Close();
            base.OnClosed(e);
        }
    }
}