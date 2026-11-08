using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fujify.Models;
using Fujify.Services;
using Xunit;

namespace Fujify.Tests
{
    public class LightroomPresetServiceTests
    {
        private class MockLicenseService : ILicenseService
        {
            public bool IsPro { get; set; } = false;
            public LicenseStatus CurrentStatus => new() { Tier = IsPro ? LicenseTier.Pro : LicenseTier.Free };
            public int MaxFreeQueueSize => 10;
            public int DelaySeconds => 15;
            public string? AccountEmail => null;
            public bool IsAccountLoggedIn => false;
            public string? AccountLicenseKey => null;
            public bool HasAccountLicense => false;
            public string AccountDashboardUrl => "https://fujifier.cc/account";
            public int UsedActivations => 0;
            public int MaxActivations => 3;
            public string SlotInfoText => "0 of 3 devices";
            public IReadOnlyList<Fujify.Models.LicenseDeviceDto> RegisteredDevices => Array.Empty<Fujify.Models.LicenseDeviceDto>();
            public string CurrentDeviceId => "mock-device-id";

#pragma warning disable CS0067
            public event EventHandler<LicenseStatus>? LicenseChanged;
            public event EventHandler? DevicesChanged;
#pragma warning restore CS0067

            public void Initialize() { }
            public void SyncFromSettings() { }
            public Task<LicenseActivationResult> ActivateLicenseKeyAsync(string licenseKey) => Task.FromResult(new LicenseActivationResult(true));
            public Task<LicenseActivationResult> ActivateCurrentDeviceAsync() => Task.FromResult(new LicenseActivationResult(true));
            public Task<LicenseActivationResult> LoginWithAccountAsync(string email, string password) => Task.FromResult(new LicenseActivationResult(true));
            public Task<(bool Success, string Message)> RequestPasswordResetAsync(string email) => Task.FromResult((true, "Reset link sent"));
            public Task<LicenseActivationResult> LoginWithGoogleOAuthAsync(Action<string>? statusCallback = null, CancellationToken cancellationToken = default) => Task.FromResult(new LicenseActivationResult(true));
            public Task<LicenseActivationResult> LoginWithGoogleIdTokenAsync(string googleIdToken) => Task.FromResult(new LicenseActivationResult(true));
            public Task<LicenseActivationResult> RetrieveLicenseFromAccountAsync() => Task.FromResult(new LicenseActivationResult(true));
            public Task<LicenseActivationResult> CompleteOAuthCallbackAsync(string code, string? codeVerifier = null, string? state = null) => Task.FromResult(new LicenseActivationResult(true));
            public Task<bool> DeactivateLicenseAsync() => Task.FromResult(true);
            public Task<bool> DeactivateRemoteDeviceAsync(string deviceId) => Task.FromResult(true);
            public Task<IReadOnlyList<Fujify.Models.LicenseDeviceDto>> FetchRegisteredDevicesAsync() => Task.FromResult<IReadOnlyList<Fujify.Models.LicenseDeviceDto>>(Array.Empty<Fujify.Models.LicenseDeviceDto>());
            public Task<bool> LogoutAccountAsync() => Task.FromResult(true);
            public Task<LicenseActivationResult> VerifyCurrentLicenseAsync(bool forceNetwork = false) => Task.FromResult(new LicenseActivationResult(true));
            public bool VerifyLicenseKey(string licenseKey, out string? email, out string? error) { email = null; error = null; return true; }
        }

        [Fact]
        public void LightroomPresetService_Has20Presets()
        {
            var license = new MockLicenseService();
            var service = new LightroomPresetService(license);

            Assert.Equal(20, service.Presets.Count);
        }

        [Fact]
        public void LightroomPresetService_PresetsSetOnlyCameraProfileAndNoDevelopSliders()
        {
            var license = new MockLicenseService();
            var service = new LightroomPresetService(license);

            string[] forbiddenSliders = new[]
            {
                "crs:Exposure2012",
                "crs:Contrast2012",
                "crs:Highlights2012",
                "crs:Shadows2012",
                "crs:Whites2012",
                "crs:Blacks2012",
                "crs:Clarity2012",
                "crs:Dehaze",
                "crs:Texture",
                "crs:Vibrance",
                "crs:Saturation",
                "crs:ToneCurvePV2012",
                "crs:Temperature",
                "crs:Tint"
            };

            foreach (var preset in service.Presets)
            {
                string xml = service.GeneratePresetXml(preset);

                // 1. Must contain camera profile or look
                Assert.Contains($"crs:CameraProfile=\"{preset.CameraProfile}\"", xml);
                Assert.Contains("<crs:Group>", xml);
                Assert.Contains("Fujifilm Film Simulations (Fujifier)", xml);

                // 2. Must contain requirement notice
                Assert.Contains("Photos must be converted with Fujifier beforehand", xml);

                // 3. Must NOT contain any develop slider adjustments
                foreach (var slider in forbiddenSliders)
                {
                    Assert.DoesNotContain(slider, xml);
                }
            }
        }

        [Fact]
        public void LightroomPresetService_FreeTier_BlockedFromInstalling()
        {
            var license = new MockLicenseService { IsPro = false };
            var service = new LightroomPresetService(license);

            var result = service.InstallPresetsToLightroom();

            Assert.False(result.Success);
            Assert.True(result.RequiresPro);
            Assert.Equal(0, result.Count);
        }

        [Fact]
        public void LightroomPresetService_FreeTier_BlockedFromExporting()
        {
            var license = new MockLicenseService { IsPro = false };
            var service = new LightroomPresetService(license);

            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            var result = service.ExportPresetsToDirectory(tempDir);

            Assert.False(result.Success);
            Assert.True(result.RequiresPro);
        }

        [Fact]
        public void LightroomPresetService_ProTier_CanExportToDirectory()
        {
            var license = new MockLicenseService { IsPro = true };
            var service = new LightroomPresetService(license);

            var tempDir = Path.Combine(Path.GetTempPath(), "Fujifier_Test_" + Guid.NewGuid().ToString("N"));
            try
            {
                var result = service.ExportPresetsToDirectory(tempDir);

                Assert.True(result.Success);
                Assert.Equal(20, result.Count);
                Assert.True(Directory.Exists(tempDir));

                var files = Directory.GetFiles(tempDir, "*.xmp");
                Assert.Equal(20, files.Length);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        [Fact]
        public void LightroomPresetService_GeneratePresetsZip_CreatesValidZipWithReadme()
        {
            var license = new MockLicenseService { IsPro = true };
            var service = new LightroomPresetService(license);

            byte[] zipBytes = service.GeneratePresetsZip();
            Assert.NotNull(zipBytes);
            Assert.True(zipBytes.Length > 0);

            using var ms = new MemoryStream(zipBytes);
            using var archive = new ZipArchive(ms, ZipArchiveMode.Read);

            // Check that 20 xmp files and 1 README.txt exist
            var xmpEntries = archive.Entries.Where(e => e.FullName.EndsWith(".xmp", StringComparison.OrdinalIgnoreCase)).ToList();
            Assert.Equal(20, xmpEntries.Count);

            var readme = archive.Entries.FirstOrDefault(e => e.Name.Equals("README.txt", StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(readme);

            using var reader = new StreamReader(readme.Open());
            string content = reader.ReadToEnd();
            Assert.Contains("converted to DNG using Fujifier beforehand", content);
        }

        [Fact]
        public void LightroomPresetService_ProfileNames_MatchExactAdobeLightroomNaming()
        {
            var license = new MockLicenseService();
            var service = new LightroomPresetService(license);

            // Pro Neg variations must use Title Case 'Pro' (not all-caps 'PRO')
            var proNegHi = service.Presets.First(p => p.Id == "pro_neg_hi");
            Assert.Equal("Camera Pro Neg. Hi", proNegHi.CameraProfile);
            Assert.Equal("Fujifilm Pro Neg. Hi.xmp", proNegHi.FileName);

            var proNegStd = service.Presets.First(p => p.Id == "pro_neg_std");
            Assert.Equal("Camera Pro Neg. Std", proNegStd.CameraProfile);
            Assert.Equal("Fujifilm Pro Neg. Std.xmp", proNegStd.FileName);

            // ACROS filtered variations must include "Filter" suffix
            var acrosYe = service.Presets.First(p => p.Id == "acros_ye");
            Assert.Equal("Camera ACROS+Ye Filter", acrosYe.CameraProfile);

            var acrosR = service.Presets.First(p => p.Id == "acros_r");
            Assert.Equal("Camera ACROS+R Filter", acrosR.CameraProfile);

            var acrosG = service.Presets.First(p => p.Id == "acros_g");
            Assert.Equal("Camera ACROS+G Filter", acrosG.CameraProfile);

            // Standard ACROS without filter has no "Filter" suffix
            var acros = service.Presets.First(p => p.Id == "acros");
            Assert.Equal("Camera ACROS", acros.CameraProfile);

            // MONOCHROME filtered variations must include "Filter" suffix
            var monoYe = service.Presets.First(p => p.Id == "monochrome_ye");
            Assert.Equal("Camera MONOCHROME+Ye Filter", monoYe.CameraProfile);

            var monoR = service.Presets.First(p => p.Id == "monochrome_r");
            Assert.Equal("Camera MONOCHROME+R Filter", monoR.CameraProfile);

            var monoG = service.Presets.First(p => p.Id == "monochrome_g");
            Assert.Equal("Camera MONOCHROME+G Filter", monoG.CameraProfile);

            // Standard MONOCHROME without filter has no "Filter" suffix
            var mono = service.Presets.First(p => p.Id == "monochrome");
            Assert.Equal("Camera MONOCHROME", mono.CameraProfile);
        }
    }
}

