// ViewModels/LoginViewModel.cs
using factory_automation_system_FAS_.Services;
using factory_automation_system_FAS_.Utils;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;

namespace factory_automation_system_FAS_.ViewModels
{
    public sealed class LoginViewModelFixed : INotifyPropertyChanged
    {
        private readonly AuthService _auth = new();
        private readonly DispatcherTimer _lockTimer = new();

        private const int MaxFailCount = 5;
        private const int LockSeconds = 30;

        private int _failCount;
        private int _lockRemaining;

        private string _userId = "";
        public string UserId
        {
            get => _userId;
            set
            {
                _userId = value;
                OnPropertyChanged();
                RaiseLoginCanExecuteChanged();  
            }
        }

        private string _password = "";
        public string Password
        {
            get => _password;
            set
            {
                _password = value;
                OnPropertyChanged();
                RaiseLoginCanExecuteChanged();   
            }
        }


        private string _error = "";
        public string Error { get => _error; private set { _error = value; OnPropertyChanged(); } }

        public bool IsLocked => _lockRemaining > 0;
        public string LockMessage => IsLocked ? $"로그인 시도가 너무 많습니다. {_lockRemaining}초 후 다시 시도하세요." : "";

        public ICommand LoginCommand { get; }

        public event Action? LoginSucceeded;

        public LoginViewModelFixed()
        {
            LoginCommand = new RelayCommand(DoLogin, CanLogin);

            _lockTimer.Interval = TimeSpan.FromSeconds(1);
            _lockTimer.Tick += (_, __) =>
            {
                if (_lockRemaining > 0)
                {
                    _lockRemaining--;
                    OnPropertyChanged(nameof(IsLocked));
                    OnPropertyChanged(nameof(LockMessage));
                    RaiseLoginCanExecuteChanged();
                }

                if (_lockRemaining <= 0)
                    _lockTimer.Stop();
            };
        }

        private bool CanLogin()
        {
            if (IsLocked) return false;
            return !string.IsNullOrWhiteSpace(UserId) && !string.IsNullOrWhiteSpace(Password);
        }

        private void DoLogin()
        {
            if (IsLocked) return;

            Error = "";

            var result = _auth.Validate(UserId.Trim(), Password);

            // 보안상 Password는 즉시 비움 (UI는 PasswordBox가 따로 관리)
            Password = "";

            if (result.Ok)
            {
                _failCount = 0;
                Error = "";
                LoginSucceeded?.Invoke();
                return;
            }

            _failCount++;
            Error = string.IsNullOrWhiteSpace(result.Message) ? "아이디/비밀번호가 올바르지 않습니다" : result.Message;

            if (_failCount >= MaxFailCount)
            {
                _failCount = 0;
                _lockRemaining = LockSeconds;
                OnPropertyChanged(nameof(IsLocked));
                OnPropertyChanged(nameof(LockMessage));
                _lockTimer.Start();
            }

            RaiseLoginCanExecuteChanged();
        }

        private void RaiseLoginCanExecuteChanged()
        {
            if (LoginCommand is RelayCommand rc)
                rc.RaiseCanExecuteChanged();
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
