using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace factory_automation_system_FAS_.Models
{
    public class TraceLog
    {
        public int trace_id { get; set; }
        public string? entity_type { get; set; }
        public int entity_id { get; set; }
        public string? action { get; set; }
        public DateTime ts { get; set; }
        public string? user_id { get; set; }
        public string? detail { get; set; } // JSON 데이터
    }
}
