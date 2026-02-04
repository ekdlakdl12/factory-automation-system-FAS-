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

            // [노란 줄 해결] null 방어 코드
            _connStr = config.GetSection("ConnectionStrings")["MariaDbConnection"] ?? "";

            if (string.IsNullOrEmpty(_connStr))
            {
                System.Diagnostics.Debug.WriteLine("경고: MariaDbConnection 설정이 appsettings.json에 없습니다.");
            }
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

        // 2. [신규/수정] 새 테이블 구조에 맞춰 JSON 데이터 저장
        public async Task<bool> InsertVisionEventFromJsonAsync(int convId)
        {
            // 실제 파일 경로 (VisionWorker 결과물)
            string jsonPath = @"C:\Users\JUNYEONG\Desktop\VisionWorker\VisionWorker\result.json";

            try
            {
                if (!File.Exists(jsonPath))
                {
                    System.Diagnostics.Debug.WriteLine($"파일이 없습니다: {jsonPath}");
                    return false;
                }

                string jsonContent;
                using (var stream = new FileStream(jsonPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(stream))
                {
                    jsonContent = await reader.ReadToEndAsync();
                }

                // [팩트체크] 제공해주신 JSON은 배열 형태이므로 JArray로 파싱 후 마지막(최신) 항목 추출
                var jsonArray = JArray.Parse(jsonContent);
                var data = jsonArray.Last;

                if (data == null) return false;

                using (var conn = new MySqlConnection(_connStr))
                {
                    // 새로 생성하신 CREATE TABLE 구조와 1:1 매칭되는 SQL
                    string sql = @"INSERT INTO VisionEvent 
                                   (conv_id, time_kst, x, y, ms, type, image, detected_class, confidence, meta) 
                                   VALUES 
                                   (@convId, @timeKst, @x, @y, @ms, @type, @image, @class, @conf, @meta)";

                    var parameters = new
                    {
                        convId = convId,
                        timeKst = DateTime.Parse(data["time_kst"]?.ToString() ?? DateTime.Now.ToString()),
                        x = data["x"]?.ToObject<double>() ?? 0,
                        y = data["y"]?.ToObject<double>() ?? 0,
                        ms = data["ms"]?.ToObject<double>() ?? 0,
                        type = data["type"]?.ToString(),
                        image = data["image"]?.ToString(),
                        @class = "NORMAL", // 필요 시 분석 로직 추가
                        conf = 1.0f,
                        meta = data.ToString() // 해당 JSON 오브젝트만 백업
                    };

                    return (await conn.ExecuteAsync(sql, parameters)) > 0;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"상세 에러: {ex.Message}");
                return false;
            }
        }

        // 3. 비전 검사 최신 데이터 가져오기 (WPF 리스트 출력용)
        public async Task<List<VisionEvent>> GetRecentVisionEventsAsync(int limit = 50)
        {
            using (var conn = new MySqlConnection(_connStr))
            {
                // 최근 순으로 정렬하여 새 필드들을 포함해 가져옴
                string sql = "SELECT * FROM VisionEvent ORDER BY time_kst DESC LIMIT @limit";
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