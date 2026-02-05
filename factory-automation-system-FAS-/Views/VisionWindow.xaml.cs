using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using factory_automation_system_FAS_.Models;
using factory_automation_system_FAS_.Services;

namespace factory_automation_system_FAS_.Views
{
    public partial class VisionWindow : Window
    {
        private readonly DatabaseService _dbService = new DatabaseService();
        private readonly string _jsonPath = @"C:\Users\JUNYEONG\Desktop\VisionWorker\VisionWorker\total.json";

        // UI와 바인딩되는 컬렉션
        public ObservableCollection<VisionEvent> VisionList { get; set; } = new ObservableCollection<VisionEvent>();

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
                if (!File.Exists(_jsonPath)) return;

                // 1. JSON 파일 로드
                string jsonContent = await File.ReadAllTextAsync(_jsonPath);

                // 2. 날짜 및 시간 포맷 설정 (밀리초 포함)
                var settings = new JsonSerializerSettings();
                settings.Converters.Add(new IsoDateTimeConverter { DateTimeFormat = "yyyy-MM-dd HH:mm:ss.fff" });

                // 3. 역직렬화 (color 포함된 모델로 변환)
                var jsonData = JsonConvert.DeserializeObject<List<VisionEvent>>(jsonContent, settings);

                if (jsonData != null && jsonData.Any())
                {
                    // 4. DB 저장 (DatabaseService 내부에 color 컬럼 처리가 되어 있어야 함)
                    await _dbService.SaveVisionEventsToDbAsync(jsonData);

                    // 5. DB에서 최신 데이터 50건 조회
                    var dbData = await _dbService.GetRecentVisionEventsAsync(50);

                    // 6. UI 스레드에서 리스트 갱신
                    Application.Current.Dispatcher.Invoke(() => {
                        VisionList.Clear();
                        foreach (var item in dbData)
                        {
                            VisionList.Add(item);
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                // DB명 'fas_monitoring_db' 확인 메시지 포함
                MessageBox.Show($"오류 발생: {ex.Message}\n\n[알림] DB 이름이 'fas_monitoring_db'인지, 'color' 컬럼이 생성되어 있는지 확인하세요.");
            }
        }

        // 데이터 그리드 행 선택 시 이미지 미리보기
        private void dgVisionEvents_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (dgVisionEvents.SelectedItem is VisionEvent selected)
            {
                try
                {
                    string path = selected.FullImagePath;
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

        // 새로고침 버튼 클릭 이벤트
        private async void btnRefresh_Click(object sender, RoutedEventArgs e) => await RefreshData();
    }
}