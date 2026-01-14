//비전 검사 결과
namespace factory_automation_system_FAS_.Models
{
    public class VisionResult
    {
        public int Id { get; set; }
        public string Barcode { get; set; } // Foreign Key (ProductionHistory 참조)
        public string DetectedColor { get; set; }
        public string DetectedShape { get; set; }
    }
}