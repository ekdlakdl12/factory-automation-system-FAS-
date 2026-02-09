using System.ComponentModel;
using System.Runtime.CompilerServices;

// 팩트체크: 모든 ViewModel 파일의 namespace는 아래와 같이 통일하는 것이 가장 안전
// ViewModel들이 공통적으로 가져야 할 'UI 업데이트 기능'을 하나로 모아둔 부모 클래스
namespace factory_automation_system_FAS_.ViewModels
{
    public class BaseViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}