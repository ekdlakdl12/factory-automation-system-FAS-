// EnvSensor.cs (환경 센서 데이터)
using System;

namespace factory_automation_system_FAS_.Models
{
    public class EnvSensor
    {
        public int Id { get; set; }
        public float Temp { get; set; }
        public float Humi { get; set; }
        public DateTime RecordedAt { get; set; }
    }
}