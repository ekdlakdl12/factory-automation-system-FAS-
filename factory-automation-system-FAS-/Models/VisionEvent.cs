using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace factory_automation_system_FAS_.Models
{
    public class VisionEvent
    {
        public int event_id { get; set; }
        public int conv_id { get; set; }
        public string? image_ref { get; set; }
        public string? detected_class { get; set; }
        public float confidence { get; set; }
        public DateTime ts { get; set; }
        public string? meta { get; set; } // JSON 데이터
    }
}
