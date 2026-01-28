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

            public Point StartMouseInCanvasVisual; // e.GetPosition(canvas) (visual coords, affected by transform)
            public double StartX;                  // VM logical coords
            public double StartY;

            public double StartScale;              // Viewport.Scale at drag start

            public Canvas? Canvas;
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

            var canvas = FindAncestorCanvas(fe);
            if (canvas is null) return;

            // Canvas의 DataContext는 MainViewModel이어야 함 (MapView가 MainVM에 바인딩)
            var mainVm = canvas.DataContext as MainViewModel;

            // ViewMode면 드래그 자체 금지
            if (mainVm != null && !mainVm.IsEditMode)
                return;

            double scale = 1.0;
            if (mainVm != null)
            {
                scale = mainVm.Viewport?.Scale ?? 1.0;
                if (scale <= 0.000001) scale = 1.0;
            }

            var st = new DragState
            {
                IsDragging = true,
                HasMoved = false,
                Canvas = canvas,
                Vm = vm,
                MainVm = mainVm,
                StartMouseInCanvasVisual = e.GetPosition(canvas),
                StartX = vm.X,
                StartY = vm.Y,
                StartScale = scale
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
            if (st is null || !st.IsDragging || st.Canvas is null || st.Vm is null) return;
            if (!fe.IsMouseCaptured) return;

            // 혹시 드래그 도중 ViewMode로 전환되면 즉시 종료
            if (st.MainVm != null && !st.MainVm.IsEditMode)
            {
                EndDrag(fe, commitSnap: false);
                return;
            }

            var now = e.GetPosition(st.Canvas);

            var dxVisual = now.X - st.StartMouseInCanvasVisual.X;
            var dyVisual = now.Y - st.StartMouseInCanvasVisual.Y;

            // 작은 흔들림은 드래그로 안 봄
            if (!st.HasMoved && (Math.Abs(dxVisual) + Math.Abs(dyVisual)) < 2.0)
                return;

            st.HasMoved = true;

            // ✅ 핵심: Visual delta -> Logical delta
            double scale = st.StartScale;
            if (scale <= 0.000001) scale = 1.0;

            double dx = dxVisual / scale;
            double dy = dyVisual / scale;

            double newX = st.StartX + dx;
            double newY = st.StartY + dy;

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

        private static Canvas? FindAncestorCanvas(DependencyObject start)
        {
            DependencyObject? cur = start;
            while (cur != null)
            {
                if (cur is Canvas c) return c;
                cur = VisualTreeHelper.GetParent(cur);
            }
            return null;
        }
    }
}
