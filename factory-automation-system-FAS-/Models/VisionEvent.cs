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

        [JsonProperty("time_kst")]
        public DateTime time_kst { get; set; }

        public double x { get; set; }
        public double y { get; set; }
        public double ms { get; set; }
        public string? type { get; set; }
        public string? image { get; set; }

        [JsonProperty("detected_class")]
        public string? detected_class { get; set; }

        public float confidence { get; set; }
        public string? meta { get; set; }

        public string FullImagePath
        {
            get
            {
                if (string.IsNullOrEmpty(image)) return null;
                string basePath = @"C:\Users\JUNYEONG\Desktop\VisionWorker\VisionWorker\";
                return Path.Combine(basePath, image.Replace("/", @"\"));
            }
        }
    }
}