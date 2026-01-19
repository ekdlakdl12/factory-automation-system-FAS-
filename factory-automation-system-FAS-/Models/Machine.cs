using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace factory_automation_system_FAS_.Models
{
    public class Machine
    {
        public int machine_id { get; set; }
        public string? type { get; set; }
        public string? location { get; set; }
        public string? status { get; set; }
    }
}
