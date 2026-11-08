using System;
using System.IO;
using Fujify.Services;
using Xunit;

namespace Fujify.Tests
{
    public class SettingsPersistenceTests
    {
        [Fact]
        public void SettingsService_Save_WritesSettingsAtomicallyAndCreatesBackup()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), $"settings_test_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            string settingsFile = Path.Combine(tempDir, "settings.json");
            string bakFile = Path.Combine(tempDir, "settings.json.bak");

            try
            {
                var service = new SettingsService(settingsFile);
                service.Settings.Language = "fr";
                service.Settings.ThemeMode = "Amoled";
                service.Settings.AppendSuffix = true;
                service.Settings.Suffix = "-testfuji";
                service.Settings.CustomOutputFolder = "C:\\MyExports";
                service.Save();

                Assert.True(File.Exists(settingsFile));

                // Change a setting and save again -> should create .bak of the previous state
                service.Settings.Language = "es";
                service.Save();

                Assert.True(File.Exists(settingsFile));
                Assert.True(File.Exists(bakFile));

                // Reload from disk
                var reloaded = new SettingsService(settingsFile);
                Assert.Equal("es", reloaded.Settings.Language);
                Assert.Equal("Amoled", reloaded.Settings.ThemeMode);
                Assert.True(reloaded.Settings.AppendSuffix);
                Assert.Equal("-testfuji", reloaded.Settings.Suffix);
                Assert.Equal("C:\\MyExports", reloaded.Settings.CustomOutputFolder);
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void SettingsService_Load_RecoversFromBak_WhenPrimaryMissing()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), $"settings_test_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            string settingsFile = Path.Combine(tempDir, "settings.json");
            string bakFile = Path.Combine(tempDir, "settings.json.bak");

            try
            {
                // Write a valid backup file
                string validJson = @"{
                    ""Language"": ""ja"",
                    ""ThemeMode"": ""Light"",
                    ""LicenseKey"": ""FUJI-RECOVER-1234"",
                    ""CustomOutputFolder"": ""D:\\Recovered""
                }";
                File.WriteAllText(bakFile, validJson);

                // Initialize service pointing to primary file (which does not exist)
                var service = new SettingsService(settingsFile);

                Assert.Equal("ja", service.Settings.Language);
                Assert.Equal("Light", service.Settings.ThemeMode);
                Assert.Equal("FUJI-RECOVER-1234", service.Settings.LicenseKey);
                Assert.Equal("D:\\Recovered", service.Settings.CustomOutputFolder);

                // Service should have automatically restored primary settings.json
                Assert.True(File.Exists(settingsFile));
                string primaryContent = File.ReadAllText(settingsFile);
                Assert.Contains("FUJI-RECOVER-1234", primaryContent);
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void SettingsService_Load_RecoversFromBak_WhenPrimaryZeroBytes()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), $"settings_test_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            string settingsFile = Path.Combine(tempDir, "settings.json");
            string bakFile = Path.Combine(tempDir, "settings.json.bak");

            try
            {
                // Write 0-byte corrupt primary file (e.g. power lost during update or interrupted write)
                File.WriteAllText(settingsFile, "");

                // Write a valid backup file
                string validJson = @"{
                    ""Language"": ""de"",
                    ""ThemeMode"": ""Dark"",
                    ""LicenseKey"": ""FUJI-FROM-BAK-5678""
                }";
                File.WriteAllText(bakFile, validJson);

                var service = new SettingsService(settingsFile);

                Assert.Equal("de", service.Settings.Language);
                Assert.Equal("Dark", service.Settings.ThemeMode);
                Assert.Equal("FUJI-FROM-BAK-5678", service.Settings.LicenseKey);

                // Primary file should now be restored
                var info = new FileInfo(settingsFile);
                Assert.True(info.Length > 0);
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void SettingsService_Load_RecoversFromBak_WhenPrimaryCorrupt()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), $"settings_test_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            string settingsFile = Path.Combine(tempDir, "settings.json");
            string bakFile = Path.Combine(tempDir, "settings.json.bak");

            try
            {
                // Corrupt unparseable JSON in primary file
                File.WriteAllText(settingsFile, "{ this is not valid json at all !!!");

                // Valid backup file
                string validJson = @"{
                    ""Language"": ""it"",
                    ""LosslessCompression"": true,
                    ""LicenseKey"": ""FUJI-CORRUPT-RECOVER""
                }";
                File.WriteAllText(bakFile, validJson);

                var service = new SettingsService(settingsFile);

                Assert.Equal("it", service.Settings.Language);
                Assert.True(service.Settings.LosslessCompression);
                Assert.Equal("FUJI-CORRUPT-RECOVER", service.Settings.LicenseKey);
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void SettingsService_Load_RecoversFromTmp_WhenAppKilledDuringSave()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), $"settings_test_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            string settingsFile = Path.Combine(tempDir, "settings.json");
            string tmpFile = Path.Combine(tempDir, "settings.json.tmp");

            try
            {
                // Primary doesn't exist, but .tmp was written right before app shutdown
                string validJson = @"{
                    ""Language"": ""ru"",
                    ""ThemeMode"": ""Automatic"",
                    ""CustomOutputFolder"": ""E:\\Photos""
                }";
                File.WriteAllText(tmpFile, validJson);

                var service = new SettingsService(settingsFile);

                Assert.Equal("ru", service.Settings.Language);
                Assert.Equal("Automatic", service.Settings.ThemeMode);
                Assert.Equal("E:\\Photos", service.Settings.CustomOutputFolder);

                // Primary should now be committed
                Assert.True(File.Exists(settingsFile));
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void SettingsService_Load_PermissiveOptions_HandlesCommentsAndTrailingCommas()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), $"settings_test_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            string settingsFile = Path.Combine(tempDir, "settings.json");

            try
            {
                // JSON with C-style comments and trailing comma
                string jsonWithComments = @"
                {
                    // User preference comments
                    ""language"": ""zh"",
                    ""themeMode"": ""Amoled"",
                    ""backupOriginalFile"": false,
                }";
                File.WriteAllText(settingsFile, jsonWithComments);

                var service = new SettingsService(settingsFile);

                Assert.Equal("zh", service.Settings.Language);
                Assert.Equal("Amoled", service.Settings.ThemeMode);
                Assert.False(service.Settings.BackupOriginalFile);
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }
    }
}

