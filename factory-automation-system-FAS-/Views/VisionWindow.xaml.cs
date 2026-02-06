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

        // DB에서 가져온 원본 데이터를 보관 (상세 창 전달용)
        private List<VisionEvent> _rawDbData = new List<VisionEvent>();

        // UI DataGrid와 바인딩되는 요약 리스트
        public ObservableCollection<object> VisionSummaryList { get; set; } = new ObservableCollection<object>();

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
                // 1. JSON 파일 확인 및 DB 동기화
                if (File.Exists(_jsonPath))
                {
                    string jsonContent = await File.ReadAllTextAsync(_jsonPath);
                    var settings = new JsonSerializerSettings();
                    settings.Converters.Add(new IsoDateTimeConverter { DateTimeFormat = "yyyy-MM-dd HH:mm:ss.fff" });

                    var jsonData = JsonConvert.DeserializeObject<List<VisionEvent>>(jsonContent, settings);

                    if (jsonData != null && jsonData.Any())
                    {
                        // DB에 새로운 데이터 저장
                        await _dbService.SaveVisionEventsToDbAsync(jsonData);
                    }
                }

                // 2. DB에서 데이터 조회 (그룹화를 위해 넉넉히 500건 조회)
                _rawDbData = await _dbService.GetRecentVisionEventsAsync(500);

                // 3. 바코드별 그룹화 요약 로직 수행
                var summary = _rawDbData
                    .GroupBy(x => x.barcode ?? "NO_BARCODE")
                    .Select(g => new
                    {
                        conv_id = g.First().conv_id, // 컨베이어
                        barcode = g.Key,             // 바코드
                        TotalCount = g.Count(),      // 총수량
                        // 판정 결과 중 하나라도 불량(NG/b로 시작)이 있으면 "NG"
                        FinalStatus = g.Any(x =>
                            (x.detected_class?.ToUpper().Contains("NG") ?? false) ||
                            (x.detected_class?.ToLower().StartsWith("b") ?? false)) ? "NG" : "OK",
                        LastTime = g.Max(x => x.time_kst) // 들어온시간
                    })
                    .OrderByDescending(x => x.LastTime)
                    .ToList();

                // 4. UI 갱신
                Application.Current.Dispatcher.Invoke(() =>
                {
                    VisionSummaryList.Clear();
                    foreach (var item in summary)
                    {
                        VisionSummaryList.Add(item);
                    }
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"데이터 로드 중 오류: {ex.Message}\n\n[알림] DB의 'barcode' 컬럼 유무를 확인하세요.");
            }
        }

        // 바코드 행 더블클릭 이벤트 (상세 창 열기)
        private void dgVisionSummary_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // DataGrid 이름이 dgVisionSummary인지 확인 필요
            var grid = sender as DataGrid;
            if (grid?.SelectedItem == null) return;

            // 선택된 행의 바코드 값 가져오기
            dynamic selectedItem = grid.SelectedItem;
            string targetBarcode = selectedItem.barcode;

            // 해당 바코드에 해당하는 모든 검사 이력 필터링
            var detailList = _rawDbData
                .Where(x => (x.barcode ?? "NO_BARCODE") == targetBarcode)
                .OrderBy(x => x.time_kst)
                .ToList();

            // 상세 창 생성 및 데이터 전달
            VisionDetailWindow detailWin = new VisionDetailWindow(detailList);
            detailWin.Owner = this;
            detailWin.Title = $"상세 검사 이력 - {targetBarcode}";
            detailWin.ShowDialog();
        }

        // 새로고침 버튼
        private async void btnRefresh_Click(object sender, RoutedEventArgs e) => await RefreshData();

        // 선택 변경 시 이미지 미리보기 (요약 창에서는 그룹의 최신 이미지를 보여줌)
        private void dgVisionSummary_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var grid = sender as DataGrid;
            if (grid?.SelectedItem is null) return;

            dynamic selected = grid.SelectedItem;
            string barcode = selected.barcode;

            // 해당 바코드의 가장 최근 이미지 찾기
            var latestEvent = _rawDbData.FirstOrDefault(x => (x.barcode ?? "NO_BARCODE") == barcode);

            if (latestEvent != null)
            {
                ShowImage(latestEvent.FullImagePath);
            }
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
                else
                {
                    imgPreview.Source = null;
                }
            }
            catch
            {
                imgPreview.Source = null;
            }
        }
    }
}