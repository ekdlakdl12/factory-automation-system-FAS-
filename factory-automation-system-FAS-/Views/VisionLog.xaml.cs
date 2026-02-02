using System;
using System.Collections.ObjectModel;
using System.Windows;

namespace factory_automation_system_FAS_.Views
{
    public partial class VisionLog : Window
    {
        public VisionLog()
        {
            InitializeComponent();

            // ViewModel 연결
            var vm = new VisionLogViewModel();
            DataContext = vm;

            // F5 실행 시 바로 더미 데이터 몇 줄 추가(동작 확인용)
            Loaded += (_, __) =>
            {
                vm.AddDummyRow("QR", "", "QR: 10,20", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), "Left");
                vm.AddDummyRow("COLOR", "", "RED", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), "Cam1");
                vm.AddDummyRow("MEASURE", "", "25.3 x 10.2", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), "Top");
            };
        }
    }

    // ============================
    // ViewModel (최소)
    // ============================
    public class VisionLogViewModel
    {
        // XAML의 ItemsSource="{Binding Logs}" 와 이름 일치해야 함
        public ObservableCollection<VisionLogRow> Logs { get; } = new ObservableCollection<VisionLogRow>();

        // 팀원이 나중에 JSON 읽어서 여기로 Add 하면 됨
        public void AddRow(VisionLogRow row)
        {
            if (row == null) return;
            Logs.Add(row);
        }

        // 동작 확인용 더미 함수(필요 없으면 삭제 가능)
        public void AddDummyRow(string type, string imagePath, string dataText, string time, string extra)
        {
            Logs.Add(new VisionLogRow
            {
                Type = type,
                ImagePath = imagePath,
                DataText = dataText,
                Time = time,
                Extra = extra
            });
        }
    }

    // ============================
    // DataGrid Row Model (최소)
    // ============================
    public class VisionLogRow
    {
        public string Type { get; set; } = "";       // QR / COLOR / MEASURE 등
        public string ImagePath { get; set; } = "";  // 이미지 경로(팀원이 나중에)
        public string DataText { get; set; } = "";   // 데이터 텍스트
        public string Time { get; set; } = "";       // 시간 문자열
        public string Extra { get; set; } = "";      // 추가 정보
    }
}
