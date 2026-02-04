using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using factory_automation_system_FAS_.ViewModels;
using factory_automation_system_FAS_.Models;
using factory_automation_system_FAS_.Views; // VisionWindow 참조를 위해 추가
using WpfAppModbus;
using System.Linq; // Window 제어를 위한 Linq 추가

namespace factory_automation_system_FAS_
{
    public partial class MainWindow : Window
    {
        private ModbusService? _modbusService;
        private MainViewModel _viewModel;
        private TcpClient? _arduinoClient;
        private NetworkStream? _arduinoStream;
        private CancellationTokenSource? _monitoringCts;
        private int _dbSaveCounter = 0;

        public MainWindow()
        {
            InitializeComponent();
            _viewModel = new MainViewModel();
            this.DataContext = _viewModel;

            if (MainControl != null) MainControl.DataContext = _viewModel;
            _modbusService = new ModbusService();
        }

        // [추가] 비전 창 열기 버튼 이벤트
        private void BtnOpenVision_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 이미 열려있는 창이 있는지 확인
                var existingWindow = Application.Current.Windows.OfType<VisionWindow>().FirstOrDefault();
                if (existingWindow != null)
                {
                    existingWindow.Activate(); // 이미 있으면 앞으로 가져옴
                    return;
                }

                // 새 비전 창 생성 및 출력
                VisionWindow visionWin = new VisionWindow();
                visionWin.Owner = this; // 메인 창이 닫히면 함께 종료됨
                visionWin.Show();
                AppendLog("비전 모니터링 창 활성화");
            }
            catch (Exception ex)
            {
                AppendLog($"비전 창 열기 실패: {ex.Message}");
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            try
            {
                _monitoringCts?.Cancel();
                _modbusService?.Disconnect();
                _arduinoStream?.Close();
                _arduinoClient?.Close();
            }
            catch { }
            finally
            {
                Application.Current.Shutdown();
                System.Diagnostics.Process.GetCurrentProcess().Kill();
            }
        }

        private async void BtnPlcConnect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_modbusService != null && await _modbusService.ConnectAsync(TxtIp.Text, int.Parse(TxtPort.Text)))
                {
                    LedConn.Fill = Brushes.LimeGreen;
                    BtnPlcConnect.IsEnabled = false;
                    BtnPlcDisconnect.IsEnabled = true;

                    StartMonitoring();
                    await ConnectArduinoEthernet("192.168.0.220", 8080);
                    AppendLog("시스템 통합 모니터링 및 DB 저장 프로세스 시작");
                }
            }
            catch (Exception ex) { AppendLog($"연결 실패: {ex.Message}"); }
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
                                _viewModel.IsALineRunning = (outputs[0] == 1);
                                _viewModel.IsBLineRunning = (outputs[100] == 1);
                                _viewModel.IsCLineRunning = (outputs[120] == 1);

                                _dbSaveCounter++;
                                if (_dbSaveCounter >= 10)
                                {
                                    SaveDataToModels();
                                    _dbSaveCounter = 0;
                                }
                            });
                        }
                    }
                    catch { }
                }
                await Task.Delay(500);
            }
        }

        private void SaveDataToModels()
        {
            try
            {
                var traceLog = new TraceLog
                {
                    entity_type = "PLC_ENVIRONMENT",
                    entity_id = 1,
                    action = "AUTO_SAVE",
                    ts = DateTime.Now,
                    detail = $"{{\"temp\": \"{_viewModel.Temp}\", \"humi\": \"{_viewModel.Humi}\", \"co2\": \"{_viewModel.Co2}\"}}"
                };

                var conveyor = new Conveyor
                {
                    conv_id = 1,
                    line_name = "Main_Line_A",
                    status = _viewModel.IsALineRunning ? "RUNNING" : "IDLE"
                };
            }
            catch (Exception ex) { AppendLog($"DB 모델 저장 실패: {ex.Message}"); }
        }

        private async Task ConnectArduinoEthernet(string ip, int port)
        {
            try
            {
                _arduinoClient = new TcpClient();
                await _arduinoClient.ConnectAsync(ip, port);
                _arduinoStream = _arduinoClient.GetStream();
                _ = Task.Run(() => ReceiveArduinoDataLoop());
                AppendLog("아두이노 센서 모듈 연결 성공");
            }
            catch { AppendLog("아두이노 연결 오류"); }
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
            AppendLog("모니터링 시스템 종료");
        }

        private void AppendLog(string msg) => Dispatcher.Invoke(() => TxtLog.AppendText($"[{DateTime.Now:T}] {msg}\n"));

        private void BtnImportJson_Click(object sender, RoutedEventArgs e) { MessageBox.Show("JSON 기능 준비중"); }
        private void BtnModbusRead_Click(object sender, RoutedEventArgs e) { }
        private void BtnModbusWrite_Click(object sender, RoutedEventArgs e) { }
    }
}