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

            _connStr = config.GetSection("ConnectionStrings")["MariaDbConnection"] ?? "";
        }

        public bool CheckConnection()
        {
            try
            {
                using (var conn = new MySqlConnection(_connStr))
                {
                    conn.Open();
                    return true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DB 연결 실패] {ex.Message}");
                return false;
            }
        }

        public async Task SaveVisionEventsToDbAsync(List<VisionEvent> events)
        {
            if (events == null || !events.Any()) return;

            using (var conn = new MySqlConnection(_connStr))
            {
                await conn.OpenAsync();
                using (var transaction = await conn.BeginTransactionAsync())
                {
                    try
                    {
                        // JSON 데이터의 필드명과 DB 컬럼명을 1:1로 매칭
                        string sql = @"INSERT IGNORE INTO VisionEvent 
                                       (conv_id, time_kst, x, y, ms, type, image, detected_class, confidence, meta) 
                                       VALUES 
                                       (@conv_id, @time_kst, @x, @y, @ms, @type, @image, @detected_class, @confidence, @meta)";

                        foreach (var ev in events)
                        {
                            await conn.ExecuteAsync(sql, new
                            {
                                conv_id = ev.conv_id == 0 ? 1 : ev.conv_id,
                                time_kst = ev.time_kst,
                                x = ev.x,
                                y = ev.y,
                                ms = ev.ms,
                                type = ev.type ?? "TOP",
                                image = ev.image,
                                detected_class = ev.detected_class ?? "NORMAL",
                                confidence = ev.confidence == 0 ? 1.0f : ev.confidence,
                                meta = ev.meta ?? ""
                            }, transaction);
                        }
                        await transaction.CommitAsync();
                    }
                    catch (Exception ex)
                    {
                        await transaction.RollbackAsync();
                        throw new Exception($"DB 저장 중 오류: {ex.Message}");
                    }
                }
            }
        }

        public async Task<List<VisionEvent>> GetRecentVisionEventsAsync(int limit = 50)
        {
            using (var conn = new MySqlConnection(_connStr))
            {
                string sql = "SELECT * FROM VisionEvent ORDER BY time_kst DESC LIMIT @limit";
                var result = await conn.QueryAsync<VisionEvent>(sql, new { limit });
                return result.ToList();
            }
        }
    }
}