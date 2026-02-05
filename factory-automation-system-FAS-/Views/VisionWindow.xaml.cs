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
        private readonly string _jsonPath = @"C:\Users\JUNYEONG\Desktop\VisionWorker\VisionWorker\result.json";
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

                // 1. JSON 읽기
                string jsonContent = await File.ReadAllTextAsync(_jsonPath);

                // 2. 특수 날짜 형식 처리를 위한 컨버터 설정
                var settings = new JsonSerializerSettings();
                settings.Converters.Add(new IsoDateTimeConverter { DateTimeFormat = "yyyy-MM-dd HH:mm:ss.FFFK" });

                var jsonData = JsonConvert.DeserializeObject<List<VisionEvent>>(jsonContent, settings);

                // 3. DB 작업
                if (jsonData != null && jsonData.Any())
                {
                    // DB 연결 확인 (잘못된 DB명 사용 시 여기서 에러 감지)
                    await _dbService.SaveVisionEventsToDbAsync(jsonData);

                    var dbData = await _dbService.GetRecentVisionEventsAsync(50);

                    Application.Current.Dispatcher.Invoke(() => {
                        VisionList.Clear();
                        foreach (var item in dbData) VisionList.Add(item);
                    });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"오류 발생: {ex.Message}\n\n[도움말] appsettings.json의 Database 이름이 'fas_monitoring_db'인지 확인하세요.");
            }
        }

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
                }
                catch { imgPreview.Source = null; }
            }
        }

        private async void btnRefresh_Click(object sender, RoutedEventArgs e) => await RefreshData();
    }
}