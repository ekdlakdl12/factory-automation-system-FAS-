// Models/CartModel.cs
using System.Collections.Generic;
using System.Windows;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace factory_automation_system_FAS_.Models
{
    public sealed class CartModel
    {
        public Point Position { get; set; }     // (Canvas 좌표)
        public double SpeedPxPerSec { get; set; } = 260.0;

        public bool HasLoad { get; set; }
        public StationId Target { get; set; } = StationId.RackA;
        public CartState State { get; set; } = CartState.ToOutput;

        // 현재 따라가는 웨이포인트들
        public List<Point> CurrentRoute { get; set; } = new();
        public int RouteIndex { get; set; } = 0;
    }
}
