using System;
using System.IO;
using Newtonsoft.Json;

namespace factory_automation_system_FAS_.Models
{
    public class VisionEvent
    {
        public int id { get; set; }

        [JsonProperty("conv_id")]
        public int conv_id { get; set; }

        [JsonProperty("time")] // JSON의 "time" 매핑
        public DateTime time_kst { get; set; }

        public double x { get; set; }
        public double y { get; set; }
        public double ms { get; set; }
        public string? type { get; set; }
        public string? image { get; set; }

        // 추가: JSON의 "color" 값을 저장
        [JsonProperty("color")]
        public string? color { get; set; }

        [JsonProperty("label")] // JSON의 "label" 매핑
        public string? detected_class { get; set; }

        public float confidence { get; set; }
        public string? meta { get; set; }

        public string FullImagePath
        {
            get
            {
                if (string.IsNullOrEmpty(image)) return null;
                string cleanPath = image.Replace("./", "").Replace("/", @"\");
                string basePath = @"C:\Users\JUNYEONG\Desktop\VisionWorker\VisionWorker\";
                return Path.Combine(basePath, cleanPath);
            }
        }
    }
}