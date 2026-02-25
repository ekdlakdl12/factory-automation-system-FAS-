using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using factory_automation_system_FAS_.ViewModels;
using factory_automation_system_FAS_.Models;
using factory_automation_system_FAS_.Views;
using factory_automation_system_FAS_.Services;
using WpfAppModbus;
using System.Linq;

namespace factory_automation_system_FAS_
{
    public partial class MainWindow : Window
    {
        private readonly DatabaseService _dbService = new DatabaseService();
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

        #region [창 제어] 비전 창 열기
        private void BtnOpenVision_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var existingWindow = Application.Current.Windows.OfType<VisionWindow>().FirstOrDefault();
                if (existingWindow != null)
                {
                    existingWindow.Activate();
                    return;
                }
                VisionWindow visionWin = new VisionWindow { Owner = this };
                visionWin.Show();
                AppendLog("비전 모니터링 시스템 활성화");
            }
            catch (Exception ex) { AppendLog($"비전 창 열기 실패: {ex.Message}"); }
        }
        #endregion

        #region [통신 시작/종료] PLC 및 아두이노 연결
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
                    AppendLog("시스템 통합 모니터링 프로세스 가동");
                }
            }
            catch (Exception ex) { AppendLog($"연결 실패: {ex.Message}"); }
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
        #endregion

        #region [모니터링] PLC 데이터 수집 루프
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
                                int activeConvId = _viewModel.IsBLineRunning ? 2 : (_viewModel.IsCLineRunning ? 3 : 1);
                                _dbSaveCounter++;
                                if (_dbSaveCounter >= 10)
                                {
                                    SaveDataToModels(activeConvId);
                                    _dbSaveCounter = 0;
                                }
                            });
                        }
                    }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"PLC 수집 에러: {ex.Message}"); }
                }
                await Task.Delay(500);
            }
        }
        #endregion

        #region [DB 저장] 모델 가공 및 저장
        private async void SaveDataToModels(int currentConvId)
        {
            try
            {
                var traceLog = new TraceLog
                {
                    entity_type = "ENVIRONMENT",
                    entity_id = currentConvId,
                    action = "PERIODIC_CHECK",
                    ts = DateTime.Now,
                    detail = $"{{\"temp\": \"{_viewModel.Temp}\", \"humi\": \"{_viewModel.Humi}\", \"co2\": \"{_viewModel.Co2}\"}}"
                };
                var conveyor = new Conveyor
                {
                    conv_id = currentConvId,
                    line_name = $"Production_Line_{currentConvId}",
                    status = (currentConvId == 1 && _viewModel.IsALineRunning) ||
                             (currentConvId == 2 && _viewModel.IsBLineRunning) ||
                             (currentConvId == 3 && _viewModel.IsCLineRunning) ? "RUNNING" : "IDLE"
                };
            }
            catch (Exception ex) { AppendLog($"DB 저장 실패: {ex.Message}"); }
        }
        #endregion

        #region [아두이노] 센서 데이터 수신
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
            catch { AppendLog("아두이노 연결 실패 (네트워크 확인)"); }
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
        #endregion

        #region [시스템 종료] 자원 해제
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
        #endregion

        private void AppendLog(string msg) => Dispatcher.Invoke(() => TxtLog.AppendText($"[{DateTime.Now:T}] {msg}\n"));
        private void BtnImportJson_Click(object sender, RoutedEventArgs e) { }
        private void BtnModbusRead_Click(object sender, RoutedEventArgs e) { }
        private void BtnModbusWrite_Click(object sender, RoutedEventArgs e) { }
    }
}