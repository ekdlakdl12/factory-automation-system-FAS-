using System.Windows;
using factory_automation_system_FAS_.ViewModels;

namespace factory_automation_system_FAS_.Views
{
    public partial class ProductionQueryWindow : Window
    {
        public ProductionQueryWindow()
        {
            InitializeComponent();
            DataContext = new ProductionQueryViewModel();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
