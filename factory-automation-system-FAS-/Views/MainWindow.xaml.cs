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
using factory_automation_system_FAS_.Services; // DatabaseService 사용을 위해 추가
using WpfAppModbus;
using System.Linq;

namespace factory_automation_system_FAS_
{
    public partial class MainWindow : Window
    {
        private readonly DatabaseService _dbService = new DatabaseService(); // DB 저장을 위한 서비스 객체
        private ModbusService? _modbusService;
        private MainViewModel _viewModel;
        private TcpClient? _arduinoClient;
        private NetworkStream? _arduinoStream;
        private CancellationTokenSource? _monitoringCts;
        private int _dbSaveCounter = 0; // 주기적 저장을 위한 카운터 (0.5초 * 10 = 5초)

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
                // 중복 창 열림 방지: 이미 열려있는 VisionWindow가 있는지 확인
                var existingWindow = Application.Current.Windows.OfType<VisionWindow>().FirstOrDefault();
                if (existingWindow != null)
                {
                    existingWindow.Activate();
                    return;
                }

                VisionWindow visionWin = new VisionWindow();
                visionWin.Owner = this; // 메인창 종료 시 비전창도 자동 종료되도록 설정
                visionWin.Show();
                AppendLog("비전 모니터링 시스템 활성화");
            }
            catch (Exception ex)
            {
                AppendLog($"비전 창 열기 실패: {ex.Message}");
            }
        }
        #endregion

        #region [통신 시작/종료] PLC 및 아두이노 연결
        private async void BtnPlcConnect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // PLC Modbus TCP 연결 (사용자 입력 IP/PORT 사용)
                if (_modbusService != null && await _modbusService.ConnectAsync(TxtIp.Text, int.Parse(TxtPort.Text)))
                {
                    LedConn.Fill = Brushes.LimeGreen;
                    BtnPlcConnect.IsEnabled = false;
                    BtnPlcDisconnect.IsEnabled = true;

                    // 1. PLC 모니터링 루프 시작
                    StartMonitoring();
                    // 2. 아두이노 이더넷 연결 시작
                    await ConnectArduinoEthernet("192.168.0.220", 8080);

                    AppendLog("시스템 통합 모니터링 프로세스 가동");
                }
            }
            catch (Exception ex) { AppendLog($"연결 실패: {ex.Message}"); }
        }

        private void BtnPlcDisconnect_Click(object sender, RoutedEventArgs e)
        {
            _monitoringCts?.Cancel(); // 모니터링 작업 취소
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
            // 백그라운드 스레드에서 무한 루프 실행 (UI 멈춤 방지)
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
                        // [중요] PLC 주소 2000번부터 130개의 레지스터를 읽어옴
                        ushort[] outputs = await _modbusService.ReadRegistersAsync(2000, 130);

                        if (outputs != null)
                        {
                            Dispatcher.Invoke(() => {
                                // 1. PLC 신호를 ViewModel 상태에 반영 (1이면 가동중)
                                _viewModel.IsALineRunning = (outputs[0] == 1);
                                _viewModel.IsBLineRunning = (outputs[100] == 1);
                                _viewModel.IsCLineRunning = (outputs[120] == 1);

                                // 2. 현재 활성화된 컨베이어 ID 판단 (A:1, B:2, C:3)
                                int activeConvId = 1; // 기본값
                                if (_viewModel.IsBLineRunning) activeConvId = 2;
                                else if (_viewModel.IsCLineRunning) activeConvId = 3;

                                // 3. 주기적 DB 저장 로직 (카운터가 10이 되면 저장, 약 5초 주기)
                                _dbSaveCounter++;
                                if (_dbSaveCounter >= 10)
                                {
                                    SaveDataToModels(activeConvId);
                                    _dbSaveCounter = 0;
                                }
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"PLC 수집 에러: {ex.Message}");
                    }
                }
                await Task.Delay(500); // 0.5초 대기
            }
        }
        #endregion

        #region [DB 저장] 모델 가공 및 저장
        /// <summary>
        /// 현재 가동 중인 컨베이어 ID를 받아 로그 및 상태 정보를 DB에 저장합니다.
        /// </summary>
        private async void SaveDataToModels(int currentConvId)
        {
            try
            {
                // 1. 환경 데이터 로그 생성 (TraceLog 모델)
                var traceLog = new TraceLog
                {
                    entity_type = "ENVIRONMENT",
                    entity_id = currentConvId,
                    action = "PERIODIC_CHECK",
                    ts = DateTime.Now,
                    detail = $"{{\"temp\": \"{_viewModel.Temp}\", \"humi\": \"{_viewModel.Humi}\", \"co2\": \"{_viewModel.Co2}\"}}"
                };

                // 2. 컨베이어 상태 정보 생성 (Conveyor 모델)
                var conveyor = new Conveyor
                {
                    conv_id = currentConvId,
                    line_name = $"Production_Line_{currentConvId}",
                    status = (currentConvId == 1 && _viewModel.IsALineRunning) ||
                             (currentConvId == 2 && _viewModel.IsBLineRunning) ||
                             (currentConvId == 3 && _viewModel.IsCLineRunning) ? "RUNNING" : "IDLE"
                };

                // [참고] 실제로 DB에 반영하려면 아래와 같이 서비스를 호출해야 합니다.
                // await _dbService.InsertTraceLogAsync(traceLog);
                // await _dbService.UpdateConveyorStatusAsync(conveyor);
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
                        // 데이터 형식 가정: "25.4,60.2,450" (온도,습도,CO2)
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
                // 모든 백그라운드 프로세스 강제 종료
                Application.Current.Shutdown();
                System.Diagnostics.Process.GetCurrentProcess().Kill();
            }
        }
        #endregion

        private void AppendLog(string msg) => Dispatcher.Invoke(() => TxtLog.AppendText($"[{DateTime.Now:T}] {msg}\n"));

        // 미사용 이벤트 핸들러 유지
        private void BtnImportJson_Click(object sender, RoutedEventArgs e) { }
        private void BtnModbusRead_Click(object sender, RoutedEventArgs e) { }
        private void BtnModbusWrite_Click(object sender, RoutedEventArgs e) { }
    }
}