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
    // 데이터 바인딩을 위한 ViewModel (기능 추가용)
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
        private MainViewModel _viewModel; // 바인딩 객체

        private TcpClient? _arduinoClient;
        private NetworkStream? _arduinoStream;
        private bool _isArduinoConnected = false;

        public MainWindow()
        {
            InitializeComponent();
            _viewModel = new MainViewModel();
            this.DataContext = _viewModel; // 바인딩 연결

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
                _arduinoStream?.Close();
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
                            _viewModel.ArduinoData = data; // 바인딩된 값 갱신 (UI 자동 변경)
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
                    await ConnectArduinoEthernet("192.168.0.231", 8080);
                }
                else { MessageBox.Show("PLC 네트워크 상태를 확인하세요."); }
            }
            catch (Exception ex) { AppendLog($"[Connect Error] {ex.Message}"); }
        }

        private void BtnPlcDisconnect_Click(object sender, RoutedEventArgs e)
        {
            _modbusService?.Disconnect();
            _isArduinoConnected = false;
            _arduinoStream?.Close();
            _arduinoClient?.Close();
            AppendLog("모든 연결 해제됨");
            LedConn.Fill = Brushes.Gray;
            BtnPlcConnect.IsEnabled = true;
            BtnPlcDisconnect.IsEnabled = false;
        }

        private async void BtnModbusRead_Click(object sender, RoutedEventArgs e)
        {
            if (_modbusService == null || !_modbusService.IsConnected) return;
            if (!ushort.TryParse(TxtAddr.Text, out ushort addr)) return;
            var data = await _modbusService.ReadRegistersAsync(addr, 2);
            if (data != null) AppendLog($"[PLC READ] Addr {addr}: {data[0]}");
        }

        private async void BtnModbusWrite_Click(object sender, RoutedEventArgs e)
        {
            if (_modbusService == null || !_modbusService.IsConnected) return;
            if (!ushort.TryParse(TxtAddr.Text, out ushort addr) || !ushort.TryParse(TxtWriteVal.Text, out ushort val)) return;
            bool success = await _modbusService.WriteRegisterAsync(addr, val);
            if (success) AppendLog($"[PLC WRITE] Addr {addr} -> {val} 성공");
        }

        private async void BtnImportJson_Click(object sender, RoutedEventArgs e)
        {
            if (_dbService == null) return;
            bool result = await _dbService.InsertVisionEventFromJsonAsync(1);
            if (result) dgVisionLogs.ItemsSource = await _dbService.GetRecentVisionEventsAsync();
        }

        private void AppendLog(string msg)
        {
            if (TxtLog == null) return;
            Dispatcher.Invoke(() => {
                TxtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}{Environment.NewLine}");
                TxtLog.ScrollToEnd();
            });
        }

        protected override void OnClosed(EventArgs e)
        {
            _modbusService?.Disconnect();
            _isArduinoConnected = false;
            base.OnClosed(e);
        }
    }
}