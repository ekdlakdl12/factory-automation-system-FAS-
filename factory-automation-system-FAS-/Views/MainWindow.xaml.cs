using System.Windows;
using factory_automation_system_FAS_.ViewModels;

namespace factory_automation_system_FAS_.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            this.DataContext = new MainViewModel();
        }

        private void OnTestButtonClick(object sender, RoutedEventArgs e)
        {
            if (this.DataContext is MainViewModel viewModel)
            {
                if (viewModel.CheckDbConnection())
                {
                    var list = viewModel.GetHistoryList();

                    // 팩트체크: ResultText와 HistoryGrid의 이름이 XAML과 일치해야 합니다.
                    ResultText.Text = $"✅ 연결 성공! 현재 저장된 이력: {list.Count}건";
                    HistoryGrid.ItemsSource = list;
                }
                else
                {
                    ResultText.Text = "❌ 연결 실패! 설정을 확인하세요.";
                    MessageBox.Show("DB 연결에 실패했습니다.");
                }
            }
        }
    }
}