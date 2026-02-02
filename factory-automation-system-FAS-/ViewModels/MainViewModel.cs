using System;

namespace factory_automation_system_FAS_.ViewModels
{
    // 'partial' 키워드를 반드시 포함하여 다른 파일과의 선언 충돌을 방지합니다.
    public partial class MainViewModel : BaseViewModel
    {
        private string _temp = "0";
        private string _humi = "0";
        private string _co2 = "0";
        private string _machineStatus = "READY";
        private bool _isALineRunning;
        private bool _isBLineRunning;
        private bool _isCLineRunning;

        public string Temp { get => _temp; set { _temp = value; OnPropertyChanged(); } }
        public string Humi { get => _humi; set { _humi = value; OnPropertyChanged(); } }
        public string Co2 { get => _co2; set { _co2 = value; OnPropertyChanged(); } }
        public string MachineStatus { get => _machineStatus; set { _machineStatus = value; OnPropertyChanged(); } }
        public bool IsALineRunning { get => _isALineRunning; set { _isALineRunning = value; OnPropertyChanged(); } }
        public bool IsBLineRunning { get => _isBLineRunning; set { _isBLineRunning = value; OnPropertyChanged(); } }
        public bool IsCLineRunning { get => _isCLineRunning; set { _isCLineRunning = value; OnPropertyChanged(); } }

        public MainViewModel()
        {
            // 중복 생성자 에러 방지를 위해 필요한 로직만 최소화하여 배치
        }
    }
}