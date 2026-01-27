// Models/StationModel.cs
using System.Windows;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace factory_automation_system_FAS_.Models
{
    public enum StationId
    {
        RackA,
        RackB,
        Forming1,
        Forming2,
        Output
    }

    public enum CartState
    {
        /// <summary>왼쪽(픽업 영역)에서 다음 사이클 시작 대기</summary>
        IdleAtLeft,

        /// <summary>제품을 싣고 오른쪽(드롭 영역)으로 이동 중</summary>
        ToDrop,

        /// <summary>드롭 지점에서 대기(기본 60초)</summary>
        DroppingWait,

        /// <summary>빈 카트로 왼쪽으로 복귀 중</summary>
        ReturningEmpty
    }
}
