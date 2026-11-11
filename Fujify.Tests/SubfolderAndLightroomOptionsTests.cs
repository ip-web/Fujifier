using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Fujify.Models;
using Fujify.Services;
using Fujify.ViewModels;
using Xunit;

namespace Fujify.Tests
{
    public class SubfolderAndLightroomOptionsTests
    {
        [Fact]
        public void ComputeTargetDirectory_WhenSubfolderDisabled_ReturnsBaseDirectory()
        {
            string sourceFile = Path.Combine("C:", "Photos", "Holiday", "DSC001.ARW");
            string expectedBase = Path.Combine("C:", "Photos", "Holiday");

            string result = OutputLocationHelper.ComputeTargetDirectory(
                sourceFile,
                customOutputFolder: null,
                createSubfolder: false,
                subfolderUseOriginalName: true,
                subfolderSuffix: "-fujified",
                subfolderCustomName: "Fujified");

            Assert.Equal(expectedBase, result);
        }

        [Fact]
        public void ComputeTargetDirectory_WhenSubfolderEnabled_AndKeepOriginalNameWithSuffix()
        {
            string sourceFile = Path.Combine("C:", "Photos", "Holiday", "DSC001.ARW");
            string expected = Path.Combine("C:", "Photos", "Holiday", "Holiday-fujified");

            string result = OutputLocationHelper.ComputeTargetDirectory(
                sourceFile,
                customOutputFolder: null,
                createSubfolder: true,
                subfolderUseOriginalName: true,
                subfolderSuffix: "-fujified",
                subfolderCustomName: "Fujified");

            Assert.Equal(expected, result);
        }

        [Fact]
        public void ComputeTargetDirectory_WhenSubfolderEnabled_AndKeepOriginalNameWithoutSuffix()
        {
            string sourceFile = Path.Combine("C:", "Photos", "Holiday", "DSC001.ARW");
            string expected = Path.Combine("C:", "Photos", "Holiday", "Holiday");

            string result = OutputLocationHelper.ComputeTargetDirectory(
                sourceFile,
                customOutputFolder: null,
                createSubfolder: true,
                subfolderUseOriginalName: true,
                subfolderSuffix: "",
                subfolderCustomName: "Fujified");

            Assert.Equal(expected, result);
        }

        [Fact]
        public void ComputeTargetDirectory_WhenSubfolderEnabled_AndCustomSubfolderName()
        {
            string sourceFile = Path.Combine("C:", "Photos", "Holiday", "DSC001.ARW");
            string expected = Path.Combine("C:", "Photos", "Holiday", "ConvertedDNG");

            string result = OutputLocationHelper.ComputeTargetDirectory(
                sourceFile,
                customOutputFolder: null,
                createSubfolder: true,
                subfolderUseOriginalName: false,
                subfolderSuffix: "-fujified",
                subfolderCustomName: "ConvertedDNG");

            Assert.Equal(expected, result);
        }

        [Fact]
        public void ComputeTargetDirectory_WithCustomOutputFolder_CreatesSubfolderUnderCustomOutput()
        {
            string sourceFile = Path.Combine("C:", "Photos", "Holiday", "DSC001.ARW");
            string customOutput = Path.Combine("D:", "ExportFolder");
            string expected = Path.Combine("D:", "ExportFolder", "Holiday-fujified");

            string result = OutputLocationHelper.ComputeTargetDirectory(
                sourceFile,
                customOutputFolder: customOutput,
                createSubfolder: true,
                subfolderUseOriginalName: true,
                subfolderSuffix: "-fujified",
                subfolderCustomName: "Fujified");

            Assert.Equal(expected, result);
        }

        [Fact]
        public void ComputeInitialDestinationFilePath_CombinesSubfolderAndFileSuffix()
        {
            string sourceFile = Path.Combine("C:", "Photos", "Holiday", "DSC001.ARW");
            var settings = new AppSettings
            {
                CreateSubfolder = true,
                SubfolderUseOriginalName = true,
                SubfolderSuffix = "-fujified",
                AppendSuffix = true,
                Suffix = "-fujified"
            };

            string result = OutputLocationHelper.ComputeInitialDestinationFilePath(
                sourceFile,
                customOutputFolder: null,
                settings);

            string expected = Path.Combine("C:", "Photos", "Holiday", "Holiday-fujified", "DSC001-fujified.dng");
            Assert.Equal(expected, result);
        }

        [Fact]
        public void AppSettings_SubfolderProperties_SerializeAndDeserialize()
        {
            var original = new AppSettings
            {
                CreateSubfolder = true,
                SubfolderUseOriginalName = false,
                SubfolderSuffix = "_processed",
                SubfolderCustomName = "DNG_Exports",
                AutoOpenOutputFolder = true
            };

            string json = JsonSerializer.Serialize(original);
            var deserialized = JsonSerializer.Deserialize<AppSettings>(json);

            Assert.NotNull(deserialized);
            Assert.True(deserialized.CreateSubfolder);
            Assert.False(deserialized.SubfolderUseOriginalName);
            Assert.Equal("_processed", deserialized.SubfolderSuffix);
            Assert.Equal("DNG_Exports", deserialized.SubfolderCustomName);
            Assert.True(deserialized.AutoOpenOutputFolder);
        }

        [Fact]
        public void FileItem_ErrorHighlightProperties_WhenStatusIsError()
        {
            var item = new Models.FileItem
            {
                FilePath = "C:\\test\\photo.arw",
                Status = "Error: Native conversion failed"
            };

            Assert.True(item.IsError);
            Assert.Equal("#E74C3C", item.StatusColor);
            Assert.Equal("#22E74C3C", item.ErrorBackground);
            Assert.Equal("#80E74C3C", item.ErrorBorderBrush);
            Assert.Equal(1.0, item.ErrorBorderThickness);
            Assert.Equal("✕", item.StatusIcon);
        }

        [Fact]
        public void FileItem_ErrorHighlightProperties_WhenStatusIsSuccessOrPending()
        {
            var pendingItem = new Models.FileItem
            {
                FilePath = "C:\\test\\photo1.arw",
                Status = "Pending"
            };

            Assert.False(pendingItem.IsError);
            Assert.Equal("#A0A0AC", pendingItem.StatusColor);
            Assert.Equal("Transparent", pendingItem.ErrorBackground);
            Assert.Equal("Transparent", pendingItem.ErrorBorderBrush);
            Assert.Equal(0.0, pendingItem.ErrorBorderThickness);
            Assert.Equal("⏳", pendingItem.StatusIcon);

            var successItem = new Models.FileItem
            {
                FilePath = "C:\\test\\photo2.arw",
                Status = "✔"
            };

            Assert.False(successItem.IsError);
            Assert.True(successItem.IsCompleted);
            Assert.Equal("#01D701", successItem.StatusColor);
            Assert.Equal("Transparent", successItem.ErrorBackground);
            Assert.Equal("Transparent", successItem.ErrorBorderBrush);
            Assert.Equal(0.0, successItem.ErrorBorderThickness);
            Assert.Equal("✓", successItem.StatusIcon);
        }

        [Fact]
        public void SortFilesByStatus_MovesErrorFilesToTop()
        {
            var settingsService = new FakeSettingsService();
            var licenseService = new LicenseService(settingsService);
            var vm = new MainViewModel(settingsService: settingsService, licenseService: licenseService);

            var file1 = new Models.FileItem { FilePath = "C:\\photos\\img1.arw", Status = "✔" };
            var file2 = new Models.FileItem { FilePath = "C:\\photos\\img2.arw", Status = "Error: Corrupted file" };
            var file3 = new Models.FileItem { FilePath = "C:\\photos\\img3.arw", Status = "Pending" };
            var file4 = new Models.FileItem { FilePath = "C:\\photos\\img4.arw", Status = "Error: Cannot write" };
            var file5 = new Models.FileItem { FilePath = "C:\\photos\\img5.arw", Status = "✔" };

            vm.Files.Add(file1);
            vm.Files.Add(file2);
            vm.Files.Add(file3);
            vm.Files.Add(file4);
            vm.Files.Add(file5);

            vm.SortFilesByStatus();

            // Error items should be first
            Assert.Equal("C:\\photos\\img2.arw", vm.Files[0].FilePath);
            Assert.Equal("C:\\photos\\img4.arw", vm.Files[1].FilePath);
            // Then pending / other
            Assert.Equal("C:\\photos\\img3.arw", vm.Files[2].FilePath);
            // Then completed
            Assert.Equal("C:\\photos\\img1.arw", vm.Files[3].FilePath);
            Assert.Equal("C:\\photos\\img5.arw", vm.Files[4].FilePath);

            // First error item should be automatically selected
            Assert.Equal(vm.Files[0], vm.SelectedFile);
        }

        [Fact]
        public void PreferencesViewModel_AutoOpenOutputFolder_SavesToSettings()
        {
            var settingsService = new FakeSettingsService();
            var licenseService = new LicenseService(settingsService);
            var prefs = new PreferencesViewModel(settingsService, licenseService);

            Assert.False(prefs.AutoOpenOutputFolder);
            prefs.AutoOpenOutputFolder = true;

            prefs.SaveCommand.Execute(null);

            Assert.True(settingsService.Settings.AutoOpenOutputFolder);
        }

        [Fact]
        public void LightroomOption_IsDisabledOnDesktopPlatforms()
        {
            bool isMobile = OperatingSystem.IsAndroid() || OperatingSystem.IsIOS();

            var settingsService = new FakeSettingsService();
            var licenseService = new LicenseService(settingsService);
            var prefs = new PreferencesViewModel(settingsService, licenseService);
            Assert.Equal(isMobile, prefs.IsOpenInLightroomSupported);

            if (!isMobile)
            {
                Assert.False(prefs.IsOpenInLightroomSupported);
            }
        }

        [Fact]
        public void SubfolderOption_IsSupportedAcrossPlatforms()
        {
            var settingsService = new FakeSettingsService();
            var licenseService = new LicenseService(settingsService);
            var prefs = new PreferencesViewModel(settingsService, licenseService);
            var mainVm = new MainViewModel(settingsService: settingsService, licenseService: licenseService);

            Assert.True(prefs.IsSubfolderOptionSupported);
            Assert.True(mainVm.IsSubfolderOptionSupported);
        }

        [Fact]
        public void QuickProcessViewModel_SubfolderPreview_DefaultsToPhotos_WhenNoFilesAndNoOutputFolder()
        {
            var settingsService = new FakeSettingsService();
            settingsService.Settings.CustomOutputFolder = null;
            var vm = new QuickProcessViewModel(settingsService: settingsService);
            vm.CreateSubfolder = true;
            vm.SubfolderUseOriginalName = true;
            vm.SubfolderSuffix = "-fujified";

            Assert.Equal("Photos-fujified", vm.SubfolderPreview);
        }

        [Fact]
        public void QuickProcessViewModel_SubfolderPreview_UsesPictureFolder_WhenFilesSelected()
        {
            var settingsService = new FakeSettingsService();
            settingsService.Settings.CustomOutputFolder = null;
            string tempDir = Path.Combine(Path.GetTempPath(), $"trip_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            string testFile = Path.Combine(tempDir, "photo1.arw");
            File.WriteAllText(testFile, "dummy");

            try
            {
                var vm = new QuickProcessViewModel(
                    initialFiles: new[] { testFile },
                    settingsService: settingsService);
                vm.CreateSubfolder = true;
                vm.SubfolderUseOriginalName = true;
                vm.SubfolderSuffix = "-fujified";

                string expectedFolder = Path.GetFileName(tempDir);
                Assert.Equal($"{expectedFolder}-fujified", vm.SubfolderPreview);
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void QuickProcessViewModel_SubfolderPreview_UsesOutputFolder_WhenOutputFolderIsSet()
        {
            var settingsService = new FakeSettingsService();
            settingsService.Settings.CustomOutputFolder = null;
            string tempDir = Path.Combine(Path.GetTempPath(), $"trip_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            string testFile = Path.Combine(tempDir, "photo1.arw");
            File.WriteAllText(testFile, "dummy");
            string outputFolder = Path.Combine("D:", "Exports");

            try
            {
                var vm = new QuickProcessViewModel(
                    initialFiles: new[] { testFile },
                    settingsService: settingsService);
                vm.CreateSubfolder = true;
                vm.SubfolderUseOriginalName = true;
                vm.SubfolderSuffix = "-fujified";

                string expectedFolder = Path.GetFileName(tempDir);
                Assert.Equal($"{expectedFolder}-fujified", vm.SubfolderPreview);

                vm.OutputFolderPath = outputFolder;
                Assert.Equal("Exports-fujified", vm.SubfolderPreview);
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void QuickProcessViewModel_SubfolderPreview_ClearingOutputFolder_RevertsToPictureFolder()
        {
            var settingsService = new FakeSettingsService();
            settingsService.Settings.CustomOutputFolder = null;
            string tempDir = Path.Combine(Path.GetTempPath(), $"trip_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            string testFile = Path.Combine(tempDir, "photo1.arw");
            File.WriteAllText(testFile, "dummy");
            string outputFolder = Path.Combine("D:", "Exports");

            try
            {
                var vm = new QuickProcessViewModel(
                    initialFiles: new[] { testFile },
                    settingsService: settingsService);
                vm.CreateSubfolder = true;
                vm.SubfolderUseOriginalName = true;
                vm.SubfolderSuffix = "-fujified";
                vm.OutputFolderPath = outputFolder;

                Assert.Equal("Exports-fujified", vm.SubfolderPreview);

                vm.OutputFolderPath = null;
                string expectedFolder = Path.GetFileName(tempDir);
                Assert.Equal($"{expectedFolder}-fujified", vm.SubfolderPreview);
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void MainViewModel_SubfolderPreview_UsesPictureFolder_WhenFilesAdded()
        {
            var settingsService = new FakeSettingsService();
            settingsService.Settings.CustomOutputFolder = null;
            var vm = new MainViewModel(settingsService: settingsService);
            vm.CreateSubfolder = true;
            vm.SubfolderUseOriginalName = true;
            vm.SubfolderSuffix = "-fujified";

            Assert.Equal("Photos/Photos-fujified/", vm.SubfolderPreview);

            vm.Files.Add(new FileItem { FilePath = Path.Combine("C:", "Holiday", "pic1.arw") });
            Assert.Equal("Holiday/Holiday-fujified/", vm.SubfolderPreview);
        }

        [Fact]
        public void MainViewModel_SubfolderPreview_UsesOutputFolder_WhenOutputFolderIsSet()
        {
            var settingsService = new FakeSettingsService();
            settingsService.Settings.CustomOutputFolder = null;
            var vm = new MainViewModel(settingsService: settingsService);
            vm.CreateSubfolder = true;
            vm.SubfolderUseOriginalName = true;
            vm.SubfolderSuffix = "-fujified";

            vm.OutputFolderPath = Path.Combine("D:", "OutputDirectory");
            Assert.Equal("OutputDirectory/OutputDirectory-fujified/", vm.SubfolderPreview);
        }

        [Fact]
        public void MainViewModel_SubfolderPreview_ClearingOutputFolder_RevertsToPictureFolder()
        {
            var settingsService = new FakeSettingsService();
            settingsService.Settings.CustomOutputFolder = null;
            var vm = new MainViewModel(settingsService: settingsService);
            vm.CreateSubfolder = true;
            vm.SubfolderUseOriginalName = true;
            vm.SubfolderSuffix = "-fujified";

            vm.Files.Add(new FileItem { FilePath = Path.Combine("C:", "Holiday", "pic1.arw") });
            vm.OutputFolderPath = Path.Combine("D:", "OutputDirectory");
            Assert.Equal("OutputDirectory/OutputDirectory-fujified/", vm.SubfolderPreview);

            vm.OutputFolderPath = null;
            Assert.Equal("Holiday/Holiday-fujified/", vm.SubfolderPreview);
        }

        [Fact]
        public void PreferencesViewModel_SubfolderPreview_UsesCustomOutputFolder_WhenSetInSettings()
        {
            var settingsService = new FakeSettingsService();
            settingsService.Settings.CustomOutputFolder = Path.Combine("D:", "MyCustomOutput");
            var licenseService = new LicenseService(settingsService);
            var prefs = new PreferencesViewModel(settingsService, licenseService);
            prefs.CreateSubfolder = true;
            prefs.SubfolderUseOriginalName = true;
            prefs.SubfolderSuffix = "-fujified";

            Assert.Equal("MyCustomOutput → MyCustomOutput/MyCustomOutput-fujified/", prefs.SubfolderPreview);
        }

        [Fact]
        public void OutputLocationHelper_ComputeTargetDirectory_FallsBackToCustomOutputFolder_WhenSourceDirIsEmpty()
        {
            string expected = Path.Combine("D:", "ExportFolder", "ExportFolder-fujified");

            string result = OutputLocationHelper.ComputeTargetDirectory(
                sourceFilePath: "pic1.arw",
                customOutputFolder: Path.Combine("D:", "ExportFolder"),
                createSubfolder: true,
                subfolderUseOriginalName: true,
                subfolderSuffix: "-fujified",
                subfolderCustomName: "Fujified");

            Assert.Equal(expected, result);
        }

        [Fact]
        public void ResolveAndroidStoragePath_ResolvesSafUrisCorrectly()
        {
            Assert.Equal("/storage/emulated/0/MyFolder", DialogService.ResolveAndroidStoragePathString("primary:MyFolder"));
            Assert.Equal("/storage/emulated/0/MyFolder", DialogService.ResolveAndroidStoragePathString("/tree/primary:MyFolder"));
            Assert.Equal("/storage/emulated/0/MyFolder", DialogService.ResolveAndroidStoragePathString("content://com.android.externalstorage.documents/tree/primary%3AMyFolder"));
            Assert.Equal("/storage/1234-5678/MyFolder", DialogService.ResolveAndroidStoragePathString("content://com.android.externalstorage.documents/tree/1234-5678%3AMyFolder"));
            Assert.Equal("/storage/emulated/0", DialogService.ResolveAndroidStoragePathString("primary:"));
            Assert.Equal("/storage/emulated/0", DialogService.ResolveAndroidStoragePathString("/tree/primary:"));

            // Normal desktop path should remain untouched
            Assert.Equal("C:\\Photos", DialogService.ResolveAndroidStoragePathString("C:\\Photos"));
        }

        [Fact]
        public async Task ProcessFileAsync_WithOutputFolder_ShouldDirectlyConvertIntoDestinationFolder_WithoutSourceDirTempFiles()
        {
            var tempSourceDir = Path.Combine(Path.GetTempPath(), $"src_{Guid.NewGuid():N}");
            var tempOutputDir = Path.Combine(Path.GetTempPath(), $"out_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempSourceDir);
            Directory.CreateDirectory(tempOutputDir);

            string rawFile = Path.Combine(tempSourceDir, "photo1.arw");
            File.WriteAllText(rawFile, "dummy raw content");

            try
            {
                var settingsService = new FakeSettingsService();
                settingsService.Settings.EnableCaptureOneMode = false;
                settingsService.Settings.ConversionTargetMode = "Lightroom";
                settingsService.Settings.BackupOriginalFile = true;

                string? capturedTargetDngPath = null;
                var mockRawConverter = new MockRawConverter
                {
                    OnConvert = (file, embed, target, lossless) =>
                    {
                        capturedTargetDngPath = target;
                        // Simulate creating the target DNG file
                        File.WriteAllText(target!, "dummy dng content");
                        return new ConversionResult(true, target, null);
                    }
                };

                var mockMetadata = new MockMetadataService
                {
                    OnInject = (dng, outDir, backup, isDng, customOut, model, srcRaw) =>
                    {
                        return new MetadataResult(true, null);
                    }
                };

                var vm = new MainViewModel(
                    rawConverterService: mockRawConverter,
                    metadataService: mockMetadata,
                    settingsService: settingsService);

                vm.OutputFolderPath = tempOutputDir;

                var reservedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                bool success = await vm.ProcessSingleFileAsync(
                    rawFile,
                    tempOutputDir,
                    createBackup: true,
                    conflictChoice: ConflictChoice.Overwrite,
                    reservedOutputPaths: reservedPaths,
                    ct: CancellationToken.None);

                Assert.True(success);
                Assert.NotNull(capturedTargetDngPath);

                // Target DNG path MUST be inside tempOutputDir and match the expected file name
                Assert.StartsWith(tempOutputDir, capturedTargetDngPath, StringComparison.OrdinalIgnoreCase);
                Assert.Equal(Path.Combine(tempOutputDir, "photo1.dng"), capturedTargetDngPath);
                Assert.DoesNotContain("_tmp_", capturedTargetDngPath);

                // Assert NO temp files were ever created in tempSourceDir
                var sourceFiles = Directory.GetFiles(tempSourceDir);
                Assert.Single(sourceFiles);
                Assert.Equal(rawFile, sourceFiles[0]);
            }
            finally
            {
                if (Directory.Exists(tempSourceDir)) Directory.Delete(tempSourceDir, true);
                if (Directory.Exists(tempOutputDir)) Directory.Delete(tempOutputDir, true);
            }
        }

        private class MockRawConverter : IRawConverterService
        {
            public Func<string, bool, string?, bool, ConversionResult>? OnConvert { get; set; }

            public ConversionResult ConvertToDng(string filePath, bool embedOriginalRaw, string? targetDngPath = null, bool losslessCompression = true)
            {
                return OnConvert?.Invoke(filePath, embedOriginalRaw, targetDngPath, losslessCompression)
                    ?? new ConversionResult(true, targetDngPath ?? filePath, null);
            }

            public Task<ConversionResult> ConvertToDngAsync(string filePath, bool embedOriginalRaw, string? targetDngPath = null, bool losslessCompression = true, CancellationToken cancellationToken = default)
            {
                return Task.FromResult(ConvertToDng(filePath, embedOriginalRaw, targetDngPath, losslessCompression));
            }
        }

        private class MockMetadataService : IMetadataService
        {
            public Func<string, string?, bool, bool, string?, string, string?, MetadataResult>? OnInject { get; set; }

            public string GetCameraMakeAndModel(string filePath) => "Sony ILCE-7M4";
            public int GetOrientation(string filePath) => 1;
            public ExifData? GetExifData(string filePath) => null;

            public MetadataResult InjectFujifilmMetadata(string dngFilePath, string? outputFolder, bool createBackup, bool isOriginallyDng, string? customOutputFilePath = null, string targetUniqueCameraModel = "Fujifilm X-T5", string? sourceRawFilePath = null)
            {
                return OnInject?.Invoke(dngFilePath, outputFolder, createBackup, isOriginallyDng, customOutputFilePath, targetUniqueCameraModel, sourceRawFilePath)
                    ?? new MetadataResult(true, null);
            }

            public Task<MetadataResult> InjectFujifilmMetadataAsync(string dngFilePath, string? outputFolder, bool createBackup, bool isOriginallyDng, string? customOutputFilePath = null, string targetUniqueCameraModel = "Fujifilm X-T5", string? sourceRawFilePath = null, CancellationToken cancellationToken = default)
            {
                return Task.FromResult(InjectFujifilmMetadata(dngFilePath, outputFolder, createBackup, isOriginallyDng, customOutputFilePath, targetUniqueCameraModel, sourceRawFilePath));
            }

            public void CleanupTemporaryFiles(string dngFilePath, bool useOutputPath, bool createBackup, bool isOriginallyDng) { }
        }

        private class FakeSettingsService : ISettingsService
        {
            public AppSettings Settings { get; } = new AppSettings();
            public void Save() { }
            public void Load() { }
        }
    }
}
