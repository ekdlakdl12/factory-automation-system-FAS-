using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace factory_automation_system_FAS_.Models
{
    public sealed class MapViewportModel : INotifyPropertyChanged
    {
        // ===== Settings =====
        public double MinZoom { get; set; } = 0.5;
        public double MaxZoom { get; set; } = 3.0;
        public double ZoomStep { get; set; } = 1.12;

        // ===== Transform Props =====
        private double _scale = 1.0;
        public double Scale
        {
            get => _scale;
            private set { _scale = value; OnPropertyChanged(); }
        }

        private double _translateX = 0.0;
        public double TranslateX
        {
            get => _translateX;
            private set { _translateX = value; OnPropertyChanged(); }
        }

        private double _translateY = 0.0;
        public double TranslateY
        {
            get => _translateY;
            private set { _translateY = value; OnPropertyChanged(); }
        }

        // ===== Pan State =====
        private bool _isPanning;
        private Point _panStartMouseInWindow;
        private double _panStartX;
        private double _panStartY;

        // ===== Public API =====
        public void ZoomAt(int wheelDelta, Point mousePosOnCanvas)
        {
            double oldScale = Scale;
            double newScale = oldScale;

            if (wheelDelta > 0) newScale *= ZoomStep;
            else newScale /= ZoomStep;

            newScale = Math.Max(MinZoom, Math.Min(MaxZoom, newScale));
            if (Math.Abs(newScale - oldScale) < 0.0001) return;

            // "마우스 위치 고정" 보정: Translate -= mouse * (new-old)
            double dx = mousePosOnCanvas.X * (newScale - oldScale);
            double dy = mousePosOnCanvas.Y * (newScale - oldScale);

            Scale = newScale;
            TranslateX -= dx;
            TranslateY -= dy;
        }

        public void BeginPan(Point mousePosInWindow)
        {
            _isPanning = true;
            _panStartMouseInWindow = mousePosInWindow;
            _panStartX = TranslateX;
            _panStartY = TranslateY;
        }

        public void PanTo(Point mousePosInWindow)
        {
            if (!_isPanning) return;

            Vector delta = mousePosInWindow - _panStartMouseInWindow;
            TranslateX = _panStartX + delta.X;
            TranslateY = _panStartY + delta.Y;
        }

        public void EndPan()
        {
            _isPanning = false;
        }

        public void Reset()
        {
            Scale = 1.0;
            TranslateX = 0.0;
            TranslateY = 0.0;
        }

        // ===== INotifyPropertyChanged =====
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
