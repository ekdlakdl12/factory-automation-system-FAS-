// Services/AuthService.cs
using factory_automation_system_FAS_.Models;
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace factory_automation_system_FAS_.Services
{
    public sealed class AuthService
    {
        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        public AuthResult Validate(string userId, string password)
        {
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(password))
                return AuthResult.Failed("아이디/비밀번호가 올바르지 않습니다");

            ConfigService.EnsureAuthLocalExists();

            AuthConfig cfg;
            try
            {
                var json = File.ReadAllText(ConfigService.AuthLocalPath);
                cfg = JsonSerializer.Deserialize<AuthConfig>(json, JsonOpts)
                      ?? throw new InvalidOperationException("auth.local.json parse failed.");
            }
            catch
            {
                // 설정 파일이 깨져도 내부 정보 노출은 하지 않음
                return AuthResult.Failed("인증 설정을 불러올 수 없습니다");
            }

            var u = cfg.Users.FirstOrDefault(x =>
                x.Enabled &&
                string.Equals(x.Id, userId, StringComparison.OrdinalIgnoreCase));

            if (u is null)
                return AuthResult.Failed("아이디/비밀번호가 올바르지 않습니다");

            if (!string.Equals(cfg.HashAlg, "PBKDF2-SHA256", StringComparison.OrdinalIgnoreCase))
                return AuthResult.Failed("인증 설정을 불러올 수 없습니다");

            if (!TryVerifyPbkdf2Sha256(password, u.PasswordSaltB64, u.PasswordHashB64, cfg.Iterations))
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
