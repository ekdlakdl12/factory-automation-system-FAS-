using System.Collections.ObjectModel;
using System.Windows;

namespace factory_automation_system_FAS_.ViewModels.MapEntities
{
    // 레이아웃용 라인/통로(Polyline) - 클릭 불가
    public sealed class PathVM : OverlayVM
    {
        public ObservableCollection<Point> Points { get; } = new();

        public double Thickness { get; set; } = 6;
        public string Kind { get; set; } = "Line";

        public PathVM(string id) : base(id)
        {
            IsInteractive = false; // 장비만 클릭
            ZIndex = 25;

            DisplayName = id;
        }
    }
}
