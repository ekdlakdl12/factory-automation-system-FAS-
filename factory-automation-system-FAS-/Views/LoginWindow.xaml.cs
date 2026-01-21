// Views/LoginWindow.xaml.cs
using factory_automation_system_FAS_.ViewModels;
using System.Windows;

namespace factory_automation_system_FAS_.Views
{
    public partial class LoginWindow : Window
    {
        private readonly LoginViewModelFixed _vm = new();

        public LoginWindow()
        {
            InitializeComponent();

            DataContext = _vm;
            _vm.LoginSucceeded += OnLoginSucceeded;
        }

        private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            // MVP: PasswordBox는 바인딩이 안 되므로 이벤트로 전달
            if (sender is System.Windows.Controls.PasswordBox pb)
                _vm.Password = pb.Password;
        }

        private void OnLoginSucceeded()
        {
            var main = new MainWindow();
            main.Show();
            Close();
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }
    }
}
