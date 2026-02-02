using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using factory_automation_system_FAS_.ViewModels;
using factory_automation_system_FAS_.Models; // 제공된 DB 모델 사용
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
        private int _dbSaveCounter = 0; // 5초 주기 저장을 위한 카운터

        public MainWindow()
        {
            InitializeComponent();
            _viewModel = new MainViewModel();
            this.DataContext = _viewModel;

            if (MainControl != null) MainControl.DataContext = _viewModel;
            _modbusService = new ModbusService();
        }

        private async void BtnPlcConnect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // UI에서 입력받은 IP와 Port로 PLC 연결
                if (await _modbusService!.ConnectAsync(TxtIp.Text, int.Parse(TxtPort.Text)))
                {
                    LedConn.Fill = Brushes.LimeGreen;
                    BtnPlcConnect.IsEnabled = false;
                    BtnPlcDisconnect.IsEnabled = true;

                    StartMonitoring(); // 모니터링 시작
                    await ConnectArduinoEthernet("192.168.0.220", 8080); // 아두이노 연결
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
                        // 엑셀 범위: %MX3000(2000) ~ %MX3120(2120)까지 읽기 위해 130개 요청
                        ushort[] outputs = await _modbusService.ReadRegistersAsync(2000, 130);

                        if (outputs != null)
                        {
                            Dispatcher.Invoke(() => {
                                // 1. 엑셀 '메모리주소.xlsx' 기반 가동 상태 매칭
                                _viewModel.IsALineRunning = (outputs[0] == 1);   // %MX3000 (A_Blue Line Run)
                                _viewModel.IsBLineRunning = (outputs[100] == 1); // %MX3100 (B_Start_System)
                                _viewModel.IsCLineRunning = (outputs[120] == 1); // %MX3120 (WC_Run_System)

                                // 2. DB 저장 로직 실행 (500ms * 10 = 5초 주기)
                                _dbSaveCounter++;
                                if (_dbSaveCounter >= 10)
                                {
                                    SaveDataToModels();
                                    _dbSaveCounter = 0;
                                }
                            });
                        }
                    }
                    catch { /* 통신 노이즈 발생 시 무시 */ }
                }
                await Task.Delay(500); // 0.5초 주기
            }
        }

        // 수신된 데이터를 DB 모델 형식으로 변환하여 저장하는 로직
        private void SaveDataToModels()
        {
            try
            {
                // [TraceLog] 환경 및 가동 로그 생성
                var traceLog = new TraceLog
                {
                    entity_type = "PLC_ENVIRONMENT",
                    entity_id = 1,
                    action = "AUTO_SAVE",
                    ts = DateTime.Now,
                    detail = $"{{\"temp\": \"{_viewModel.Temp}\", \"humi\": \"{_viewModel.Humi}\", \"co2\": \"{_viewModel.Co2}\", \"A_Run\": {_viewModel.IsALineRunning.ToString().ToLower()}}}"
                };

                // [Conveyor] 라인 가동 정보 업데이트용
                var conveyor = new Conveyor
                {
                    conv_id = 1,
                    line_name = "Main_Line_A",
                    status = _viewModel.IsALineRunning ? "RUNNING" : "IDLE"
                };

                // [DB 저장 실행부] 
                // 실제 사용하시는 DB 저장 클래스(예: EntityFramework DBContext)를 여기에 호출하세요.
                // 예: _db.TraceLogs.Add(traceLog); _db.SaveChanges();
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

        // 미사용 클릭 이벤트 (생략 없이 유지)
        private void BtnImportJson_Click(object sender, RoutedEventArgs e) { MessageBox.Show("JSON 기능 준비중"); }
        private void BtnModbusRead_Click(object sender, RoutedEventArgs e) { }
        private void BtnModbusWrite_Click(object sender, RoutedEventArgs e) { }
    }
}