using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace factory_automation_system_FAS_.Models
{
    /// <summary>
    /// LOG 리스트 한 줄에 해당하는 공통 모델
    /// (설비 로그 / 검사 로그를 한 리스트로 합치기 위한 형태)
    /// </summary>
    public sealed class LogRecord
    {
        public DateTime Timestamp { get; set; }
        public string Kind { get; set; } = "";      // "설비" or "검사"
        public string Summary { get; set; } = "";

        // 설비 쪽
        public string Device { get; set; } = "";
        public string Status { get; set; } = "";

        // 검사 쪽
        public string Shape { get; set; } = "";
        public string Color { get; set; } = "";

        // DataGrid 표시용
        public string TimestampText => Timestamp.ToString("yyyy-MM-dd HH:mm:ss");

        public static LogRecord DeviceLog(DateTime ts, string device, string status)
        {
            return new LogRecord
            {
                Timestamp = ts,
                Kind = "설비",
                Device = device,
                Status = status,
                Summary = $"[{device}] 상태={status}",
                Shape = "",
                Color = ""
            };
        }

        public static LogRecord InspectionLog(DateTime ts, string shape, string color)
        {
            return new LogRecord
            {
                Timestamp = ts,
                Kind = "검사",
                Shape = shape,
                Color = color,
                Summary = $"[검사] 모양={shape}, 색깔={color}",
                Device = "",
                Status = ""
            };
        }
    }
}
