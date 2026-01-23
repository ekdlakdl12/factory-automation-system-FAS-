// Services/AuthService.cs
using factory_automation_system_FAS_.Models;
using System;
using System.IO;
using System.Security.Cryptography;

namespace factory_automation_system_FAS_.Services
{
    public sealed class AuthService
    {
        public AuthResult Validate(string userId, string password)
        {
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(password))
                return AuthResult.Failed("아이디/비밀번호가 올바르지 않습니다");

            ConfigService.EnsureUsersLocalExists();

            AuthUserRecord? u;
            try
            {
                u = LoadUserFromCsv(ConfigService.UsersLocalPath, userId);
            }
            catch
            {
                // 설정 파일이 깨져도 내부 정보 노출은 하지 않음
                return AuthResult.Failed("인증 설정을 불러올 수 없습니다");
            }

            if (u is null || !u.Enabled)
                return AuthResult.Failed("아이디/비밀번호가 올바르지 않습니다");

            // iterations는 샘플과 동일하게 100000 고정
            const int iterations = 100000;

            if (!TryVerifyPbkdf2Sha256(password, u.PasswordSaltB64, u.PasswordHashB64, iterations))
                return AuthResult.Failed("아이디/비밀번호가 올바르지 않습니다");

            AppSession.CurrentUser = new UserSession
            {
                Id = u.Id,
                DisplayName = string.IsNullOrWhiteSpace(u.DisplayName) ? u.Id : u.DisplayName,
                Role = string.IsNullOrWhiteSpace(u.Role) ? "Admin" : u.Role
            };

            return AuthResult.Success(AppSession.CurrentUser);
        }

        private static bool TryVerifyPbkdf2Sha256(string password, string saltB64, string expectedHashB64, int iterations)
        {
            try
            {
                if (iterations <= 0) iterations = 100000;

                var salt = Convert.FromBase64String(saltB64);
                var expected = Convert.FromBase64String(expectedHashB64);

                var actual = Rfc2898DeriveBytes.Pbkdf2(
                    password: password,
                    salt: salt,
                    iterations: iterations,
                    hashAlgorithm: HashAlgorithmName.SHA256,
                    outputLength: expected.Length);

                return CryptographicOperations.FixedTimeEquals(actual, expected);
            }
            catch
            {
                return false;
            }
        }

        private static AuthUserRecord? LoadUserFromCsv(string csvPath, string userId)
        {
            // CSV 포맷:
            // id,display_name,role,enabled,password_salt_b64,password_hash_b64
            // admin,Administrator,Admin,true,....,....

            var lines = File.ReadAllLines(csvPath);

            foreach (var raw in lines)
            {
                var line = raw?.Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (line.StartsWith("#")) continue;

                // 헤더 스킵
                if (line.StartsWith("id,", StringComparison.OrdinalIgnoreCase)) continue;

                var parts = line.Split(',');
                if (parts.Length < 6) continue;

                var id = parts[0].Trim();
                if (!string.Equals(id, userId, StringComparison.OrdinalIgnoreCase))
                    continue;

                var displayName = parts[1].Trim();
                var role = parts[2].Trim();
                var enabledStr = parts[3].Trim();

                var saltB64 = parts[4].Trim();
                var hashB64 = parts[5].Trim();

                var enabled =
                    string.Equals(enabledStr, "true", StringComparison.OrdinalIgnoreCase)
                    || enabledStr == "1"
                    || string.Equals(enabledStr, "y", StringComparison.OrdinalIgnoreCase);

                return new AuthUserRecord
                {
                    Id = id,
                    DisplayName = displayName,
                    Role = string.IsNullOrWhiteSpace(role) ? "Admin" : role,
                    Enabled = enabled,
                    PasswordSaltB64 = saltB64,
                    PasswordHashB64 = hashB64
                };
            }

            return null;
        }
    }

    public sealed class AuthResult
    {
        public bool Ok { get; init; }
        public string Message { get; init; } = "";
        public UserSession? User { get; init; }

        public static AuthResult Success(UserSession? user) => new() { Ok = true, User = user, Message = "" };
        public static AuthResult Failed(string message) => new() { Ok = false, Message = message };
    }
}
