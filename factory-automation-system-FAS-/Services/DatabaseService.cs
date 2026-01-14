using MySql.Data.MySqlClient;
using System;
using System.Data;

namespace factory_automation_system_FAS_.Services
{
    public class DatabaseService
    {
        // 팩트체크: 라즈베리파이 외부 포트 33060 적용 //시연때는 그냥 연결하는걸로 바꿀수도잇음
        private readonly string _connStr = "Server=raymondspreatics.iptime.org;Port=33060;Database=factory_db;Uid=root;Pwd=비밀번호;";

        // 연결 테스트 함수
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

        // 가짜 데이터 입력 테스트 (PLC 연동 전 테스트용)
        public void InsertDummyData(string barcode, int total)
        {
            using (var conn = new MySqlConnection(_connStr))
            {
                string sql = "INSERT INTO production_history (barcode, total_quantity) VALUES (@barcode, @total)";
                MySqlCommand cmd = new MySqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@barcode", barcode);
                cmd.Parameters.AddWithValue("@total", total);

                conn.Open();
                cmd.ExecuteNonQuery();
            }
        }
    }
}