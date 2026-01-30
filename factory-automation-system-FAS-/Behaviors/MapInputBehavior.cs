// Behaviors/MapInputBehavior.cs
using factory_automation_system_FAS_.Models;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using factory_automation_system_FAS_.ViewModels.MapEntities;


namespace factory_automation_system_FAS_.Behaviors
{
    public static class MapInputBehavior
    {
        // ===== TargetCanvas (줌 계산용) =====
        public static readonly DependencyProperty TargetCanvasProperty =
            DependencyProperty.RegisterAttached(
                "TargetCanvas",
                typeof(IInputElement),
                typeof(MapInputBehavior),
                new PropertyMetadata(null));

        public static void SetTargetCanvas(DependencyObject d, IInputElement value) => d.SetValue(TargetCanvasProperty, value);
        public static IInputElement GetTargetCanvas(DependencyObject d) => (IInputElement)d.GetValue(TargetCanvasProperty);

        // ===== Commands =====
        public static readonly DependencyProperty ZoomCommandProperty =
            DependencyProperty.RegisterAttached("ZoomCommand", typeof(ICommand), typeof(MapInputBehavior),
                new PropertyMetadata(null, OnAnyCommandChanged));

        public static void SetZoomCommand(DependencyObject d, ICommand value) => d.SetValue(ZoomCommandProperty, value);
        public static ICommand GetZoomCommand(DependencyObject d) => (ICommand)d.GetValue(ZoomCommandProperty);

        public static readonly DependencyProperty BeginPanCommandProperty =
            DependencyProperty.RegisterAttached("BeginPanCommand", typeof(ICommand), typeof(MapInputBehavior),
                new PropertyMetadata(null, OnAnyCommandChanged));

        public static void SetBeginPanCommand(DependencyObject d, ICommand value) => d.SetValue(BeginPanCommandProperty, value);
        public static ICommand GetBeginPanCommand(DependencyObject d) => (ICommand)d.GetValue(BeginPanCommandProperty);

        public static readonly DependencyProperty PanCommandProperty =
            DependencyProperty.RegisterAttached("PanCommand", typeof(ICommand), typeof(MapInputBehavior),
                new PropertyMetadata(null, OnAnyCommandChanged));

        public static void SetPanCommand(DependencyObject d, ICommand value) => d.SetValue(PanCommandProperty, value);
        public static ICommand GetPanCommand(DependencyObject d) => (ICommand)d.GetValue(PanCommandProperty);

        public static readonly DependencyProperty EndPanCommandProperty =
            DependencyProperty.RegisterAttached("EndPanCommand", typeof(ICommand), typeof(MapInputBehavior),
                new PropertyMetadata(null, OnAnyCommandChanged));

        public static void SetEndPanCommand(DependencyObject d, ICommand value) => d.SetValue(EndPanCommandProperty, value);
        public static ICommand GetEndPanCommand(DependencyObject d) => (ICommand)d.GetValue(EndPanCommandProperty);

        // 내부 상태(캡처/팬 여부) 저장용
        private static readonly DependencyProperty IsPanningProperty =
            DependencyProperty.RegisterAttached("IsPanning", typeof(bool), typeof(MapInputBehavior),
                new PropertyMetadata(false));

        private static void SetIsPanning(DependencyObject d, bool value) => d.SetValue(IsPanningProperty, value);
        private static bool GetIsPanning(DependencyObject d) => (bool)d.GetValue(IsPanningProperty);

        private static readonly DependencyProperty IsHookedProperty =
            DependencyProperty.RegisterAttached("IsHooked", typeof(bool), typeof(MapInputBehavior),
                new PropertyMetadata(false));

        private static void SetIsHooked(DependencyObject d, bool value) => d.SetValue(IsHookedProperty, value);
        private static bool GetIsHooked(DependencyObject d) => (bool)d.GetValue(IsHookedProperty);

        private static void OnAnyCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not UIElement host) return;

            // Command 하나라도 붙으면 이벤트 훅
            if (!GetIsHooked(d))
            {
                host.MouseWheel += Host_MouseWheel;
                host.MouseLeftButtonDown += Host_MouseLeftButtonDown;
                host.MouseLeftButtonUp += Host_MouseLeftButtonUp;
                host.MouseMove += Host_MouseMove;

                SetIsHooked(d, true);
            }
        }

        private static void Host_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is not DependencyObject host) return;
            if (sender is not IInputElement inputHost) return;

            var cmd = GetZoomCommand(host);
            if (cmd is null) return;

            var posOnHost = e.GetPosition(inputHost);

            var args = new ZoomArgs
            {
                Delta = e.Delta,
                MousePosOnCanvas = posOnHost // 이름은 그대로 두고, 값은 host 좌표를 넣는다
            };

            if (cmd.CanExecute(args))
                cmd.Execute(args);

            e.Handled = true;
        }


        private static void Host_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not UIElement ui) return;
            if (sender is not DependencyObject host) return;

            var cmd = GetBeginPanCommand(host);
            if (cmd is null) return;

            // ✅ 엔티티(Interactive) 위에서 클릭한 경우: Pan 시작하지 말고 엔티티 드래그가 먹게 둔다
            if (e.OriginalSource is DependencyObject dep)
            {
                var cur = dep;
                while (cur != null)
                {
                    if (cur is FrameworkElement fe && fe.DataContext is MapEntityVM vm && vm.IsInteractive)
                        return;

                    cur = VisualTreeHelper.GetParent(cur);
                }
            }

            // Host 기준 좌표 (this 말고 host)
            var posOnHost = e.GetPosition(ui);

            var args = new PanArgs { MousePosOnHost = posOnHost };
            if (cmd.CanExecute(args))
                cmd.Execute(args);

            // MVVM 정석: 캡처는 View/Behavior에서 처리
            SetIsPanning(host, true);
            ui.CaptureMouse();

            e.Handled = true;
        }

        private static void Host_MouseMove(object sender, MouseEventArgs e)
        {
            if (sender is not UIElement ui) return;
            if (sender is not DependencyObject host) return;

            if (!GetIsPanning(host)) return;

            var cmd = GetPanCommand(host);
            if (cmd is null) return;

            var posOnHost = e.GetPosition(ui);
            var args = new PanArgs { MousePosOnHost = posOnHost };

            if (cmd.CanExecute(args))
                cmd.Execute(args);

            e.Handled = true;
        }

        private static void Host_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is not UIElement ui) return;
            if (sender is not DependencyObject host) return;

            var cmd = GetEndPanCommand(host);
            if (cmd is not null && cmd.CanExecute(null))
                cmd.Execute(null);

            SetIsPanning(host, false);
            ui.ReleaseMouseCapture();

            e.Handled = true;
        }
    }
}
