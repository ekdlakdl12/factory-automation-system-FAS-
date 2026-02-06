using factory_automation_system_FAS_.Models;
using factory_automation_system_FAS_.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace factory_automation_system_FAS_.Views
{
    public partial class VisionWindow : Window
    {
        private readonly DatabaseService _dbService = new DatabaseService();
        private readonly string _jsonPath = @"C:\Users\JUNYEONG\Desktop\VisionWorker\VisionWorker\total.json";

        private List<VisionEvent> _rawDbData = new List<VisionEvent>();
        public ObservableCollection<object> VisionSummaryList { get; set; } = new ObservableCollection<object>();

        // 마지막으로 처리한 JSON 파일의 시간을 저장 (불필요한 DB 작업 방지)
        private DateTime _lastJsonTime = DateTime.MinValue;

        public VisionWindow()
        {
            InitializeComponent();
            this.DataContext = this;
            this.Loaded += async (s, e) => await RefreshData();
        }

        private async Task RefreshData()
        {
            try
            {
                if (File.Exists(_jsonPath))
                {
                    // 1. 파일의 마지막 수정 시간 확인
                    DateTime currentFileTime = File.GetLastWriteTime(_jsonPath);

                    // 파일 시간이 이전과 같다면 DB 저장 로직은 건너뜁니다.
                    if (currentFileTime > _lastJsonTime)
                    {
                        string jsonContent = await File.ReadAllTextAsync(_jsonPath);
                        var settings = new JsonSerializerSettings();
                        settings.Converters.Add(new IsoDateTimeConverter { DateTimeFormat = "yyyy-MM-dd HH:mm:ss.fff" });

                        var jsonData = JsonConvert.DeserializeObject<List<VisionEvent>>(jsonContent, settings);

                        if (jsonData != null && jsonData.Any())
                        {
                            await _dbService.SaveVisionEventsToDbAsync(jsonData);
                        }
                        _lastJsonTime = currentFileTime;
                    }
                }

                // 2. DB에서 데이터 조회 (최신 500건)
                var dbData = await _dbService.GetRecentVisionEventsAsync(500);

                // 3. 그룹화 및 요약 (중복 방지를 위해 Distinct 또는 GroupBy 활용)
                var summary = dbData
                    .GroupBy(x => x.barcode ?? "NO_BARCODE")
                    .Select(g => new
                    {
                        conv_id = g.First().conv_id,
                        barcode = g.Key,
                        TotalCount = g.Count(),
                        FinalStatus = g.Any(x =>
                            (x.detected_class?.ToUpper().Contains("NG") ?? false) ||
                            (x.detected_class?.ToLower().StartsWith("b") ?? false)) ? "NG" : "OK",
                        LastTime = g.Max(x => x.time_kst)
                    })
                    .OrderByDescending(x => x.LastTime)
                    .ToList();

                // 4. UI 갱신 (기존 데이터와 비교하여 변경사항이 있을 때만 갱신하거나 전체 클리어 후 삽입)
                Application.Current.Dispatcher.Invoke(() =>
                {
                    // 단순 Clear 후 Add는 깜빡임이 있을 수 있지만, 
                    // 데이터 중복 표시 문제는 여기서 요약(summary)을 새로 생성함으써 해결됩니다.
                    VisionSummaryList.Clear();
                    foreach (var item in summary)
                    {
                        VisionSummaryList.Add(item);
                    }
                    _rawDbData = dbData; // 상세 창용 데이터 업데이트
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Refresh Error: {ex.Message}");
            }
        }

        private void dgVisionSummary_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (dgVisionSummary.SelectedItem == null) return;

            dynamic selectedItem = dgVisionSummary.SelectedItem;
            string targetBarcode = selectedItem.barcode;

            var detailList = _rawDbData
                .Where(x => (x.barcode ?? "NO_BARCODE") == targetBarcode)
                .OrderBy(x => x.time_kst)
                .ToList();

            VisionDetailWindow detailWin = new VisionDetailWindow(detailList);
            detailWin.Owner = this;
            detailWin.ShowDialog();
        }

        private async void btnRefresh_Click(object sender, RoutedEventArgs e) => await RefreshData();

        private void dgVisionSummary_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (dgVisionSummary.SelectedItem is null) return;
            dynamic selected = dgVisionSummary.SelectedItem;
            string barcode = selected.barcode;

            var latestEvent = _rawDbData.FirstOrDefault(x => (x.barcode ?? "NO_BARCODE") == barcode);
            if (latestEvent != null) ShowImage(latestEvent.FullImagePath);
        }

        private void ShowImage(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    BitmapImage bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(path, UriKind.Absolute);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    imgPreview.Source = bitmap;
                }
                else imgPreview.Source = null;
            }
            catch { imgPreview.Source = null; }
        }
    }
}