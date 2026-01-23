using factory_automation_system_FAS_.Views;
using System.Configuration;
using System.Data;
using System.Windows;

namespace factory_automation_system_FAS_
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var login = new LoginWindow();
            MainWindow = login;   // 중요(수명/종료관리 안정화)
            login.Show();
        }
    }

}
