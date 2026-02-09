using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace factory_automation_system_FAS_.Models
{
    public class Inventory
    {
        public int inv_id { get; set; }
        public int material_id { get; set; }
        public int qty { get; set; }
        public string? location { get; set; }
        public DateTime updated_at { get; set; }
    }
}
