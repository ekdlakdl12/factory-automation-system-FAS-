using System;
using System.IO.Ports; // NuGet에서 System.IO.Ports 설치 필수
using System.Windows;
using System.Windows.Media;
using factory_automation_system_FAS_.Services;
using WpfAppModbus; // ModbusService 네임스페이스 확인 필요

namespace factory_automation_system_FAS_
{
    public partial class MainWindow : Window
    {
        private readonly DatabaseService _dbService;
        private readonly ModbusService _modbusService;
        private SerialPort _serialPort; // 시리얼 통신 객체 추가

        public MainWindow()
        {
            InitializeComponent();
            _dbService = new DatabaseService();
            _modbusService = new ModbusService();

            // 시리얼 포트 초기설정
            InitSerial();
        }

        // --- [시리얼 통신 (Arduino) 로직] ---
        private void InitSerial()
        {
            _serialPort = new SerialPort();
            _serialPort.PortName = "COM3"; // TODO: 실제 아두이노 연결 포트로 수정 필요
            _serialPort.BaudRate = 9600;
            _serialPort.DataReceived += SerialPort_DataReceived;
        }

        private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                string data = _serialPort.ReadLine(); // 아두이노로부터 한 줄 읽기
                // UI 스레드 접근을 위해 Dispatcher 사용
                Dispatcher.Invoke(() => {
                    AppendLog($"[Arduino] 수신: {data.Trim()}");
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => AppendLog($"[Serial Error] {ex.Message}"));
            }
        }

        // --- [기존 모드버스 및 DB 로직 유지/통합] ---
        private async void BtnPlcConnect_Click(object sender, RoutedEventArgs e)
        {
            AppendLog($"{TxtIp.Text} 연결 시도 중...");
            bool isConnected = await _modbusService.ConnectAsync(TxtIp.Text, int.Parse(TxtPort.Text));

            if (isConnected)
            {
                AppendLog("PLC 연결 성공 (Modbus TCP)");
                LedConn.Fill = Brushes.LimeGreen;
                BtnPlcConnect.IsEnabled = false;
                BtnPlcDisconnect.IsEnabled = true;

                // PLC 연결 성공 시 아두이노 시리얼 포트도 자동으로 열기 시도
                try
                {
                    if (!_serialPort.IsOpen)
                    {
                        _serialPort.Open();
                        AppendLog("[Arduino] Serial Port Open 성공");
                    }
                }
                catch
                {
                    AppendLog("[Arduino] Serial Port 연결 실패 (포트 확인 요망)");
                }
            }
            else
            {
                AppendLog("PLC 연결 실패!");
                MessageBox.Show("PLC 네트워크 상태를 확인하세요.");
            }
        }

        private void BtnPlcDisconnect_Click(object sender, RoutedEventArgs e)
        {
            _modbusService.Disconnect();
            if (_serialPort.IsOpen) _serialPort.Close(); // 시리얼도 함께 닫기

            AppendLog("모든 연결 해제됨");
            LedConn.Fill = Brushes.Gray;
            BtnPlcConnect.IsEnabled = true;
            BtnPlcDisconnect.IsEnabled = false;
        }

        private async void BtnModbusRead_Click(object sender, RoutedEventArgs e)
        {
            if (!_modbusService.IsConnected) return;

            ushort addr = ushort.Parse(TxtAddr.Text);
            var data = await _modbusService.ReadRegistersAsync(addr, 1);

            if (data != null) AppendLog($"[PLC READ] Addr {addr}: {data[0]}");
            else AppendLog("[PLC READ] 읽기 실패");
        }

        private async void BtnModbusWrite_Click(object sender, RoutedEventArgs e)
        {
            if (!_modbusService.IsConnected) return;

            ushort addr = ushort.Parse(TxtAddr.Text);
            ushort val = ushort.Parse(TxtWriteVal.Text);
            bool success = await _modbusService.WriteRegisterAsync(addr, val);

            if (success) AppendLog($"[PLC WRITE] Addr {addr} -> {val} 성공");
            else AppendLog("[PLC WRITE] 쓰기 실패");
        }

        private async void BtnImportJson_Click(object sender, RoutedEventArgs e)
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

        private void AppendLog(string msg)
        {
            TxtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}{Environment.NewLine}");
            TxtLog.ScrollToEnd();
        }

        // 프로그램 종료 시 자원 해제
        protected override void OnClosed(EventArgs e)
        {
            _modbusService.Disconnect();
            if (_serialPort != null && _serialPort.IsOpen) _serialPort.Close();
            base.OnClosed(e);
        }
    }
}