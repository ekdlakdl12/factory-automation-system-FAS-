using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Configuration;
using MySql.Data.MySqlClient;
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

        // 팩트체크: MainViewModel에서 빨간 줄이 뜨지 않도록 이 함수가 반드시 있어야 합니다.
        public bool CheckConnection()
        {
            using (var conn = new MySqlConnection(_connStr))
            {
                try
                {
                    conn.Open();
                    return true;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"DB 연결 에러: {ex.Message}");
                    return false;
                }
            }
        }

        // 기존에 만든 생산 이력 조회 함수
        public List<ProductionHistory> GetProductionHistory()
        {
            var list = new List<ProductionHistory>();
            using (var conn = new MySqlConnection(_connStr))
            {
                try
                {
                    conn.Open();
                    string sql = "SELECT barcode, total_qty, good_qty, defect_rate, created_at FROM production_history ORDER BY created_at DESC";
                    using (var cmd = new MySqlCommand(sql, conn))
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            list.Add(new ProductionHistory
                            {
                                Barcode = reader["barcode"].ToString(),
                                TotalQuantity = Convert.ToInt32(reader["total_qty"]),
                                GoodQuantity = Convert.ToInt32(reader["good_qty"]),
                                DefectRate = Convert.ToSingle(reader["defect_rate"]),
                                CreatedAt = Convert.ToDateTime(reader["created_at"])
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"데이터 로드 실패: {ex.Message}");
                }
            }
            return list;
        }
    }
}