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

            // 팩트체크: appsettings.json의 키 이름이 "MariaDbConnection"인지 확인하세요.
            _connStr = config.GetSection("ConnectionStrings")["MariaDbConnection"];
        }

        // DB 연결 상태 확인 함수
        public bool CheckConnection()
        {
            if (string.IsNullOrEmpty(_connStr)) return false;

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

        // 생산 이력 조회 함수
        public List<ProductionHistory> GetProductionHistory()
        {
            var list = new List<ProductionHistory>();
            using (var conn = new MySqlConnection(_connStr))
            {
                try
                {
                    conn.Open();
                    // 팩트체크: 실제 DB 컬럼명인 total_quantity, good_quantity로 쿼리를 수정했습니다.
                    string sql = "SELECT barcode, total_quantity, good_quantity, defect_rate, created_at FROM production_history ORDER BY created_at DESC";

                    using (var cmd = new MySqlCommand(sql, conn))
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            // 팩트체크: ProductionHistory 모델의 소문자 속성명에 맞춰 데이터를 매핑합니다.
                            list.Add(new ProductionHistory
                            {
                                barcode = reader["barcode"].ToString(),
                                total_quantity = Convert.ToInt32(reader["total_quantity"]),
                                good_quantity = Convert.ToInt32(reader["good_quantity"]),
                                defect_rate = Convert.ToSingle(reader["defect_rate"]),
                                created_at = Convert.ToDateTime(reader["created_at"])
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