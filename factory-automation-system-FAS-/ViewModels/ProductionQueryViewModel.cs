using factory_automation_system_FAS_.Models;
using factory_automation_system_FAS_.Utils;
using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;

namespace factory_automation_system_FAS_.ViewModels
{
    /// <summary>
    /// LOG(생산조회) 화면용 VM
    /// - 상단 2줄 조회(설비 상태 / 제품 검사)
    /// - 리스트(DataGrid 1개) 필터 갱신
    /// - MVP: 더미 데이터
    /// </summary>
    public sealed class ProductionQueryViewModel : INotifyPropertyChanged
    {
        // ====== ComboBox 옵션 ======
        public ObservableCollection<string> Devices { get; } = new();
        public ObservableCollection<string> Statuses { get; } = new();
        public ObservableCollection<string> Shapes { get; } = new();
        public ObservableCollection<string> Colors { get; } = new();

        // ====== 조회 입력값 ======
        private DateTime? _deviceDate;
        public DateTime? DeviceDate { get => _deviceDate; set { _deviceDate = value; OnPropertyChanged(); } }

        private string? _selectedDevice;
        public string? SelectedDevice { get => _selectedDevice; set { _selectedDevice = value; OnPropertyChanged(); } }

        private string? _selectedStatus;
        public string? SelectedStatus { get => _selectedStatus; set { _selectedStatus = value; OnPropertyChanged(); } }

        private DateTime? _inspectionDate;
        public DateTime? InspectionDate { get => _inspectionDate; set { _inspectionDate = value; OnPropertyChanged(); } }

        private string? _selectedShape;
        public string? SelectedShape { get => _selectedShape; set { _selectedShape = value; OnPropertyChanged(); } }

        private string? _selectedColor;
        public string? SelectedColor { get => _selectedColor; set { _selectedColor = value; OnPropertyChanged(); } }

        // ====== 로그 원본/뷰 ======
        public ObservableCollection<LogRecord> AllLogs { get; } = new();
        public ICollectionView LogsView { get; }

        // ====== 현재 필터 표시 ======
        private string _activeFilterLabel = "현재 필터: 없음 (전체 표시)";
        public string ActiveFilterLabel { get => _activeFilterLabel; private set { _activeFilterLabel = value; OnPropertyChanged(); } }

        private FilterMode _mode = FilterMode.None;

        // ====== Commands ======
        public ICommand ApplyDeviceFilterCommand { get; }
        public ICommand ApplyInspectionFilterCommand { get; }
        public ICommand ClearFilterCommand { get; }
        public ICommand OpenDetailCommand { get; }
        public ICommand ExportCsvCommand { get; }

        public event PropertyChangedEventHandler? PropertyChanged;

        public ProductionQueryViewModel()
        {
            // ICollectionView 생성 (필터는 View에 걸어둠)
            LogsView = CollectionViewSource.GetDefaultView(AllLogs);
            LogsView.Filter = FilterLogs;

            ApplyDeviceFilterCommand = new RelayCommand(ApplyDeviceFilter);
            ApplyInspectionFilterCommand = new RelayCommand(ApplyInspectionFilter);
            ClearFilterCommand = new RelayCommand(ClearFilter);
            OpenDetailCommand = new RelayCommand<LogRecord>(OpenDetail);
            ExportCsvCommand = new RelayCommand(ExportCsv);

            SeedOptions();
            SeedDummyLogs();

            // 초기값
            DeviceDate = DateTime.Today;
            InspectionDate = DateTime.Today;
        }

        // =========================
        // Filtering
        // =========================

        private bool FilterLogs(object obj)
        {
            if (obj is not LogRecord r) return false;
            if (_mode == FilterMode.None) return true;

            if (_mode == FilterMode.Device)
            {
                // 설비 상태 조회: 날짜(선택) + 장치(선택) + 상태(선택)
                if (DeviceDate.HasValue && r.Timestamp.Date != DeviceDate.Value.Date) return false;
                if (!string.IsNullOrWhiteSpace(SelectedDevice) && !string.Equals(r.Device, SelectedDevice, StringComparison.OrdinalIgnoreCase)) return false;
                if (!string.IsNullOrWhiteSpace(SelectedStatus) && !string.Equals(r.Status, SelectedStatus, StringComparison.OrdinalIgnoreCase)) return false;
                return r.Kind == "설비";
            }

            if (_mode == FilterMode.Inspection)
            {
                // 제품 검사 조회: 날짜(선택) + 모양(선택) + 색깔(선택)
                if (InspectionDate.HasValue && r.Timestamp.Date != InspectionDate.Value.Date) return false;
                if (!string.IsNullOrWhiteSpace(SelectedShape) && !string.Equals(r.Shape, SelectedShape, StringComparison.OrdinalIgnoreCase)) return false;
                if (!string.IsNullOrWhiteSpace(SelectedColor) && !string.Equals(r.Color, SelectedColor, StringComparison.OrdinalIgnoreCase)) return false;
                return r.Kind == "검사";
            }

            return true;
        }

        private void ApplyDeviceFilter()
        {
            _mode = FilterMode.Device;

            var d = DeviceDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "전체";
            var dev = string.IsNullOrWhiteSpace(SelectedDevice) ? "전체" : SelectedDevice;
            var st = string.IsNullOrWhiteSpace(SelectedStatus) ? "전체" : SelectedStatus;

            ActiveFilterLabel = $"현재 필터: [설비] 날짜={d}, 장치={dev}, 상태={st}";
            LogsView.Refresh();
        }

        private void ApplyInspectionFilter()
        {
            _mode = FilterMode.Inspection;

            var d = InspectionDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "전체";
            var sh = string.IsNullOrWhiteSpace(SelectedShape) ? "전체" : SelectedShape;
            var co = string.IsNullOrWhiteSpace(SelectedColor) ? "전체" : SelectedColor;

            ActiveFilterLabel = $"현재 필터: [검사] 날짜={d}, 모양={sh}, 색깔={co}";
            LogsView.Refresh();
        }

        private void ClearFilter()
        {
            _mode = FilterMode.None;
            ActiveFilterLabel = "현재 필터: 없음 (전체 표시)";
            LogsView.Refresh();
        }

        // =========================
        // Detail / Export
        // =========================

        private void OpenDetail(LogRecord r)
        {
            if (r is null) return;

            var sb = new StringBuilder();
            sb.AppendLine($"날짜: {r.Timestamp:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"구분: {r.Kind}");
            sb.AppendLine($"요약: {r.Summary}");
            sb.AppendLine();
            sb.AppendLine($"장치: {r.Device}");
            sb.AppendLine($"상태: {r.Status}");
            sb.AppendLine($"모양: {r.Shape}");
            sb.AppendLine($"색깔: {r.Color}");

            MessageBox.Show(sb.ToString(), "상세 확인", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ExportCsv()
        {
            var dlg = new SaveFileDialog
            {
                Title = "로그 CSV 저장",
                Filter = "CSV (*.csv)|*.csv",
                FileName = $"logs_{DateTime.Now:yyyyMMdd_HHmm}.csv"
            };

            if (dlg.ShowDialog() != true)
                return;

            try
            {
                // 현재 View에 보이는 것만 저장
                var rows = LogsView.Cast<LogRecord>().ToList();

                var lines = new StringBuilder();
                lines.AppendLine("timestamp,kind,summary,device,status,shape,color");

                foreach (var r in rows)
                {
                    string esc(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
                    lines.AppendLine(
                        string.Join(",",
                            esc(r.Timestamp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
                            esc(r.Kind),
                            esc(r.Summary),
                            esc(r.Device),
                            esc(r.Status),
                            esc(r.Shape),
                            esc(r.Color)
                        )
                    );
                }

                File.WriteAllText(dlg.FileName, lines.ToString(), Encoding.UTF8);
                MessageBox.Show($"저장 완료: {dlg.FileName}", "CSV", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "CSV 저장 실패", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // =========================
        // Dummy data (MVP)
        // =========================

        private void SeedOptions()
        {
            // 장치/상태/모양/색깔 옵션 (나중에 DB로 교체)
            Devices.Clear();
            Devices.Add("Camera-1");
            Devices.Add("Camera-2");
            Devices.Add("Sensor-1");
            Devices.Add("Sensor-2");

            Statuses.Clear();
            Statuses.Add("OK");
            Statuses.Add("WARN");
            Statuses.Add("FAIL");

            Shapes.Clear();
            Shapes.Add("A");
            Shapes.Add("B");
            Shapes.Add("C");

            Colors.Clear();
            Colors.Add("Red");
            Colors.Add("Blue");
            Colors.Add("Green");
        }

        private void SeedDummyLogs()
        {
            AllLogs.Clear();

            // 설비 로그
            AllLogs.Add(LogRecord.DeviceLog(DateTime.Today.AddHours(9), "Camera-1", "OK"));
            AllLogs.Add(LogRecord.DeviceLog(DateTime.Today.AddHours(10), "Camera-2", "WARN"));
            AllLogs.Add(LogRecord.DeviceLog(DateTime.Today.AddHours(11), "Sensor-1", "OK"));
            AllLogs.Add(LogRecord.DeviceLog(DateTime.Today.AddHours(12), "Sensor-2", "FAIL"));

            // 검사 로그
            AllLogs.Add(LogRecord.InspectionLog(DateTime.Today.AddHours(9).AddMinutes(10), "A", "Red"));
            AllLogs.Add(LogRecord.InspectionLog(DateTime.Today.AddHours(10).AddMinutes(5), "B", "Blue"));
            AllLogs.Add(LogRecord.InspectionLog(DateTime.Today.AddHours(11).AddMinutes(20), "C", "Green"));
        }

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private enum FilterMode
        {
            None,
            Device,
            Inspection
        }
    }
}
