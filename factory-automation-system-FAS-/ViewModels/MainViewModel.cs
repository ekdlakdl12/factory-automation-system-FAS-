using System;

namespace factory_automation_system_FAS_.ViewModels
{
    // 팩트체크: partial 키워드가 있어야 여러 파일로 나눌 수 있습니다.
    public partial class MainViewModel : BaseViewModel
    {
        public MainViewModel()
        {
            // MainViewModel.DB.cs에 정의된 메서드 호출
            InitializeDatabase();
        }
    }
}