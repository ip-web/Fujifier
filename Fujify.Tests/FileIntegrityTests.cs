/*
 * This file is part of Fujify.
 *
 * Copyright (C) 2024 Isidore Paulin contact@ipweb.dev
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 */

using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Fujify.Common;
using Fujify.Services;
using Fujify.ViewModels;
using Xunit;

namespace Fujify.Tests
{
    public class FileIntegrityTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly FileIntegrityService _service;

        public FileIntegrityTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"integrity_test_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
            _service = new FileIntegrityService();
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_tempDir))
                {
                    Directory.Delete(_tempDir, true);
                }
            }
            catch { }
        }

        [Fact]
        public void VerifyIntegrity_NullOrEmptyPath_ReturnsFailure()
        {
            var res1 = _service.VerifyIntegrity("");
            Assert.False(res1.IsValid);
            Assert.Contains("null or empty", res1.ErrorMessage, StringComparison.OrdinalIgnoreCase);

            var res2 = _service.VerifyIntegrity("   ");
            Assert.False(res2.IsValid);
        }

        [Fact]
        public void VerifyIntegrity_NonExistentFile_ReturnsFailure()
        {
            string nonExistent = Path.Combine(_tempDir, "does_not_exist.dng");
            var res = _service.VerifyIntegrity(nonExistent);
            Assert.False(res.IsValid);
            Assert.Contains("does not exist", res.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void VerifyIntegrity_ZeroByteFile_ReturnsFailure()
        {
            string emptyFile = Path.Combine(_tempDir, "empty.dng");
            File.WriteAllBytes(emptyFile, Array.Empty<byte>());

            var res = _service.VerifyIntegrity(emptyFile);
            Assert.False(res.IsValid);
            Assert.Contains("0 bytes", res.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void VerifyIntegrity_Dng_TooSmall_ReturnsFailure()
        {
            string smallDng = Path.Combine(_tempDir, "small.dng");
            File.WriteAllBytes(smallDng, new byte[500]); // Less than 1024 bytes

            var res = _service.VerifyIntegrity(smallDng);
            Assert.False(res.IsValid);
            Assert.Contains("too small", res.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void VerifyIntegrity_Dng_InvalidByteOrder_ReturnsFailure()
        {
            string badDng = Path.Combine(_tempDir, "bad_order.dng");
            byte[] data = new byte[2048];
            data[0] = 0xAA;
            data[1] = 0xBB;
            File.WriteAllBytes(badDng, data);

            var res = _service.VerifyIntegrity(badDng);
            Assert.False(res.IsValid);
            Assert.Contains("byte order", res.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void VerifyIntegrity_Dng_InvalidMagic_ReturnsFailure()
        {
            string badDng = Path.Combine(_tempDir, "bad_magic.dng");
            byte[] data = new byte[2048];
            data[0] = 0x49; // 'I'
            data[1] = 0x49; // 'I'
            data[2] = 43;   // Not 42
            data[3] = 0;
            File.WriteAllBytes(badDng, data);

            var res = _service.VerifyIntegrity(badDng);
            Assert.False(res.IsValid);
            Assert.Contains("magic", res.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void VerifyIntegrity_Dng_OutOfBoundsIfdOffset_ReturnsFailure()
        {
            string badDng = Path.Combine(_tempDir, "bad_offset.dng");
            byte[] data = new byte[2048];
            data[0] = 0x49;
            data[1] = 0x49;
            data[2] = 42;
            data[3] = 0;
            // IFD offset = 5000 (past 2048)
            BitConverter.GetBytes((uint)5000).CopyTo(data, 4);
            File.WriteAllBytes(badDng, data);

            var res = _service.VerifyIntegrity(badDng);
            Assert.False(res.IsValid);
            Assert.Contains("out of file bounds", res.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void VerifyIntegrity_Dng_ZeroEntryCount_ReturnsFailure()
        {
            string badDng = Path.Combine(_tempDir, "zero_entries.dng");
            byte[] data = new byte[2048];
            data[0] = 0x49;
            data[1] = 0x49;
            data[2] = 42;
            data[3] = 0;
            BitConverter.GetBytes((uint)8).CopyTo(data, 4); // IFD offset = 8
            BitConverter.GetBytes((ushort)0).CopyTo(data, 8); // Entry count = 0
            File.WriteAllBytes(badDng, data);

            var res = _service.VerifyIntegrity(badDng);
            Assert.False(res.IsValid);
            Assert.Contains("entry count", res.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void VerifyIntegrity_Dng_MissingRequiredTags_ReturnsFailure()
        {
            string badDng = Path.Combine(_tempDir, "no_tags.dng");
            byte[] data = new byte[2048];
            data[0] = 0x49;
            data[1] = 0x49;
            data[2] = 42;
            data[3] = 0;
            BitConverter.GetBytes((uint)8).CopyTo(data, 4); // IFD0 offset = 8
            BitConverter.GetBytes((ushort)1).CopyTo(data, 8); // 1 entry
            // Entry 0: tag = 0x00FF (not DNG or camera tag)
            BitConverter.GetBytes((ushort)0x00FF).CopyTo(data, 10);
            File.WriteAllBytes(badDng, data);

            var res = _service.VerifyIntegrity(badDng);
            Assert.False(res.IsValid);
            Assert.Contains("metadata tags", res.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void VerifyIntegrity_Dng_ValidSyntheticDng_ReturnsSuccess()
        {
            string validDng = Path.Combine(_tempDir, "valid.dng");
            byte[] data = CreateSyntheticValidDng();
            File.WriteAllBytes(validDng, data);

            var res = _service.VerifyIntegrity(validDng);
            Assert.True(res.IsValid, $"Expected valid DNG, but failed: {res.ErrorMessage}");
            Assert.Null(res.ErrorMessage);
        }

        [Fact]
        public void VerifyIntegrity_Raf_TooSmall_ReturnsFailure()
        {
            string smallRaf = Path.Combine(_tempDir, "small.raf");
            File.WriteAllBytes(smallRaf, new byte[100]); // Less than 148 bytes

            var res = _service.VerifyIntegrity(smallRaf);
            Assert.False(res.IsValid);
            Assert.Contains("too small", res.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void VerifyIntegrity_Raf_InvalidMagic_ReturnsFailure()
        {
            string badRaf = Path.Combine(_tempDir, "bad_magic.raf");
            byte[] data = new byte[512];
            Encoding.ASCII.GetBytes("NOT-A-VALID-RAF-").CopyTo(data, 0);
            File.WriteAllBytes(badRaf, data);

            var res = _service.VerifyIntegrity(badRaf);
            Assert.False(res.IsValid);
            Assert.Contains("magic mismatch", res.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void VerifyIntegrity_Raf_MissingModelString_ReturnsFailure()
        {
            string badRaf = Path.Combine(_tempDir, "no_model.raf");
            byte[] data = new byte[512];
            Encoding.ASCII.GetBytes("FUJIFILMCCD-RAW ").CopyTo(data, 0);
            // 0x1C is left all zeros (null/empty model)
            File.WriteAllBytes(badRaf, data);

            var res = _service.VerifyIntegrity(badRaf);
            Assert.False(res.IsValid);
            Assert.Contains("camera model string", res.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void VerifyIntegrity_Raf_InvalidCfaOffset_ReturnsFailure()
        {
            string badRaf = Path.Combine(_tempDir, "bad_cfa.raf");
            byte[] data = new byte[512];
            Encoding.ASCII.GetBytes("FUJIFILMCCD-RAW ").CopyTo(data, 0);
            Encoding.ASCII.GetBytes("X-T5").CopyTo(data, 0x1C);
            // meta_off = 0 (invalid)
            data[0x5C] = 0; data[0x5D] = 0; data[0x5E] = 0; data[0x5F] = 0;
            // meta_len = 64
            data[0x60] = 0; data[0x61] = 0; data[0x62] = 0; data[0x63] = 64;
            File.WriteAllBytes(badRaf, data);

            var res = _service.VerifyIntegrity(badRaf);
            Assert.False(res.IsValid);
            Assert.Contains("CFA Meta Directory", res.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void VerifyIntegrity_Raf_ValidSyntheticRaf_ReturnsSuccess()
        {
            string validRaf = Path.Combine(_tempDir, "valid.raf");
            byte[] data = CreateSyntheticValidRaf();
            File.WriteAllBytes(validRaf, data);

            var res = _service.VerifyIntegrity(validRaf);
            Assert.True(res.IsValid, $"Expected valid RAF, but failed: {res.ErrorMessage}");
            Assert.Null(res.ErrorMessage);
        }

        [Fact]
        public void VerifyIntegrity_GenericFile_ValidAndInvalid()
        {
            string smallGeneric = Path.Combine(_tempDir, "small.jpg");
            File.WriteAllBytes(smallGeneric, new byte[30]); // < 64 bytes
            var resSmall = _service.VerifyIntegrity(smallGeneric);
            Assert.False(resSmall.IsValid);
            Assert.Contains("too small", resSmall.ErrorMessage, StringComparison.OrdinalIgnoreCase);

            string validGeneric = Path.Combine(_tempDir, "valid.jpg");
            File.WriteAllBytes(validGeneric, new byte[256]);
            var resValid = _service.VerifyIntegrity(validGeneric);
            Assert.True(resValid.IsValid);
        }

        [Fact]
        public async Task VerifyIntegrityAsync_SucceedsAndRespectsCancellation()
        {
            string validDng = Path.Combine(_tempDir, "async_valid.dng");
            File.WriteAllBytes(validDng, CreateSyntheticValidDng());

            var res = await _service.VerifyIntegrityAsync(validDng, CancellationToken.None);
            Assert.True(res.IsValid);

            using var cts = new CancellationTokenSource();
            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                await _service.VerifyIntegrityAsync(validDng, cts.Token);
            });
        }

        [Fact]
        public void CommandLineArgs_VerifyIntegrityOptions_ParsedCorrectly()
        {
            var parsed1 = CommandLineArgs.Parse(new[] { "--verify", "photo.arw" });
            Assert.True(parsed1.VerifyFileIntegrity);

            var parsed2 = CommandLineArgs.Parse(new[] { "--verify-integrity", "photo.arw" });
            Assert.True(parsed2.VerifyFileIntegrity);

            var parsed3 = CommandLineArgs.Parse(new[] { "--check-integrity", "photo.arw" });
            Assert.True(parsed3.VerifyFileIntegrity);

            var parsed4 = CommandLineArgs.Parse(new[] { "--no-verify", "photo.arw" });
            Assert.False(parsed4.VerifyFileIntegrity);
        }

        [Fact]
        public void SettingsService_VerifyFileIntegrity_PersistsAcrossSaveAndReload()
        {
            string settingsFile = Path.Combine(_tempDir, "settings.json");
            var settingsService = new SettingsService(settingsFile);
            Assert.False(settingsService.Settings.VerifyFileIntegrity);

            settingsService.Settings.VerifyFileIntegrity = true;
            settingsService.Save();

            var reloaded = new SettingsService(settingsFile);
            Assert.True(reloaded.Settings.VerifyFileIntegrity);
        }

        [Fact]
        public void PreferencesViewModel_VerifyFileIntegrity_SyncsWithSettings()
        {
            string settingsFile = Path.Combine(_tempDir, "prefs_settings.json");
            var settingsService = new SettingsService(settingsFile);
            var licenseService = new LicenseService(settingsService);

            var vm = new PreferencesViewModel(settingsService, licenseService);
            Assert.False(vm.VerifyFileIntegrity);

            vm.VerifyFileIntegrity = true;
            vm.SaveCommand.Execute(null);

            var reloaded = new SettingsService(settingsFile);
            Assert.True(reloaded.Settings.VerifyFileIntegrity);
        }

        [Fact]
        public void QuickProcessViewModel_VerifyFileIntegrity_LoadsFromSettings()
        {
            string settingsFile = Path.Combine(_tempDir, "qp_settings.json");
            var settingsService = new SettingsService(settingsFile);
            settingsService.Settings.VerifyFileIntegrity = true;
            settingsService.Save();

            var qpVm = new QuickProcessViewModel(settingsService: settingsService);
            Assert.True(qpVm.VerifyFileIntegrity);

            qpVm.VerifyFileIntegrity = false;
            Assert.False(qpVm.VerifyFileIntegrity);
        }

        private static byte[] CreateSyntheticValidDng()
        {
            byte[] data = new byte[2048];
            // Byte order: II (Little-endian)
            data[0] = 0x49;
            data[1] = 0x49;
            // Magic: 42
            data[2] = 42;
            data[3] = 0;
            // IFD0 offset: 8
            BitConverter.GetBytes((uint)8).CopyTo(data, 4);

            // IFD0 entry count: 1
            BitConverter.GetBytes((ushort)1).CopyTo(data, 8);

            // Entry 0: tag 0xC614 (UniqueCameraModel)
            BitConverter.GetBytes((ushort)0xC614).CopyTo(data, 10);
            BitConverter.GetBytes((ushort)2).CopyTo(data, 12); // Type ASCII
            BitConverter.GetBytes((uint)14).CopyTo(data, 14);  // Count 14
            BitConverter.GetBytes((uint)22).CopyTo(data, 18);  // Value offset 22

            // Value at offset 22
            Encoding.ASCII.GetBytes("Fujifilm X-T5\0").CopyTo(data, 22);

            return data;
        }

        private static byte[] CreateSyntheticValidRaf()
        {
            byte[] data = new byte[512];
            // Magic
            Encoding.ASCII.GetBytes("FUJIFILMCCD-RAW ").CopyTo(data, 0);
            // Version
            Encoding.ASCII.GetBytes("0201    ").CopyTo(data, 16);
            // Camera Model at 0x1C (32 bytes)
            Encoding.ASCII.GetBytes("X-T5").CopyTo(data, 0x1C);

            // CFA Meta Directory offset (big endian) at 0x5C: 148
            data[0x5C] = 0; data[0x5D] = 0; data[0x5E] = 0; data[0x5F] = 148;
            // CFA Meta Directory length (big endian) at 0x60: 64
            data[0x60] = 0; data[0x61] = 0; data[0x62] = 0; data[0x63] = 64;

            // CFA Directory tag count at offset 148 (4 bytes)
            data[148] = 0; data[149] = 0; data[150] = 0; data[151] = 1;

            return data;
        }
    }
}
