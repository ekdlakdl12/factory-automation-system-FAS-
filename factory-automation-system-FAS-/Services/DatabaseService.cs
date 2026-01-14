using Microsoft.Extensions.Configuration; // 패키지 설치 후 활성화
using MySql.Data.MySqlClient;
using System;
using System.Configuration;
using System.IO;

namespace factory_automation_system_FAS_.Services
{
    public class DatabaseService
    {
        private readonly string _connStr;

        public DatabaseService()
        {
            try
            {
                // 1. appsettings.json 파일을 읽기 위한 설정
                var builder = new ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

                IConfiguration config = builder.Build();

                // 2. ConnectionStrings 섹션에서 MariaDbConnection 값을 가져옴
                _connStr = config.GetSection("ConnectionStrings")["MariaDbConnection"];

                if (string.IsNullOrEmpty(_connStr))
                {
                    throw new Exception("연결 문자열을 찾을 수 없습니다.");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"설정 로드 실패: {ex.Message}");
                // 예외 발생 시 기본값 혹은 에러 처리 로직
            }
        }

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
    }
}