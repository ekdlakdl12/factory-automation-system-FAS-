using System;

namespace factory_automation_system_FAS_.Models
{
    public class ProductionHistory
    {
        // 팩트체크: DB 컬럼명과 100% 일치해야 매핑 오류가 없습니다.
        public string barcode { get; set; }
        public int total_quantity { get; set; }
        public int good_quantity { get; set; }
        public float defect_rate { get; set; }
        public DateTime created_at { get; set; }
    }
}