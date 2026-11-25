using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Fujify.Models;
using Fujify.Services;
using Xunit;

namespace Fujify.Tests
{
    public class AuditImprovementsTests
    {
        private class MockSettingsService : ISettingsService
        {
            public AppSettings Settings { get; } = new();
            public void Save() { }
            public void Load() { }
        }

        private class MockHttpMessageHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
            public int CallCount { get; private set; }

            public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
            {
                _responder = responder;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                CallCount++;
                return Task.FromResult(_responder(request));
            }
        }

        [Fact]
        public void LightroomPresetService_GeneratePresetXml_EscapesSpecialXmlCharacters()
        {
            var svc = new LightroomPresetService();
            var preset = new LightroomPresetInfo(
                Id: "test",
                DisplayName: "Velvia & Vivid <Pro> \"Test\"",
                CameraProfile: "Camera Velvia & Vivid",
                FileName: "test.xmp",
                Description: "Desc & <xml>",
                Uuid: "1234",
                LookName: "Velvia <Look> & \"1\"",
                LookUuid: "5678");

            string xml = svc.GeneratePresetXml(preset);
            var doc = XDocument.Parse(xml);
            Assert.NotNull(doc.Root);
        }

        [Fact]
        public void DngMetadataEditor_BuildDefaultXmp_EscapesSpecialXmlCharacters()
        {
            string xmp = DngMetadataEditor.BuildDefaultXmp("FUJI & CO", "X-T5 <Mark II>", "Fujifilm X-T5 & Pro");
            string inner = xmp.Replace("<?xpacket begin='﻿' id='W5M0MpCehiHzreSzNTczkc9d'?>", "")
                              .Replace("<?xpacket end='w'?>", "")
                              .Trim();
            var doc = XDocument.Parse(inner);
            Assert.NotNull(doc.Root);
        }

        [Fact]
        public void DngMetadataEditor_ReadXmpPacket_RejectsOversizedXmpTagWithoutOom()
        {
            string tempFile = Path.Combine(Path.GetTempPath(), $"corrupt_xmp_{Guid.NewGuid():N}.dng");
            try
            {
                using (var fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write))
                using (var bw = new BinaryWriter(fs))
                {
                    // TIFF Little Endian header -> IFD0 at offset 8
                    bw.Write((byte)'I');
                    bw.Write((byte)'I');
                    bw.Write((ushort)42);
                    bw.Write((uint)8);

                    // IFD0: 1 entry -> TagXmp (0x02BC), Type Byte (1), Count = 0x7FFF0000 (~2 GB), Offset = 26
                    bw.Write((ushort)1);
                    bw.Write((ushort)DngMetadataEditor.TagXmp);
                    bw.Write((ushort)DngMetadataEditor.TypeByte);
                    bw.Write((uint)0x7FFF0000);
                    bw.Write((uint)26);
                    bw.Write((uint)0);
                }

                string? result = DngMetadataEditor.ReadXmpPacket(tempFile);
                Assert.Null(result);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public void UpdateService_ParseReleaseJson_ExtractsSha256Digest()
        {
            string json = @"{
                ""tag_name"": ""v2.3.0"",
                ""name"": ""Fujifier 2.3.0"",
                ""draft"": false,
                ""assets"": [
                    {
                        ""name"": ""Fujifier-v2.3.0-Setup.exe"",
                        ""browser_download_url"": ""https://github.com/ip-web/Fujifier/releases/download/v2.3.0/Fujifier-v2.3.0-Setup.exe"",
                        ""size"": 1024,
                        ""content_type"": ""application/octet-stream"",
                        ""digest"": ""sha256:0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF""
                    }
                ]
            }";

            var info = UpdateService.ParseReleaseJson(json);
            Assert.NotNull(info);
            Assert.Single(info.Assets);
            Assert.Equal("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", info.Assets[0].Sha256Digest);
        }

        [Fact]
        public async Task UpdateService_DownloadUpdateAsync_RejectsNonHttpsUrl()
        {
            var settings = new MockSettingsService();
            var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
            var service = new UpdateService(settings, new HttpClient(handler));

            var asset = new UpdateAsset
            {
                Name = "Fujifier-Setup.exe",
                DownloadUrl = "http://insecure.example.com/setup.exe"
            };

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.DownloadUpdateAsync(asset));
            Assert.Equal(0, handler.CallCount);
        }

        [Fact]
        public async Task UpdateService_CheckForUpdateAsync_RespectsCooldownWhenNotForced()
        {
            var settings = new MockSettingsService();
            settings.Settings.LastUpdateCheck = DateTime.UtcNow.AddMinutes(-10);
            var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
            var service = new UpdateService(settings, new HttpClient(handler));

            var result = await service.CheckForUpdateAsync(force: false);

            Assert.True(result.Success);
            Assert.False(result.IsUpdateAvailable);
            Assert.Equal(0, handler.CallCount);
        }

        [Fact]
        public async Task LicenseService_VerifyCurrentLicenseAsync_ReturnsFailureOnServerErrorWhenOfflineGraceExpired()
        {
            var settings = new MockSettingsService();
            settings.Settings.LicenseKey = "FUJIFIER-TEST-KEY";
            settings.Settings.LicenseeEmail = "user@example.com";
            settings.Settings.OfflineTokenExpires = DateTime.UtcNow.AddDays(-1);

            var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("Server Error")
            });

            var service = new LicenseService(settings, new HttpClient(handler));
            var result = await service.VerifyCurrentLicenseAsync(forceNetwork: true);

            Assert.False(result.Success);
            Assert.NotNull(result.ErrorMessage);
        }

        [Fact]
        public void FileItem_ProcessingState_TracksExplicitStateAccurately()
        {
            var item = new FileItem { FilePath = "C:\\temp\\photo.raf" };
            Assert.Equal(FileProcessingState.Pending, item.ProcessingState);

            item.SetStatus("Chargement...", FileProcessingState.Processing);
            Assert.Equal(FileProcessingState.Processing, item.ProcessingState);
            Assert.True(item.IsActive);
            Assert.False(item.IsError);

            item.SetStatus("Erreur inattendue", FileProcessingState.Error, "Detail");
            Assert.Equal(FileProcessingState.Error, item.ProcessingState);
            Assert.True(item.IsError);
            Assert.Equal("Detail", item.ErrorMessage);

            item.SetStatus("Terminé", FileProcessingState.Completed);
            Assert.Equal(FileProcessingState.Completed, item.ProcessingState);
            Assert.True(item.IsCompleted);
            Assert.False(item.IsError);
        }

        [Fact]
        public void ConversionPipelineHelper_GetUniqueFilePath_AvoidsCollisionsAndReserves()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), $"unique_path_test_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            try
            {
                string baseFile = Path.Combine(tempDir, "IMG_0001.dng");
                File.WriteAllText(baseFile, "x");
                var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                string first = ConversionPipelineHelper.GetUniqueFilePath(baseFile, reserved);
                string second = ConversionPipelineHelper.GetUniqueFilePath(baseFile, reserved);

                Assert.EndsWith("IMG_0001 (1).dng", first);
                Assert.EndsWith("IMG_0001 (2).dng", second);
                Assert.NotEqual(first, second);
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void DngLabNative_OnWindowsX64_ExposesConvertWithOptionsSymbol()
        {
            if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            {
                return;
            }

            Assert.True(DngLabNative.IsAvailable);
            string dllPath = Path.Combine(AppContext.BaseDirectory, "libs", "win-x64", "dnglab_ffi.dll");
            Assert.True(File.Exists(dllPath));

            Assert.True(NativeLibrary.TryLoad(dllPath, out IntPtr handle));
            try
            {
                Assert.True(NativeLibrary.TryGetExport(handle, "dnglab_convert_file_with_options", out IntPtr exportPtr));
                Assert.NotEqual(IntPtr.Zero, exportPtr);
            }
            finally
            {
                NativeLibrary.Free(handle);
            }
        }
    }
}
