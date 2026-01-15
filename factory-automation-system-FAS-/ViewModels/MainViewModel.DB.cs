using System.Collections.Generic;
using factory_automation_system_FAS_.Services;
using factory_automation_system_FAS_.Models; // 팩트체크: 이 using이 없으면 GetHistoryList에서 오류남

namespace factory_automation_system_FAS_.ViewModels
{
    public partial class MainViewModel
    {
        private readonly DatabaseService _dbService = new DatabaseService();
        private string _dbStatus = "대기 중...";

        public string DbStatus
        {
            get => _dbStatus;
            set { _dbStatus = value; OnPropertyChanged(); }
        }

        private void InitializeDatabase()
        {
            if (CheckDbConnection()) DbStatus = "라즈베리파이 DB 연결 성공!";
            else DbStatus = "DB 연결 실패";
        }

        // MainWindow.xaml.cs에서 호출하는 메서드들
        public bool CheckDbConnection() => _dbService.CheckConnection();

        public List<ProductionHistory> GetHistoryList() => _dbService.GetProductionHistory();
    }
}