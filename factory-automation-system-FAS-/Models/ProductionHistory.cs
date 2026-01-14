// ProductionHistory.cs (생산 이력)
using System;

namespace factory_automation_system_FAS_.Models
{
    public class ProductionHistory
    {
        public string Barcode { get; set; } // Primary Key
        public int TotalQuantity { get; set; }
        public int GoodQuantity { get; set; }
        public float DefectRate { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}