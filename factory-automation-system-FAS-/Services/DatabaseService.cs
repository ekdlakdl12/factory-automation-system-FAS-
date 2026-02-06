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
            // appsettings.json에서 연결 문자열 로드
            var config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            _connStr = config.GetSection("ConnectionStrings")["MariaDbConnection"] ?? "";
        }

        /// <summary>
        /// DB 연결 상태를 확인합니다. (현재 발생한 빌드 에러 해결 포인트)
        /// </summary>
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

        /// <summary>
        /// 비전 검사 이벤트 리스트를 DB에 저장합니다.
        /// </summary>
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
                        // SQL문에 barcode 및 color 포함
                        string sql = @"INSERT IGNORE INTO VisionEvent 
                                       (conv_id, barcode, time_kst, x, y, ms, type, image, color, detected_class, confidence) 
                                       VALUES 
                                       (@conv_id, @barcode, @time_kst, @x, @y, @ms, @type, @image, @color, @detected_class, @confidence)";

                        foreach (var ev in events)
                        {
                            await conn.ExecuteAsync(sql, new
                            {
                                conv_id = ev.conv_id,
                                barcode = ev.barcode ?? "NO_BARCODE",
                                time_kst = ev.time_kst,
                                x = ev.x,
                                y = ev.y,
                                ms = ev.ms,
                                type = ev.type ?? "TOP",
                                image = ev.image,
                                color = ev.color ?? "NONE",
                                detected_class = ev.detected_class ?? "NORMAL",
                                confidence = ev.confidence == 0 ? 1.0f : ev.confidence
                            }, transaction);
                        }
                        await transaction.CommitAsync();
                    }
                    catch (Exception ex)
                    {
                        await transaction.RollbackAsync();
                        throw new Exception($"[DB Error] 저장 중 오류 발생: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// 최근 검사 이력을 조회합니다.
        /// </summary>
        public async Task<List<VisionEvent>> GetRecentVisionEventsAsync(int limit = 50)
        {
            try
            {
                using (var conn = new MySqlConnection(_connStr))
                {
                    string sql = "SELECT * FROM VisionEvent ORDER BY time_kst DESC LIMIT @limit";
                    var result = await conn.QueryAsync<VisionEvent>(sql, new { limit });
                    return result.ToList();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DB 조회 에러] {ex.Message}");
                return new List<VisionEvent>();
            }
        }
    }
}