using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace factory_automation_system_FAS_.ViewModels
{
    // 'partial' 한정자를 추가하여 컴파일 오류를 해결했습니다.
    public partial class MainViewModel : INotifyPropertyChanged
    {
        private string _temp = "0";
        private string _humi = "0";
        private string _co2 = "0";
        private bool _isALineRunning;
        private bool _isBLineRunning;
        private bool _isCLineRunning;

        // 센서 데이터 속성
        public string Temp { get => _temp; set { _temp = value; OnPropertyChanged(); } }
        public string Humi { get => _humi; set { _humi = value; OnPropertyChanged(); } }
        public string Co2 { get => _co2; set { _co2 = value; OnPropertyChanged(); } }

        // PLC 가동 상태 속성 (엑셀 매칭용)
        public bool IsALineRunning { get => _isALineRunning; set { _isALineRunning = value; OnPropertyChanged(); } }
        public bool IsBLineRunning { get => _isBLineRunning; set { _isBLineRunning = value; OnPropertyChanged(); } }
        public bool IsCLineRunning { get => _isCLineRunning; set { _isCLineRunning = value; OnPropertyChanged(); } }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}