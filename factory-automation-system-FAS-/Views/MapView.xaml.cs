using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace factory_automation_system_FAS_.Views
{
    public partial class MapView : UserControl
    {
        public MapView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            Focusable = true;
            Focus();
            Keyboard.Focus(this);
        }
    }
}
