using System.Windows.Media;

namespace factory_automation_system_FAS_.ViewModels.MapEntities
{
    // 레이아웃용 구역(랙A/B, 성형1/2, 출구 등) - 클릭 불가
    public sealed class LayoutZoneVM : OverlayVM
    {
        public string Kind { get; set; } = "Area";

        public Brush Fill { get; set; } = new SolidColorBrush(Color.FromArgb(25, 46, 230, 166));
        public Brush Stroke { get; set; } = new SolidColorBrush(Color.FromArgb(120, 46, 230, 166));
        public double StrokeThickness { get; set; } = 2;
        public double CornerRadius { get; set; } = 14;

        public double LabelFontSize { get; set; } = 22;

        public LayoutZoneVM(string id) : base(id)
        {
            Width = 300;
            Height = 180;

            IsInteractive = false; // 장비만 클릭
            ZIndex = 5;

            DisplayName = id;
        }
    }
}
