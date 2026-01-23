// Services/ConfigService.cs
using System;
using System.IO;

namespace factory_automation_system_FAS_.Services
{
    public static class ConfigService
    {
        private const string UsersLocalFileName = "users.local.csv";
        private const string AppFolderName = "factory-automation-system-FAS";

        public static string AppDataDir
        {
            get
            {
                var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return Path.Combine(baseDir, AppFolderName);
            }
        }

        // %AppData%\factory-automation-system-FAS\users.local.csv
        public static string UsersLocalPath => Path.Combine(AppDataDir, UsersLocalFileName);

        /// <summary>
        /// Ensures users.local.csv exists under %AppData%\factory-automation-system-FAS\.
        /// If missing, copies it from: {AppBaseDir}\Config\users.sample.csv
        /// </summary>
        public static void EnsureUsersLocalExists()
        {
            Directory.CreateDirectory(AppDataDir);

            if (File.Exists(UsersLocalPath))
                return;

            var samplePath = Path.Combine(AppContext.BaseDirectory, "Config", "users.sample.csv");
            if (!File.Exists(samplePath))
            {
                throw new FileNotFoundException(
                    "users.sample.csv not found. Ensure it's included as Content and copied to output.",
                    samplePath);
            }

            File.Copy(samplePath, UsersLocalPath, overwrite: false);
        }
    }
}
