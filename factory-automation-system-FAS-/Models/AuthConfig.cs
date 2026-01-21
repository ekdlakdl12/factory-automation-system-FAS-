// Models/AuthConfig.cs
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace factory_automation_system_FAS_.Models
{
    public sealed class AuthConfig
    {
        [JsonPropertyName("version")]
        public int Version { get; set; } = 1;

        [JsonPropertyName("hash_alg")]
        public string HashAlg { get; set; } = "PBKDF2-SHA256";

        [JsonPropertyName("iterations")]
        public int Iterations { get; set; } = 100000;

        [JsonPropertyName("users")]
        public List<AuthUserRecord> Users { get; set; } = new();
    }

    public sealed class AuthUserRecord
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("display_name")]
        public string DisplayName { get; set; } = "";

        [JsonPropertyName("role")]
        public string Role { get; set; } = "Admin";

        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;

        [JsonPropertyName("password_salt_b64")]
        public string PasswordSaltB64 { get; set; } = "";

        [JsonPropertyName("password_hash_b64")]
        public string PasswordHashB64 { get; set; } = "";
    }
}
