using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using factory_automation_system_FAS_.Models;

namespace factory_automation_system_FAS_.Views
{
    public partial class VisionDetailWindow : Window
    {
        public VisionDetailWindow(List<VisionEvent> details)
        {
            InitializeComponent();
            dgDetails.ItemsSource = details;
        }

        private void dgDetails_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (dgDetails.SelectedItem is VisionEvent selected)
            {
                try
                {
                    // 모델의 경로 속성 확인 (FullImagePath 사용)
                    string path = selected.FullImagePath;

                    if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    {
                        BitmapImage bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.UriSource = new Uri(path, UriKind.Absolute);
                        bitmap.CacheOption = BitmapCacheOption.OnLoad; // 파일 점유 방지 및 즉시 로드
                        bitmap.EndInit();

                        imgDetailPreview.Source = bitmap;
                    }
                    else
                    {
                        imgDetailPreview.Source = null;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"이미지 로드 오류: {ex.Message}");
                    imgDetailPreview.Source = null;
                }
            }
        }
    }
}