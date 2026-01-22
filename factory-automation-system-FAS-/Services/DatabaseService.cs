using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using MySql.Data.MySqlClient;
using Dapper;
using Newtonsoft.Json.Linq;
using factory_automation_system_FAS_.Models;

namespace factory_automation_system_FAS_.Services
{
    public class DatabaseService
    {
        private readonly string _connStr;

        public DatabaseService()
        {
            var config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            _connStr = config.GetSection("ConnectionStrings")["MariaDbConnection"];
        }

        // 1. DB 연결 상태 체크
        public bool CheckConnection()
        {
            using (var conn = new MySqlConnection(_connStr))
            {
                try { conn.Open(); return true; }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"DB 연결 에러: {ex.Message}"); return false; }
            }
        }

        // 2. JSON 파일을 읽어 VisionEvent 테이블에 저장 (사용자 지정 경로 사용)
        public async Task<bool> InsertVisionEventFromJsonAsync(int convId)
        {
            // [팩트체크] 파일 탐색기 경로 뒤에 파일명을 명시해야 합니다.
            string jsonPath = @"C:\Users\JUNYEONG\Desktop\VisionWorker\VisionWorker\output.json";

            try
            {
                // 파일 존재 여부 확인
                if (!File.Exists(jsonPath))
                {
                    System.Diagnostics.Debug.WriteLine($"파일이 없습니다: {jsonPath}");
                    return false;
                }

                // [권장] C++ 프로그램과 충돌 방지를 위한 공유 읽기 모드
                string jsonContent;
                using (var stream = new FileStream(jsonPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(stream))
                {
                    jsonContent = await reader.ReadToEndAsync();
                }

                var data = Newtonsoft.Json.Linq.JObject.Parse(jsonContent);

                using (var conn = new MySqlConnection(_connStr))
                {
                    string sql = @"INSERT INTO VisionEvent (conv_id, image_ref, detected_class, confidence, ts, meta) 
                           VALUES (@convId, @imageRef, @class, @conf, @ts, @meta)";

                    var parameters = new
                    {
                        convId = convId,
                        imageRef = data["source"]?.ToString(),
                        @class = data["detected_color"]?.ToString() ?? "Box",
                        conf = 1.0,
                        ts = DateTime.Parse(data["ts"]?.ToString() ?? DateTime.Now.ToString()),
                        meta = jsonContent
                    };

                    return (await conn.ExecuteAsync(sql, parameters)) > 0;
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"상세 에러: {ex.Message}");
                return false;
            }
        }

        // 3. 비전 검사 최신 데이터 가져오기
        public async Task<List<VisionEvent>> GetRecentVisionEventsAsync(int limit = 50)
        {
            using (var conn = new MySqlConnection(_connStr))
            {
                string sql = "SELECT * FROM VisionEvent ORDER BY ts DESC LIMIT @limit";
                return (await conn.QueryAsync<VisionEvent>(sql, new { limit })).ToList();
            }
        }

        // --- 기타 테이블 조회 메서드 (기존 유지) ---
        public async Task<List<RawMaterial>> GetRawMaterialsAsync() => await QueryAllAsync<RawMaterial>("RawMaterial");
        public async Task<List<InboundReceipt>> GetInboundReceiptsAsync() => await QueryAllAsync<InboundReceipt>("InboundReceipt", "ts DESC");
        public async Task<List<ProductWorkOrder>> GetWorkOrdersAsync() => await QueryAllAsync<ProductWorkOrder>("Product_WorkOrder", "start_time DESC");
        public async Task<List<Machine>> GetMachinesAsync() => await QueryAllAsync<Machine>("Machine");
        public async Task<List<Inventory>> GetInventoryAsync() => await QueryAllAsync<Inventory>("Inventory");

        private async Task<List<T>> QueryAllAsync<T>(string table, string orderBy = "")
        {
            using (var conn = new MySqlConnection(_connStr))
            {
                string sql = $"SELECT * FROM {table} {(string.IsNullOrEmpty(orderBy) ? "" : "ORDER BY " + orderBy)}";
                return (await conn.QueryAsync<T>(sql)).ToList();
            }
        }
    }
}