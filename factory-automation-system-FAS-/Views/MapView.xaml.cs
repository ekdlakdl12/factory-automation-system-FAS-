using factory_automation_system_FAS_.ViewModels;
using System;
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
            SizeChanged += OnSizeChanged;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            Focusable = true;
            Focus();
            Keyboard.Focus(this);

            // Fit-to-view on first render when actual sizes are known.
            UpdateViewportHostSize(autoFit: true);
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Keep host size updated; do not auto-fit on every resize to avoid fighting user pan/zoom.
            UpdateViewportHostSize(autoFit: false);
        }

        private void UpdateViewportHostSize(bool autoFit)
        {
            try
            {
                if (ViewportHost is null) return;

                if (DataContext is MainViewModel vm)
                {
                    vm.Viewport.UpdateHostSize(ViewportHost.ActualWidth, ViewportHost.ActualHeight, autoFit);
                }
            }
            catch (Exception)
            {
                // No-throw: UI should never crash due to viewport sizing.
            }
        }
    }
}
