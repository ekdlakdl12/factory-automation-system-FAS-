using factory_automation_system_FAS_.ViewModels;
using factory_automation_system_FAS_.ViewModels.MapEntities;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace factory_automation_system_FAS_.Behaviors
{
    /// <summary>
    /// 장비(Interactive)만 드래그로 X/Y 이동시키는 Attached Behavior.
    /// 핵심:
    /// - MapCanvas에 RenderTransform(Scale/Translate)이 걸려 있으므로,
    ///   마우스 드래그 delta를 Viewport.Scale로 나눠 "논리 좌표"로 환산해야 함.
    /// - MainViewModel.IsEditMode == true 일 때만 드래그 허용 (ViewMode에서는 이동 금지)
    /// - EditMode에서는 GridSize 기준 Snap 적용
    /// </summary>
    public static class EntityDragBehavior
    {
        public static readonly DependencyProperty IsEnabledProperty =
            DependencyProperty.RegisterAttached(
                "IsEnabled",
                typeof(bool),
                typeof(EntityDragBehavior),
                new PropertyMetadata(false, OnIsEnabledChanged));

        public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);
        public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

        // --- internal state ---
        private static readonly DependencyProperty DragStateProperty =
            DependencyProperty.RegisterAttached(
                "DragState",
                typeof(DragState),
                typeof(EntityDragBehavior),
                new PropertyMetadata(null));

        private sealed class DragState
        {
            public bool IsDragging;
            public bool HasMoved;

            // ✅ View space (relative to ViewportHost)
            public Point StartMouseInHostView;
            public FrameworkElement? ViewportHost;

            // ✅ World space (VM logical coords, seed 기준)
            public double StartX;
            public double StartY;

            public MapEntityVM? Vm;
            public MainViewModel? MainVm;
        }

        private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not UIElement el) return;

            if ((bool)e.NewValue)
            {
                el.PreviewMouseLeftButtonDown += OnDown;
                el.PreviewMouseMove += OnMove;
                el.PreviewMouseLeftButtonUp += OnUp;
                el.LostMouseCapture += OnLostCapture;
            }
            else
            {
                el.PreviewMouseLeftButtonDown -= OnDown;
                el.PreviewMouseMove -= OnMove;
                el.PreviewMouseLeftButtonUp -= OnUp;
                el.LostMouseCapture -= OnLostCapture;
            }
        }

        private static void OnDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement fe) return;

            // DataContext가 MapEntityVM이어야 함
            if (fe.DataContext is not MapEntityVM vm) return;

            // 장비만 드래그 (레이아웃/패스 등은 IsInteractive=false로 들어오므로 차단)
            if (!vm.IsInteractive) return;

            // ✅ 입력 좌표는 ViewportHost 기준으로만 받는다.
            var host = FindViewportHost(fe);
            if (host is null) return;

            // ViewportHost의 DataContext는 MainViewModel이어야 함 (MapView가 MainVM에 바인딩)
            var mainVm = host.DataContext as MainViewModel;

            // ViewMode면 드래그 자체 금지
            if (mainVm != null && !mainVm.IsEditMode)
                return;

            var st = new DragState
            {
                IsDragging = true,
                HasMoved = false,
                ViewportHost = host,
                Vm = vm,
                MainVm = mainVm,
                StartMouseInHostView = e.GetPosition(host),
                StartX = vm.X,
                StartY = vm.Y
            };

            fe.SetValue(DragStateProperty, st);
            fe.CaptureMouse();

            // 버튼 클릭/커맨드를 막지 않음(선택은 그대로 되게)
            e.Handled = false;
        }

        private static void OnMove(object sender, MouseEventArgs e)
        {
            if (sender is not FrameworkElement fe) return;

            var st = fe.GetValue(DragStateProperty) as DragState;
            if (st is null || !st.IsDragging || st.ViewportHost is null || st.Vm is null) return;
            if (!fe.IsMouseCaptured) return;

            // 혹시 드래그 도중 ViewMode로 전환되면 즉시 종료
            if (st.MainVm != null && !st.MainVm.IsEditMode)
            {
                EndDrag(fe, commitSnap: false);
                return;
            }

            // ✅ ViewportHost 기준 View 좌표
            var nowView = e.GetPosition(st.ViewportHost);

            var dxView = nowView.X - st.StartMouseInHostView.X;
            var dyView = nowView.Y - st.StartMouseInHostView.Y;

            // 작은 흔들림은 드래그로 안 봄
            if (!st.HasMoved && (Math.Abs(dxView) + Math.Abs(dyView)) < 2.0)
                return;

            st.HasMoved = true;

            // ✅ 딱 한 번: View delta -> World delta
            double scale = st.MainVm?.Viewport?.Scale ?? 1.0;
            if (scale <= 0.000001) scale = 1.0;

            double dxWorld = dxView / scale;
            double dyWorld = dyView / scale;

            double newX = st.StartX + dxWorld;
            double newY = st.StartY + dyWorld;

            // ✅ EditMode Grid Snap (있으면 적용)
            if (st.MainVm != null && st.MainVm.IsEditMode)
            {
                int grid = st.MainVm.GridSize;
                if (grid > 1)
                {
                    newX = Snap(newX, grid);
                    newY = Snap(newY, grid);
                }
            }

            st.Vm.X = newX;
            st.Vm.Y = newY;

            st.Vm.Touch();
            e.Handled = false;
        }

        private static void OnUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement fe) return;
            EndDrag(fe, commitSnap: true);
            e.Handled = false;
        }

        private static void OnLostCapture(object sender, MouseEventArgs e)
        {
            if (sender is not FrameworkElement fe) return;
            EndDrag(fe, commitSnap: false);
        }

        private static void EndDrag(FrameworkElement fe, bool commitSnap)
        {
            var st = fe.GetValue(DragStateProperty) as DragState;
            if (st is null) return;

            st.IsDragging = false;

            // 마지막에 한 번 더 스냅 고정(마우스업 시점)
            if (commitSnap && st.MainVm != null && st.MainVm.IsEditMode && st.Vm != null)
            {
                int grid = st.MainVm.GridSize;
                if (grid > 1)
                {
                    st.Vm.X = Snap(st.Vm.X, grid);
                    st.Vm.Y = Snap(st.Vm.Y, grid);
                    st.Vm.Touch();
                }
            }

            if (fe.IsMouseCaptured)
                fe.ReleaseMouseCapture();

            fe.ClearValue(DragStateProperty);
        }

        private static double Snap(double v, int grid)
        {
            if (grid <= 1) return v;
            return Math.Round(v / grid) * grid;
        }

        private static FrameworkElement? FindViewportHost(DependencyObject start)
        {
            DependencyObject? cur = start;
            while (cur != null)
            {
                if (cur is FrameworkElement fe && fe.Name == "ViewportHost")
                    return fe;

                cur = VisualTreeHelper.GetParent(cur);
            }
            return null;
        }
    }
}
