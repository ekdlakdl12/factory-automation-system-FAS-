// Services/ConfigService.cs
using System;
using System.IO;

namespace factory_automation_system_FAS_.Services
{
    public static class ConfigService
    {
        private const string AppFolderName = "factory-automation-system-FAS";
        private const string AuthLocalFileName = "auth.local.json";

        public static string AppDataDir
        {
            get
            {
                var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return Path.Combine(baseDir, AppFolderName);
            }
        }

        public static string AuthLocalPath => Path.Combine(AppDataDir, AuthLocalFileName);

        /// <summary>
        /// Ensures auth.local.json exists under %AppData%\\factory-automation-system-FAS\\.
        /// If missing, copies it from the packaged sample: {AppBaseDir}\\Config\\auth.sample.json
        /// </summary>
        public static void EnsureAuthLocalExists()
        {
            Directory.CreateDirectory(AppDataDir);

            if (File.Exists(AuthLocalPath))
                return;

            var samplePath = Path.Combine(AppContext.BaseDirectory, "Config", "auth.sample.json");
            if (!File.Exists(samplePath))
            {
                throw new FileNotFoundException(
                    "auth.sample.json not found. Ensure it's included as Content and copied to output.",
                    samplePath);
            }

            File.Copy(samplePath, AuthLocalPath, overwrite: false);
        }
    }
}
