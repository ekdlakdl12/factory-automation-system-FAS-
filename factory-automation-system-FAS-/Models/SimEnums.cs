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
        Output,
        RackA,
        RackB
    }

    public enum CartState
    {
        ToOutput,
        Loading,
        ToRack,
        Unloading
    }
}
