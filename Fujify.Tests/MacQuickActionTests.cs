using System;
using System.IO;
using System.Xml.Linq;
using Fujify.Services;
using Xunit;

namespace Fujify.Tests
{
    public class MacQuickActionTests
    {
        [Fact]
        public void WorkflowConstants_ShouldBeDefinedProperly()
        {
            Assert.Equal("Convert with previous (Fujifier).workflow", MacQuickActionService.ConvertWithPreviousWorkflowName);
            Assert.Equal("Convert with options (Fujifier).workflow", MacQuickActionService.ConvertWithOptionsWorkflowName);
        }

        [Fact]
        public void GetServicesDirectory_WithCustomPath_ReturnsCustomPath()
        {
            string custom = Path.Combine(Path.GetTempPath(), "CustomServices");
            string result = MacQuickActionService.GetServicesDirectory(custom);
            Assert.Equal(custom, result);
        }

        [Fact]
        public void GetServicesDirectory_Default_ReturnsLibraryServicesPath()
        {
            string defaultPath = MacQuickActionService.GetServicesDirectory();
            Assert.False(string.IsNullOrWhiteSpace(defaultPath));
            Assert.EndsWith(Path.Combine("Library", "Services"), defaultPath);
        }

        [Theory]
        [InlineData("Convert with previous (Fujifier)", "dev.ipweb.fujifier.quickaction.convert-previous")]
        [InlineData("Convert with options (Fujifier)", "dev.ipweb.fujifier.quickaction.convert-options")]
        public void GetInfoPlistContent_ShouldProduceValidXmlWithRequiredUTIs(string displayName, string bundleId)
        {
            string xml = MacQuickActionService.GetInfoPlistContent(displayName, bundleId);

            // Must parse without XML exceptions
            var doc = XDocument.Parse(xml);
            Assert.NotNull(doc.Root);

            Assert.Contains(bundleId, xml);
            Assert.Contains(displayName, xml);
            Assert.Contains("public.camera-raw-image", xml);
            Assert.Contains("com.adobe.raw-image", xml);
            Assert.Contains("runWorkflowAsService", xml);
        }

        [Theory]
        [InlineData("--convert-previous")]
        [InlineData("--convert-options")]
        public void GetDocumentWflowContent_ShouldProduceValidXmlWithShellScript(string flag)
        {
            string xml = MacQuickActionService.GetDocumentWflowContent(flag);

            // Must parse without XML exceptions
            var doc = XDocument.Parse(xml);
            Assert.NotNull(doc.Root);

            Assert.Contains("com.apple.RunShellScript", xml);
            Assert.Contains("/bin/zsh", xml);
            Assert.Contains("dev.ipweb.fujifier", xml);
            Assert.Contains(flag, xml);
            Assert.Contains("<integer>1</integer>", xml); // inputMethod = 1 (pass as arguments)
        }

        [Fact]
        public void RegisterAndUnregisterQuickActions_Lifecycle_ShouldCreateAndRemoveWorkflows()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "FujifyMacQuickActionTest_" + Guid.NewGuid().ToString("N"));
            try
            {
                Assert.False(MacQuickActionService.AreQuickActionsRegistered(tempDir));

                // Register (dynamically generate)
                bool regResult = MacQuickActionService.RegisterQuickActions(tempDir, customSourceDir: Path.Combine(tempDir, "NonExistentSource"));
                Assert.True(regResult);
                Assert.True(MacQuickActionService.AreQuickActionsRegistered(tempDir));

                // Verify files exist on disk
                string prevWflow = Path.Combine(tempDir, MacQuickActionService.ConvertWithPreviousWorkflowName);
                string optsWflow = Path.Combine(tempDir, MacQuickActionService.ConvertWithOptionsWorkflowName);

                Assert.True(File.Exists(Path.Combine(prevWflow, "Contents", "Info.plist")));
                Assert.True(File.Exists(Path.Combine(prevWflow, "Contents", "document.wflow")));
                Assert.True(File.Exists(Path.Combine(optsWflow, "Contents", "Info.plist")));
                Assert.True(File.Exists(Path.Combine(optsWflow, "Contents", "document.wflow")));

                // Unregister
                bool unregResult = MacQuickActionService.UnregisterQuickActions(tempDir);
                Assert.True(unregResult);
                Assert.False(MacQuickActionService.AreQuickActionsRegistered(tempDir));
                Assert.False(Directory.Exists(prevWflow));
                Assert.False(Directory.Exists(optsWflow));
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            }
        }

        [Fact]
        public void RegisterQuickActions_FromExistingSourceDir_ShouldCopyFilesCorrectly()
        {
            string tempSource = Path.Combine(Path.GetTempPath(), "FujifySource_" + Guid.NewGuid().ToString("N"));
            string tempTarget = Path.Combine(Path.GetTempPath(), "FujifyTarget_" + Guid.NewGuid().ToString("N"));

            try
            {
                // Generate in source first
                MacQuickActionService.RegisterQuickActions(tempSource, customSourceDir: Path.Combine(tempSource, "None"));
                Assert.True(MacQuickActionService.AreQuickActionsRegistered(tempSource));

                // Now register in target by copying from source
                bool regResult = MacQuickActionService.RegisterQuickActions(tempTarget, customSourceDir: tempSource);
                Assert.True(regResult);
                Assert.True(MacQuickActionService.AreQuickActionsRegistered(tempTarget));
            }
            finally
            {
                if (Directory.Exists(tempSource)) { try { Directory.Delete(tempSource, true); } catch { } }
                if (Directory.Exists(tempTarget)) { try { Directory.Delete(tempTarget, true); } catch { } }
            }
        }

        [Fact]
        public void BundledQuickActionsInRepo_ShouldExistAndBeValidXml()
        {
            string repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
            string quickActionsDir = Path.Combine(repoRoot, "Fujify", "Assets", "QuickActions");
            if (!Directory.Exists(quickActionsDir))
            {
                quickActionsDir = Path.Combine(repoRoot, "build_assets", "mac", "QuickActions");
            }

            Assert.True(Directory.Exists(quickActionsDir), $"Missing QuickActions directory at {quickActionsDir}");

            string prevPath = Path.Combine(quickActionsDir, MacQuickActionService.ConvertWithPreviousWorkflowName);
            string optsPath = Path.Combine(quickActionsDir, MacQuickActionService.ConvertWithOptionsWorkflowName);

            Assert.True(Directory.Exists(prevPath), $"Missing {prevPath}");
            Assert.True(Directory.Exists(optsPath), $"Missing {optsPath}");

            string prevInfo = File.ReadAllText(Path.Combine(prevPath, "Contents", "Info.plist"));
            string prevWflow = File.ReadAllText(Path.Combine(prevPath, "Contents", "document.wflow"));
            string optsInfo = File.ReadAllText(Path.Combine(optsPath, "Contents", "Info.plist"));
            string optsWflow = File.ReadAllText(Path.Combine(optsPath, "Contents", "document.wflow"));

            // Validate parsing
            XDocument.Parse(prevInfo);
            XDocument.Parse(prevWflow);
            XDocument.Parse(optsInfo);
            XDocument.Parse(optsWflow);

            Assert.Contains("--convert-previous", prevWflow);
            Assert.Contains("--convert-options", optsWflow);
        }
    }
}
