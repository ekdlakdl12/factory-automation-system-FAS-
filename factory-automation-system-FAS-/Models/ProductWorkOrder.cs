using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace factory_automation_system_FAS_.Models
{
    public class ProductWorkOrder
    {
        public int product_id { get; set; }
        public string? wo_id { get; set; }
        public int raw_id { get; set; }
        public DateTime? start_time { get; set; }
        public DateTime? end_time { get; set; }
        public string? status { get; set; }
    }
}
