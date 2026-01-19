using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace factory_automation_system_FAS_.Models
{
    public class AMRTask
    {
        public int task_id { get; set; }
        public int amr_id { get; set; }
        public string? from_loc { get; set; }
        public string? to_loc { get; set; }
        public DateTime start_ts { get; set; }
        public DateTime? end_ts { get; set; }
        public string? status { get; set; }
        public string? payload { get; set; } // JSON 데이터
    }
}