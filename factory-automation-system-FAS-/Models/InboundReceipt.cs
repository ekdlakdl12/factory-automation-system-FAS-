using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace factory_automation_system_FAS_.Models
{
    public class InboundReceipt
    {
        public int receipt_id { get; set; }
        public int raw_id { get; set; }
        public string? inspector { get; set; }
        public string? status { get; set; }
        public DateTime ts { get; set; }
    }
}
