using System;

namespace factory_automation_system_FAS_.ViewModels
{
    // 팩트체크: : BaseViewModel 상속이 여기서 명시되어야 합니다.
    public partial class MainViewModel : BaseViewModel
    {
        public MainViewModel()
        {
            InitializeDatabase();
        }
    }
}