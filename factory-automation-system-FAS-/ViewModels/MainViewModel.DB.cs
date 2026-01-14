using factory_automation_system_FAS_.Services; // DatabaseService 위치 확인

namespace factory_automation_system_FAS_.ViewModels
{
    // 팩트체크: 파일 1과 네임스페이스 및 클래스명이 완벽히 일치해야 함
    public partial class MainViewModel
    {
        private readonly DatabaseService _dbService = new DatabaseService();
        private string _dbStatus = "대기 중...";

        public string DbStatus
        {
            get => _dbStatus;
            set
            {
                _dbStatus = value;
                OnPropertyChanged(); // BaseViewModel에서 상속받은 메서드
            }
        }

        // DB 초기화 로직
        private void InitializeDatabase()
        {
            if (_dbService.CheckConnection())
            {
                DbStatus = "라즈베리파이 DB 연결 성공!";
            }
            else
            {
                DbStatus = "DB 연결 실패 (네트워크 확인 필요)";
            }
        }
    }
}