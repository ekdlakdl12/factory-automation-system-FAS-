// Services/AppSession.cs
using factory_automation_system_FAS_.Models;

namespace factory_automation_system_FAS_.Services
{
    public static class AppSession
    {
        public static UserSession? CurrentUser { get; internal set; }
        public static bool IsAuthenticated => CurrentUser != null;

        public static void Logout() => CurrentUser = null;
    }
}
