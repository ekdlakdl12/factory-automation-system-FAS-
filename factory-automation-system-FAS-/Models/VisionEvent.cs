using System;
using System.IO;
using Newtonsoft.Json;

namespace factory_automation_system_FAS_.Models
{
    public class VisionEvent
    {
        public int event_id { get; set; } // DB의 event_id와 매핑

        [JsonProperty("conv_id")]
        public int conv_id { get; set; }

        [JsonProperty("time")]
        public DateTime time_kst { get; set; }

        [JsonProperty("barcode")] // JSON에 barcode가 있다면 매핑
        public string? barcode { get; set; }

        public double x { get; set; }
        public double y { get; set; }
        public double ms { get; set; }
        public string? type { get; set; }
        public string? image { get; set; }
        public string? color { get; set; }

        [JsonProperty("label")]
        public string? detected_class { get; set; }

        public float confidence { get; set; }
        public DateTime ts { get; set; } // DB에 있는 ts 컬럼 대응

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