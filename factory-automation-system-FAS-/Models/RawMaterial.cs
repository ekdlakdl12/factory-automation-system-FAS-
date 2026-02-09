using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace factory_automation_system_FAS_.Models
{
    public class RawMaterial
    {
        public int raw_id { get; set; }
        public string? supplier { get; set; }
        public string? lot_no { get; set; }
        public DateTime received_at { get; set; }
        public int qty { get; set; }
        public string? location { get; set; }
    }
}
