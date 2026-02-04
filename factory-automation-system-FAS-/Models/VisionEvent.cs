using System;
using System.IO;

namespace factory_automation_system_FAS_.Models
{
    public class VisionEvent
    {
        // 1. DB 테이블 컬럼과 1:1 매칭되는 속성들
        public int id { get; set; }
        public int conv_id { get; set; }
        public DateTime time_kst { get; set; } // ts 대신 사용
        public double x { get; set; }
        public double y { get; set; }
        public double ms { get; set; }
        public string? type { get; set; }
        public string? image { get; set; }      // image_ref 대신 사용 (DB 저장값)
        public string? detected_class { get; set; }
        public float confidence { get; set; }
        public string? meta { get; set; }

        // 2. WPF 화면 표시를 위한 읽기 전용 속성 (DB에는 없음)
        // 노트북(T9NRKD00R203393)의 실제 경로를 조합하여 이미지를 불러옵니다.
        public string FullImagePath
        {
            get
            {
                if (string.IsNullOrEmpty(image)) return null;

                // 기본 경로 (사용자 환경)
                string basePath = @"C:\Users\JUNYEONG\Desktop\VisionWorker\VisionWorker\";

                // DB의 'Visioncaptures/...' 경로를 윈도우 경로 형식으로 변환하여 결합
                return Path.Combine(basePath, image.Replace("/", @"\"));
            }
        }
    }

    // 기존 보조 클래스 유지
    public class HSVValue { public int h { get; set; } public int s { get; set; } public int v { get; set; } }
    public class BBox { public int x { get; set; } public int y { get; set; } public int w { get; set; } public int h { get; set; } }
}