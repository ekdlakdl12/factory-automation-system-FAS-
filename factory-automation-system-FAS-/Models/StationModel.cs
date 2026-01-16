// Models/StationModel.cs
using System.Windows;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace factory_automation_system_FAS_.Models
{
    public sealed class StationModel
    {
        public StationId Id { get; }
        public Point Anchor { get; set; } // 픽업/드롭 기준점(지금은 화면 좌표)
        public int Stock { get; set; }    // 쌓인 제품 개수 (MVP는 카운트만)

        public StationModel(StationId id, Point anchor)
        {
            Id = id;
            Anchor = anchor;
        }
    }
}
