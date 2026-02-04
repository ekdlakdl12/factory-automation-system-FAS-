using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using factory_automation_system_FAS_.Models;
using factory_automation_system_FAS_.Services;

namespace factory_automation_system_FAS_.Views
{
    public partial class VisionWindow : Window
    {
        private readonly DatabaseService _dbService = new DatabaseService();

        // 데이터 바인딩을 위한 컬렉션
        public ObservableCollection<VisionEvent> VisionList { get; set; } = new ObservableCollection<VisionEvent>();

        public VisionWindow()
        {
            InitializeComponent();

            // XAML 바인딩을 위한 Context 연결
            this.DataContext = this;

            // 초기 데이터 로딩
            LoadData();
        }

        private async void LoadData()
        {
            try
            {
                var data = await _dbService.GetRecentVisionEventsAsync(50);

                VisionList.Clear();
                if (data != null)
                {
                    foreach (var item in data)
                    {
                        VisionList.Add(item);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LoadData 에러] {ex.Message}");
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
                        // [팩트체크] 비전 프로그램과 충돌 방지를 위해 메모리에 로드 후 파일 연결 해제
                        BitmapImage bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.UriSource = new Uri(path, UriKind.Absolute);
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.EndInit();

                        imgPreview.Source = bitmap;
                        txtImagePath.Text = path;
                    }
                    else
                    {
                        imgPreview.Source = null;
                        txtImagePath.Text = "파일을 찾을 수 없습니다: " + path;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[이미지 로드 에러] {ex.Message}");
                    imgPreview.Source = null;
                }
            }
        }

        private void btnRefresh_Click(object sender, RoutedEventArgs e)
        {
            LoadData();
        }
    }
}