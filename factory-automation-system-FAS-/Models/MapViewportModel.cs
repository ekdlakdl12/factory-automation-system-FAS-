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

        // ===== Fit-to-View Settings =====
        /// <summary>
        /// Padding (in view pixels) applied when fitting content into the host.
        /// </summary>
        public double FitPadding { get; set; } = 24.0;

        /// <summary>
        /// Optional content size used by Fit/Reset when you want to fit the whole map.
        /// Defaults match MapView canvas size.
        /// </summary>
        public double ContentWidth { get; set; } = 3100.0;
        public double ContentHeight { get; set; } = 1700.0;

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

        // ===== Host/View Size =====
        private double _hostWidth;
        public double HostWidth
        {
            get => _hostWidth;
            private set { _hostWidth = value; OnPropertyChanged(); }
        }

        private double _hostHeight;
        public double HostHeight
        {
            get => _hostHeight;
            private set { _hostHeight = value; OnPropertyChanged(); }
        }

        // ===== Pan State =====
        private bool _isPanning;
        private Point _panStartMouseInWindow;
        private double _panStartX;
        private double _panStartY;

        // ===== Public API =====
        /// <summary>
        /// Update current host(view) size. When autoFit is true and the host size is valid,
        /// the viewport will be fit to the content.
        /// </summary>
        public void UpdateHostSize(double width, double height, bool autoFit = false)
        {
            if (double.IsNaN(width) || double.IsNaN(height)) return;
            if (width <= 0 || height <= 0) return;

            HostWidth = width;
            HostHeight = height;

            if (autoFit)
                FitToContent();
        }

        /// <summary>
        /// Fit the configured content rectangle (ContentWidth/ContentHeight) into the current host(view).
        /// World coords are not mutated; only Scale/Translate are updated.
        /// </summary>
        public void FitToContent()
        {
            if (HostWidth <= 0 || HostHeight <= 0) return;
            if (ContentWidth <= 0 || ContentHeight <= 0) return;

            var pad = Math.Max(0, FitPadding);

            double availW = Math.Max(1, HostWidth - (pad * 2));
            double availH = Math.Max(1, HostHeight - (pad * 2));

            double scaleX = availW / ContentWidth;
            double scaleY = availH / ContentHeight;
            double newScale = Math.Min(scaleX, scaleY);
            newScale = Math.Max(MinZoom, Math.Min(MaxZoom, newScale));

            Scale = newScale;

            // Center content within host.
            TranslateX = (HostWidth - (ContentWidth * Scale)) / 2.0;
            TranslateY = (HostHeight - (ContentHeight * Scale)) / 2.0;
        }

        public void ZoomAt(int wheelDelta, Point mousePosOnHost)
        {
            double oldScale = Scale;
            double newScale = oldScale;

            if (wheelDelta > 0) newScale *= ZoomStep;
            else newScale /= ZoomStep;

            newScale = Math.Max(MinZoom, Math.Min(MaxZoom, newScale));
            if (Math.Abs(newScale - oldScale) < 0.0001) return;

            // World point currently under cursor:
            // w = (mouse - T) / oldScale
            double wx = (mousePosOnHost.X - TranslateX) / oldScale;
            double wy = (mousePosOnHost.Y - TranslateY) / oldScale;

            // New translation to keep that world point under cursor:
            // T' = mouse - (w * newScale)
            Scale = newScale;
            TranslateX = mousePosOnHost.X - (wx * newScale);
            TranslateY = mousePosOnHost.Y - (wy * newScale);
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
            // Prefer fit-to-content when host size is known.
            if (HostWidth > 0 && HostHeight > 0)
            {
                FitToContent();
                return;
            }

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
