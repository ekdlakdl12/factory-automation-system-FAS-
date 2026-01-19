using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using MySql.Data.MySqlClient;
using Dapper;
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

        // 1. 공통: 연결 상태 체크
        public bool CheckConnection()
        {
            using (var conn = new MySqlConnection(_connStr))
            {
                try { conn.Open(); return true; }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"DB 연결 에러: {ex.Message}"); return false; }
            }
        }

        // 2. RawMaterial (원자재 마스터)
        public async Task<List<RawMaterial>> GetRawMaterialsAsync()
        {
            using (var conn = new MySqlConnection(_connStr))
            {
                return (await conn.QueryAsync<RawMaterial>("SELECT * FROM RawMaterial")).ToList();
            }
        }

        // 3. InboundReceipt (입고 이력)
        public async Task<List<InboundReceipt>> GetInboundReceiptsAsync()
        {
            using (var conn = new MySqlConnection(_connStr))
            {
                return (await conn.QueryAsync<InboundReceipt>("SELECT * FROM InboundReceipt ORDER BY ts DESC")).ToList();
            }
        }

        // 4. Product_WorkOrder (작업 지시)
        public async Task<List<ProductWorkOrder>> GetWorkOrdersAsync()
        {
            using (var conn = new MySqlConnection(_connStr))
            {
                return (await conn.QueryAsync<ProductWorkOrder>("SELECT * FROM Product_WorkOrder ORDER BY start_time DESC")).ToList();
            }
        }

        // 5. Machine (설비 정보)
        public async Task<List<Machine>> GetMachinesAsync()
        {
            using (var conn = new MySqlConnection(_connStr))
            {
                return (await conn.QueryAsync<Machine>("SELECT * FROM Machine")).ToList();
            }
        }

        // 6. Conveyor (라인 정보)
        public async Task<List<Conveyor>> GetConveyorsAsync()
        {
            using (var conn = new MySqlConnection(_connStr))
            {
                return (await conn.QueryAsync<Conveyor>("SELECT * FROM Conveyor")).ToList();
            }
        }

        // 7. AMRTask (로봇 이송 로그)
        public async Task<List<AMRTask>> GetAMRTasksAsync()
        {
            using (var conn = new MySqlConnection(_connStr))
            {
                return (await conn.QueryAsync<AMRTask>("SELECT * FROM AMRTask ORDER BY start_ts DESC")).ToList();
            }
        }

        // 8. VisionEvent (비전 검사 결과)
        public async Task<List<VisionEvent>> GetRecentVisionEventsAsync(int limit = 50)
        {
            using (var conn = new MySqlConnection(_connStr))
            {
                string sql = "SELECT * FROM VisionEvent ORDER BY ts DESC LIMIT @limit";
                return (await conn.QueryAsync<VisionEvent>(sql, new { limit })).ToList();
            }
        }

        // 9. Inventory (현재고 현황)
        public async Task<List<Inventory>> GetInventoryAsync()
        {
            using (var conn = new MySqlConnection(_connStr))
            {
                return (await conn.QueryAsync<Inventory>("SELECT * FROM Inventory")).ToList();
            }
        }

        // 10. TraceLog (시스템 전체 통합 로그)
        public async Task<List<TraceLog>> GetTraceLogsAsync()
        {
            using (var conn = new MySqlConnection(_connStr))
            {
                return (await conn.QueryAsync<TraceLog>("SELECT * FROM TraceLog ORDER BY ts DESC")).ToList();
            }
        }
    }
}