using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using factory_automation_system_FAS_.ViewModels;
using WpfAppModbus;

namespace factory_automation_system_FAS_
{
    public partial class MainWindow : Window
    {
        private ModbusService? _modbusService;
        private MainViewModel _viewModel;
        private TcpClient? _arduinoClient;
        private NetworkStream? _arduinoStream;
        private CancellationTokenSource? _monitoringCts;

        public MainWindow()
        {
            InitializeComponent();

            _viewModel = new MainViewModel();
            this.DataContext = _viewModel;

            // XAML에서 정의한 x:Name="MainControl"에 ViewModel 연결
            if (MainControl != null)
            {
                MainControl.DataContext = _viewModel;
            }

            _modbusService = new ModbusService();
        }

        private async void BtnPlcConnect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (await _modbusService!.ConnectAsync(TxtIp.Text, int.Parse(TxtPort.Text)))
                {
                    LedConn.Fill = Brushes.LimeGreen;
                    BtnPlcConnect.IsEnabled = false;
                    BtnPlcDisconnect.IsEnabled = true;
                    StartMonitoring();
                    // 아두이노 연결 시도
                    await ConnectArduinoEthernet("192.168.0.220", 8080);
                }
            }
            catch (Exception ex) { AppendLog($"Error: {ex.Message}"); }
        }

        private void StartMonitoring()
        {
            _monitoringCts = new CancellationTokenSource();
            Task.Run(() => MonitorLoop(_monitoringCts.Token));
        }

        private async Task MonitorLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                if (_modbusService?.IsConnected == true)
                {
                    try
                    {
                        ushort[] outputs = await _modbusService.ReadRegistersAsync(2000, 130);
                        if (outputs != null)
                        {
                            Dispatcher.Invoke(() => {
                                _viewModel.IsALineRunning = outputs[0] == 1;
                                _viewModel.IsBLineRunning = outputs[100] == 1;
                            });
                        }
                    }
                    catch { }
                }
                await Task.Delay(500);
            }
        }

        private async Task ConnectArduinoEthernet(string ip, int port)
        {
            try
            {
                _arduinoClient = new TcpClient();
                await _arduinoClient.ConnectAsync(ip, port);
                _arduinoStream = _arduinoClient.GetStream();
                _ = Task.Run(() => ReceiveArduinoDataLoop());
                AppendLog("Arduino Connected.");
            }
            catch { AppendLog("Arduino Connection Failed."); }
        }

        private async Task ReceiveArduinoDataLoop()
        {
            byte[] buffer = new byte[1024];
            while (_arduinoStream != null)
            {
                try
                {
                    int bytesRead = await _arduinoStream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead > 0)
                    {
                        string data = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();
                        string[] split = data.Split(',');
                        if (split.Length >= 3)
                        {
                            Dispatcher.Invoke(() => {
                                _viewModel.Temp = split[0];
                                _viewModel.Humi = split[1];
                                _viewModel.Co2 = split[2];
                            });
                        }
                    }
                }
                catch { break; }
            }
        }

        private void BtnPlcDisconnect_Click(object sender, RoutedEventArgs e)
        {
            _monitoringCts?.Cancel();
            _modbusService?.Disconnect();
            LedConn.Fill = Brushes.Gray;
            BtnPlcConnect.IsEnabled = true;
            BtnPlcDisconnect.IsEnabled = false;
        }

        private void AppendLog(string msg) => Dispatcher.Invoke(() => TxtLog.AppendText($"[{DateTime.Now:T}] {msg}\n"));

        // 미구현 버튼 이벤트 핸들러
        private void BtnImportJson_Click(object sender, RoutedEventArgs e) { MessageBox.Show("JSON 임포트 기능을 구현 중입니다."); }
        private void BtnModbusRead_Click(object sender, RoutedEventArgs e) { AppendLog("Modbus Read 시도..."); }
        private void BtnModbusWrite_Click(object sender, RoutedEventArgs e) { AppendLog("Modbus Write 시도..."); }
    }
}