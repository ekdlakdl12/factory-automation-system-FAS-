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
                .AddJsonFile("appsettings.json")
                .Build();

            _connStr = config.GetSection("ConnectionStrings")["MariaDbConnection"];
        }

        // 생산 이력을 DB에서 읽어오는 핵심 함수
        public List<ProductionHistory> GetProductionHistory()
        {
            var list = new List<ProductionHistory>();

            using (var conn = new MySqlConnection(_connStr))
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
            return list;
        }
    }
}