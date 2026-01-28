// Services/UserAdminService.cs
using factory_automation_system_FAS_.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace factory_automation_system_FAS_.Services
{
    /// <summary>
    /// users.local.csv 관리용 서비스
    /// CSV 포맷:
    /// id,display_name,role,enabled,password_salt_b64,password_hash_b64
    /// </summary>
    public sealed class UserAdminService
    {
        private const int Iterations = 100000;
        private const int SaltBytes = 16;
        private const int HashBytes = 32;

        public List<AuthUserRecord> LoadUsers()
        {
            ConfigService.EnsureUsersLocalExists();

            var path = ConfigService.UsersLocalPath;
            var lines = File.ReadAllLines(path);

            var users = new List<AuthUserRecord>();

            foreach (var raw in lines)
            {
                var line = (raw ?? "").Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (line.StartsWith("#")) continue;

                if (line.StartsWith("id,", StringComparison.OrdinalIgnoreCase)) continue;

                var parts = line.Split(',');
                if (parts.Length < 6) continue;

                var id = parts[0].Trim();
                var displayName = parts[1].Trim();
                var role = parts[2].Trim();
                var enabledStr = parts[3].Trim();
                var saltB64 = parts[4].Trim();
                var hashB64 = parts[5].Trim();

                var enabled =
                    string.Equals(enabledStr, "true", StringComparison.OrdinalIgnoreCase)
                    || enabledStr == "1"
                    || string.Equals(enabledStr, "y", StringComparison.OrdinalIgnoreCase);

                if (string.IsNullOrWhiteSpace(id)) continue;

                users.Add(new AuthUserRecord
                {
                    Id = id,
                    DisplayName = displayName,
                    Role = string.IsNullOrWhiteSpace(role) ? "Admin" : role,
                    Enabled = enabled,
                    PasswordSaltB64 = saltB64,
                    PasswordHashB64 = hashB64
                });
            }

            return users;
        }

        public void SaveUsers(List<AuthUserRecord> users)
        {
            ConfigService.EnsureUsersLocalExists();

            var path = ConfigService.UsersLocalPath;

            // 간단한 유효성 검사
            var duplicates = users
                .GroupBy(u => (u.Id ?? "").Trim().ToLowerInvariant())
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            if (duplicates.Count > 0)
                throw new InvalidOperationException("중복된 아이디가 있습니다: " + string.Join(", ", duplicates));

            var lines = new List<string>
            {
                "id,display_name,role,enabled,password_salt_b64,password_hash_b64"
            };

            foreach (var u in users)
            {
                var id = (u.Id ?? "").Trim();
                if (string.IsNullOrWhiteSpace(id)) continue;

                var display = (u.DisplayName ?? "").Trim();
                var role = string.IsNullOrWhiteSpace(u.Role) ? "Admin" : u.Role.Trim();
                var enabled = u.Enabled ? "true" : "false";

                if (string.IsNullOrWhiteSpace(u.PasswordSaltB64) || string.IsNullOrWhiteSpace(u.PasswordHashB64))
                    throw new InvalidOperationException($"'{id}' 사용자의 비밀번호 해시 정보가 비어있습니다.");

                // CSV 단순 저장 (필드에 ','가 들어가지 않는다는 가정)
                lines.Add($"{id},{display},{role},{enabled},{u.PasswordSaltB64},{u.PasswordHashB64}");
            }

            // 백업 한 번 남기기
            var backup = path + ".bak";
            try { File.Copy(path, backup, overwrite: true); } catch { /* ignore */ }

            File.WriteAllLines(path, lines);
        }

        public AuthUserRecord CreateUser(string id, string displayName, string role, bool enabled, string password)
        {
            var users = LoadUsers();

            var cleanId = (id ?? "").Trim();
            if (string.IsNullOrWhiteSpace(cleanId))
                throw new ArgumentException("아이디가 비어있습니다.");

            if (users.Any(x => string.Equals(x.Id, cleanId, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("이미 존재하는 아이디입니다.");

            if ((password ?? "").Length < 4)
                throw new ArgumentException("비밀번호는 4자 이상 권장입니다.");

            var (saltB64, hashB64) = CreatePbkdf2Sha256(password);

            var u = new AuthUserRecord
            {
                Id = cleanId,
                DisplayName = (displayName ?? "").Trim(),
                Role = string.IsNullOrWhiteSpace(role) ? "Admin" : role.Trim(),
                Enabled = enabled,
                PasswordSaltB64 = saltB64,
                PasswordHashB64 = hashB64
            };

            users.Add(u);
            SaveUsers(users);

            return u;
        }

        public void ResetPassword(AuthUserRecord user, string newPassword)
        {
            if (user is null) throw new ArgumentNullException(nameof(user));
            if ((newPassword ?? "").Length < 4) throw new ArgumentException("비밀번호는 4자 이상 권장입니다.");

            var (saltB64, hashB64) = CreatePbkdf2Sha256(newPassword);
            user.PasswordSaltB64 = saltB64;
            user.PasswordHashB64 = hashB64;
        }

        private static (string SaltB64, string HashB64) CreatePbkdf2Sha256(string password)
        {
            var salt = RandomNumberGenerator.GetBytes(SaltBytes);

            var hash = Rfc2898DeriveBytes.Pbkdf2(
                password: password,
                salt: salt,
                iterations: Iterations,
                hashAlgorithm: HashAlgorithmName.SHA256,
                outputLength: HashBytes);

            return (Convert.ToBase64String(salt), Convert.ToBase64String(hash));
        }
    }
}
