using System.Windows.Media;

namespace factory_automation_system_FAS_.ViewModels.MapEntities
{
    // 레이아웃용 컨베이어 블록(클릭 불가)
    public sealed class ConveyorBlockVM : OverlayVM
    {
        public Brush Fill { get; set; } = new SolidColorBrush(Color.FromArgb(22, 255, 255, 255));
        public Brush Stroke { get; set; } = new SolidColorBrush(Color.FromArgb(120, 215, 222, 232));
        public double StrokeThickness { get; set; } = 2;

        public bool ShowCenterLine { get; set; } = true;

        public ConveyorBlockVM(string id) : base(id)
        {
            // base(MapEntityVM) 안에 Width/Height 있음 -> 여기서 값만 세팅
            Width = 140;
            Height = 60;

            IsInteractive = false; // 장비만 클릭
            ZIndex = 55;

            DisplayName = id;
        }
    }
}
