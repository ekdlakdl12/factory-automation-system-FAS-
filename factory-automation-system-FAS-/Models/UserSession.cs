// Models/UserSession.cs
namespace factory_automation_system_FAS_.Models
{
    public sealed class UserSession
    {
        public string Id { get; init; } = "";
        public string DisplayName { get; init; } = "";
        public string Role { get; init; } = "Admin";
    }
}
