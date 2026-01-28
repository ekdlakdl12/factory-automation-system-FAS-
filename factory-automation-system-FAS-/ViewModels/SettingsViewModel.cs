// ViewModels/SettingsViewModel.cs
using factory_automation_system_FAS_.Models;
using factory_automation_system_FAS_.Services;
using factory_automation_system_FAS_.Utils;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

namespace factory_automation_system_FAS_.ViewModels
{
    /// <summary>
    /// 설정(유저 관리) 화면용 VM
    /// - users.local.csv를 읽어서 사용자 리스트 표시
    /// - 신규 사용자 추가
    /// - 선택 사용자 수정(표시명/권한/사용여부)
    /// - 선택 사용자 비밀번호 재설정
    /// </summary>
    public sealed class SettingsViewModel : INotifyPropertyChanged
    {
        private readonly UserAdminService _svc = new();

        public ObservableCollection<AuthUserRecord> Users { get; } = new();

        private AuthUserRecord? _selectedUser;
        public AuthUserRecord? SelectedUser
        {
            get => _selectedUser;
            set
            {
                _selectedUser = value;
                OnPropertyChanged();

                if (value is null)
                {
                    SelectedDisplayName = "";
                    SelectedRole = "Admin";
                    SelectedEnabled = true;
                    ResetPassword = "";
                }
                else
                {
                    SelectedDisplayName = value.DisplayName ?? "";
                    SelectedRole = string.IsNullOrWhiteSpace(value.Role) ? "Admin" : value.Role;
                    SelectedEnabled = value.Enabled;
                    ResetPassword = "";
                }

                RaiseCommandStates();
            }
        }

        // ===== 신규 사용자 입력 =====
        private string _newUserId = "";
        public string NewUserId
        {
            get => _newUserId;
            set { _newUserId = value; OnPropertyChanged(); RaiseCommandStates(); }
        }

        private string _newDisplayName = "";
        public string NewDisplayName
        {
            get => _newDisplayName;
            set { _newDisplayName = value; OnPropertyChanged(); }
        }

        private string _newRole = "Admin";
        public string NewRole
        {
            get => _newRole;
            set { _newRole = value; OnPropertyChanged(); }
        }

        private bool _newEnabled = true;
        public bool NewEnabled
        {
            get => _newEnabled;
            set { _newEnabled = value; OnPropertyChanged(); }
        }

        private string _newPassword = "";
        public string NewPassword
        {
            get => _newPassword;
            set { _newPassword = value; OnPropertyChanged(); RaiseCommandStates(); }
        }

        // ===== 선택 사용자 수정 =====
        private string _selectedDisplayName = "";
        public string SelectedDisplayName
        {
            get => _selectedDisplayName;
            set { _selectedDisplayName = value; OnPropertyChanged(); }
        }

        private string _selectedRole = "Admin";
        public string SelectedRole
        {
            get => _selectedRole;
            set { _selectedRole = value; OnPropertyChanged(); }
        }

        private bool _selectedEnabled = true;
        public bool SelectedEnabled
        {
            get => _selectedEnabled;
            set { _selectedEnabled = value; OnPropertyChanged(); }
        }

        private string _resetPassword = "";
        public string ResetPassword
        {
            get => _resetPassword;
            set { _resetPassword = value; OnPropertyChanged(); RaiseCommandStates(); }
        }

        // ===== Commands =====
        public ICommand ReloadCommand { get; }
        public ICommand AddUserCommand { get; }
        public ICommand ApplySelectedCommand { get; }
        public ICommand ResetPasswordCommand { get; }
        public ICommand DeleteUserCommand { get; }

        private readonly RelayCommand _addUserCmd;
        private readonly RelayCommand _applySelectedCmd;
        private readonly RelayCommand _resetPasswordCmd;
        private readonly RelayCommand _deleteUserCmd;

        public SettingsViewModel()
        {
            ReloadCommand = new RelayCommand(Load);

            _addUserCmd = new RelayCommand(AddUser, CanAddUser);
            AddUserCommand = _addUserCmd;

            _applySelectedCmd = new RelayCommand(ApplySelected, CanApplySelected);
            ApplySelectedCommand = _applySelectedCmd;

            _resetPasswordCmd = new RelayCommand(DoResetPassword, CanResetPassword);
            ResetPasswordCommand = _resetPasswordCmd;

            _deleteUserCmd = new RelayCommand(DeleteSelected, CanDeleteSelected);
            DeleteUserCommand = _deleteUserCmd;

            Load();
        }

        private void Load()
        {
            try
            {
                var list = _svc.LoadUsers();
                Users.Clear();
                foreach (var u in list.OrderBy(x => x.Id, StringComparer.OrdinalIgnoreCase))
                    Users.Add(u);

                if (SelectedUser is not null)
                {
                    // 선택 유지 시도
                    var keep = Users.FirstOrDefault(x => string.Equals(x.Id, SelectedUser.Id, StringComparison.OrdinalIgnoreCase));
                    SelectedUser = keep;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("유저 목록을 불러오지 못했습니다.\n\n" + ex.Message, "Settings", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            RaiseCommandStates();
        }

        private bool CanAddUser()
        {
            var id = (NewUserId ?? "").Trim();
            var pw = (NewPassword ?? "");
            if (string.IsNullOrWhiteSpace(id)) return false;
            if (pw.Length < 4) return false;
            if (Users.Any(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase))) return false;
            return true;
        }

        private void AddUser()
        {
            var id = (NewUserId ?? "").Trim();
            var display = (NewDisplayName ?? "").Trim();
            var role = string.IsNullOrWhiteSpace(NewRole) ? "Admin" : NewRole.Trim();

            try
            {
                var created = _svc.CreateUser(id, display, role, NewEnabled, NewPassword);

                Users.Add(created);
                SelectedUser = created;

                // 입력칸 초기화
                NewUserId = "";
                NewDisplayName = "";
                NewRole = "Admin";
                NewEnabled = true;
                NewPassword = "";

                MessageBox.Show("사용자가 추가되었습니다.", "Settings", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("사용자 추가 실패:\n\n" + ex.Message, "Settings", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool CanApplySelected() => SelectedUser is not null;

        private void ApplySelected()
        {
            if (SelectedUser is null) return;

            try
            {
                SelectedUser.DisplayName = (SelectedDisplayName ?? "").Trim();
                SelectedUser.Role = string.IsNullOrWhiteSpace(SelectedRole) ? "Admin" : SelectedRole.Trim();
                SelectedUser.Enabled = SelectedEnabled;

                _svc.SaveUsers(Users.ToList());

                // 리스트 정렬 효과(원하면): 갱신
                Load();

                MessageBox.Show("선택 사용자 정보가 저장되었습니다.", "Settings", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("저장 실패:\n\n" + ex.Message, "Settings", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool CanResetPassword() => SelectedUser is not null && (ResetPassword ?? "").Length >= 4;

        private void DoResetPassword()
        {
            if (SelectedUser is null) return;

            try
            {
                _svc.ResetPassword(SelectedUser, ResetPassword);
                _svc.SaveUsers(Users.ToList());

                ResetPassword = "";

                MessageBox.Show("비밀번호가 재설정되었습니다.", "Settings", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("비밀번호 재설정 실패:\n\n" + ex.Message, "Settings", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool CanDeleteSelected()
        {
            if (SelectedUser is null) return false;
            // 최소한 admin 계정(기본 샘플)은 삭제 방지
            if (string.Equals(SelectedUser.Id, "admin", StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        private void DeleteSelected()
        {
            if (SelectedUser is null) return;

            var id = SelectedUser.Id;
            var confirm = MessageBox.Show($"'{id}' 사용자를 삭제할까요?\n(되돌릴 수 없습니다)", "Settings", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                var target = Users.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
                if (target is null) return;

                Users.Remove(target);
                SelectedUser = null;

                _svc.SaveUsers(Users.ToList());

                MessageBox.Show("삭제되었습니다.", "Settings", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("삭제 실패:\n\n" + ex.Message, "Settings", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RaiseCommandStates()
        {
            _addUserCmd.RaiseCanExecuteChanged();
            _applySelectedCmd.RaiseCanExecuteChanged();
            _resetPasswordCmd.RaiseCanExecuteChanged();
            _deleteUserCmd.RaiseCanExecuteChanged();
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
