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

            // DEBUG: force receive mouse click even if handled by behaviors
            this.AddHandler(
                UIElement.PreviewMouseLeftButtonDownEvent,
                new MouseButtonEventHandler(OnMapPreviewClick),
                handledEventsToo: true
            );
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

    

        /// <summary>
        /// DEBUG ONLY: capture world coordinate on map click
        /// </summary>
        private void OnMapPreviewClick(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is not ViewModels.MainViewModel vm)
                return;

            if (ViewportHost is null) return;

            var hostPos = e.GetPosition(ViewportHost);

            var s = vm.Viewport.Scale;
            if (s == 0) return;

            var wx = (hostPos.X - vm.Viewport.TranslateX) / s;
            var wy = (hostPos.Y - vm.Viewport.TranslateY) / s;



            System.Diagnostics.Debug.WriteLine(
            $"[MAP CLICK] Host={hostPos}, World=({wx:0.##}, {wy:0.##}) " +
            $"Scale={s:0.####} T=({vm.Viewport.TranslateX:0.##},{vm.Viewport.TranslateY:0.##})"
        );

        }
    }
}
