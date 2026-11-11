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
using System.Diagnostics;
using System.IO;
using System.Security;

namespace Fujify.Services
{
    public static class MacQuickActionService
    {
        public const string ConvertWithPreviousWorkflowName = "Convert with previous (Fujifier).workflow";
        public const string ConvertWithOptionsWorkflowName = "Convert with options (Fujifier).workflow";

        public static bool IsSupported => OperatingSystem.IsMacOS();

        /// <summary>
        /// Gets the macOS user Services directory (~/Library/Services), or a custom path for unit testing.
        /// </summary>
        public static string GetServicesDirectory(string? customBasePath = null)
        {
            if (!string.IsNullOrWhiteSpace(customBasePath))
            {
                return customBasePath;
            }

            string userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(userHome))
            {
                userHome = Environment.GetEnvironmentVariable("HOME") ?? "/";
            }

            return Path.Combine(userHome, "Library", "Services");
        }

        /// <summary>
        /// Checks whether the Fujifier Quick Action workflows are currently installed in the Services directory.
        /// </summary>
        public static bool AreQuickActionsRegistered(string? customServicesDir = null)
        {
            try
            {
                string servicesDir = GetServicesDirectory(customServicesDir);
                string prevWorkflow = Path.Combine(servicesDir, ConvertWithPreviousWorkflowName);
                string optsWorkflow = Path.Combine(servicesDir, ConvertWithOptionsWorkflowName);

                return Directory.Exists(prevWorkflow) &&
                       File.Exists(Path.Combine(prevWorkflow, "Contents", "Info.plist")) &&
                       File.Exists(Path.Combine(prevWorkflow, "Contents", "document.wflow")) &&
                       Directory.Exists(optsWorkflow) &&
                       File.Exists(Path.Combine(optsWorkflow, "Contents", "Info.plist")) &&
                       File.Exists(Path.Combine(optsWorkflow, "Contents", "document.wflow"));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MacQuickActionService] Error checking registration: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Registers (installs) the Quick Action workflows into ~/Library/Services/.
        /// Copies pre-bundled workflows from Resources/QuickActions if present, or dynamically generates them.
        /// </summary>
        public static bool RegisterQuickActions(string? customServicesDir = null, string? customSourceDir = null)
        {
            try
            {
                string servicesDir = GetServicesDirectory(customServicesDir);
                Directory.CreateDirectory(servicesDir);

                // Check for bundled workflows in App bundle Resources
                string sourceDir = customSourceDir ?? Path.Combine(AppContext.BaseDirectory, "..", "Resources", "QuickActions");
                bool copiedFromBundle = false;

                if (Directory.Exists(sourceDir))
                {
                    string prevSource = Path.Combine(sourceDir, ConvertWithPreviousWorkflowName);
                    string optsSource = Path.Combine(sourceDir, ConvertWithOptionsWorkflowName);

                    if (Directory.Exists(prevSource) && Directory.Exists(optsSource))
                    {
                        CopyDirectory(prevSource, Path.Combine(servicesDir, ConvertWithPreviousWorkflowName));
                        CopyDirectory(optsSource, Path.Combine(servicesDir, ConvertWithOptionsWorkflowName));
                        copiedFromBundle = true;
                    }
                }

                if (!copiedFromBundle)
                {
                    // Dynamically generate the workflow bundles
                    GenerateWorkflowBundle(
                        servicesDir,
                        ConvertWithPreviousWorkflowName,
                        "Convert with previous (Fujifier)",
                        "dev.ipweb.fujifier.quickaction.convert-previous",
                        "--convert-previous");

                    GenerateWorkflowBundle(
                        servicesDir,
                        ConvertWithOptionsWorkflowName,
                        "Convert with options (Fujifier)",
                        "dev.ipweb.fujifier.quickaction.convert-options",
                        "--convert-options");
                }

                if (OperatingSystem.IsMacOS() && customServicesDir == null)
                {
                    FlushServicesCache();
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MacQuickActionService] Error registering quick actions: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Unregisters (removes) the Quick Action workflows from ~/Library/Services/.
        /// </summary>
        public static bool UnregisterQuickActions(string? customServicesDir = null)
        {
            try
            {
                string servicesDir = GetServicesDirectory(customServicesDir);
                if (!Directory.Exists(servicesDir)) return true;

                string prevWorkflow = Path.Combine(servicesDir, ConvertWithPreviousWorkflowName);
                if (Directory.Exists(prevWorkflow))
                {
                    Directory.Delete(prevWorkflow, recursive: true);
                }

                string optsWorkflow = Path.Combine(servicesDir, ConvertWithOptionsWorkflowName);
                if (Directory.Exists(optsWorkflow))
                {
                    Directory.Delete(optsWorkflow, recursive: true);
                }

                if (OperatingSystem.IsMacOS() && customServicesDir == null)
                {
                    FlushServicesCache();
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MacQuickActionService] Error unregistering quick actions: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Generates a valid macOS Automator Quick Action (.workflow) bundle.
        /// </summary>
        public static void GenerateWorkflowBundle(
            string targetDir,
            string workflowName,
            string displayName,
            string bundleIdentifier,
            string cliFlag)
        {
            string workflowPath = Path.Combine(targetDir, workflowName);
            string contentsPath = Path.Combine(workflowPath, "Contents");
            Directory.CreateDirectory(contentsPath);

            string infoPlistContent = GetInfoPlistContent(displayName, bundleIdentifier);
            File.WriteAllText(Path.Combine(contentsPath, "Info.plist"), infoPlistContent);

            string documentWflowContent = GetDocumentWflowContent(cliFlag);
            File.WriteAllText(Path.Combine(contentsPath, "document.wflow"), documentWflowContent);
        }

        public static string GetInfoPlistContent(string displayName, string bundleIdentifier)
        {
            string safeDisplayName = SecurityElement.Escape(displayName) ?? displayName;
            string safeBundleId = SecurityElement.Escape(bundleIdentifier) ?? bundleIdentifier;

            return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<!DOCTYPE plist PUBLIC ""-//Apple//DTD PLIST 1.0//EN"" ""http://www.apple.com/DTDs/PropertyList-1.0.dtd"">
<plist version=""1.0"">
<dict>
	<key>CFBundleIdentifier</key>
	<string>{safeBundleId}</string>
	<key>CFBundleName</key>
	<string>{safeDisplayName}</string>
	<key>CFBundleShortVersionString</key>
	<string>2.2.0</string>
	<key>NSServices</key>
	<array>
		<dict>
			<key>NSBackgroundColorName</key>
			<string>background</string>
			<key>NSIconName</key>
			<string>NSActionTemplate</string>
			<key>NSMenuItem</key>
			<dict>
				<key>default</key>
				<string>{safeDisplayName}</string>
			</dict>
			<key>NSMessage</key>
			<string>runWorkflowAsService</string>
			<key>NSSendFileTypes</key>
			<array>
				<string>public.camera-raw-image</string>
				<string>com.adobe.raw-image</string>
			</array>
		</dict>
	</array>
</dict>
</plist>";
        }

        public static string GetDocumentWflowContent(string cliFlag)
        {
            string shellScript = $@"#!/bin/zsh

# 1. Search for Fujifier via Spotlight / LaunchServices metadata
APP_BIN=""""
FOUND_APP=$(mdfind 'kMDItemCFBundleIdentifier == ""dev.ipweb.fujifier""' 2>/dev/null | head -n 1)

if [ -n ""$FOUND_APP"" ] && [ -x ""$FOUND_APP/Contents/MacOS/Fujifier"" ]; then
    APP_BIN=""$FOUND_APP/Contents/MacOS/Fujifier""
fi

# 2. Check standard application locations
if [ -z ""$APP_BIN"" ]; then
    if [ -x ""/Applications/Fujifier.app/Contents/MacOS/Fujifier"" ]; then
        APP_BIN=""/Applications/Fujifier.app/Contents/MacOS/Fujifier""
    elif [ -x ""$HOME/Applications/Fujifier.app/Contents/MacOS/Fujifier"" ]; then
        APP_BIN=""$HOME/Applications/Fujifier.app/Contents/MacOS/Fujifier""
    elif [ -x ""/Applications/Fujifier-Intel.app/Contents/MacOS/Fujifier"" ]; then
        APP_BIN=""/Applications/Fujifier-Intel.app/Contents/MacOS/Fujifier""
    elif [ -x ""$HOME/Applications/Fujifier-Intel.app/Contents/MacOS/Fujifier"" ]; then
        APP_BIN=""$HOME/Applications/Fujifier-Intel.app/Contents/MacOS/Fujifier""
    fi
fi

if [ -n ""$APP_BIN"" ]; then
    exec ""$APP_BIN"" {cliFlag} ""$@""
else
    exec open -a ""Fujifier"" --args {cliFlag} ""$@""
fi";

            string escapedScript = SecurityElement.Escape(shellScript) ?? shellScript;

            return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<!DOCTYPE plist PUBLIC ""-//Apple//DTD PLIST 1.0//EN"" ""http://www.apple.com/DTDs/PropertyList-1.0.dtd"">
<plist version=""1.0"">
<dict>
	<key>AMApplicationBuild</key>
	<string>523</string>
	<key>AMApplicationVersion</key>
	<string>2.10</string>
	<key>AMDocumentVersion</key>
	<string>2</string>
	<key>actions</key>
	<array>
		<dict>
			<key>action</key>
			<dict>
				<key>AMAccepts</key>
				<dict>
					<key>Container</key>
					<string>List</string>
					<key>Optional</key>
					<true/>
					<key>Types</key>
					<array>
						<string>com.apple.cocoa.path</string>
					</array>
				</dict>
				<key>AMActionVersion</key>
				<string>2.0.3</string>
				<key>AMApplication</key>
				<array>
					<string>Automator</string>
				</array>
				<key>AMParameterProperties</key>
				<dict>
					<key>COMMAND_STRING</key>
					<dict/>
					<key>CheckedForUserDefaultShell</key>
					<dict/>
					<key>inputMethod</key>
					<dict/>
					<key>shell</key>
					<dict/>
					<key>source</key>
					<dict/>
				</dict>
				<key>AMProvides</key>
				<dict>
					<key>Container</key>
					<string>List</string>
					<key>Types</key>
					<array>
						<string>com.apple.cocoa.path</string>
					</array>
				</dict>
				<key>ActionBundlePath</key>
				<string>/System/Library/Automator/Run Shell Script.action</string>
				<key>ActionName</key>
				<string>Run Shell Script</string>
				<key>ActionParameters</key>
				<dict>
					<key>COMMAND_STRING</key>
					<string>{escapedScript}</string>
					<key>CheckedForUserDefaultShell</key>
					<true/>
					<key>inputMethod</key>
					<integer>1</integer>
					<key>shell</key>
					<string>/bin/zsh</string>
					<key>source</key>
					<string></string>
				</dict>
				<key>BundleIdentifier</key>
				<string>com.apple.RunShellScript</string>
				<key>CFBundleVersion</key>
				<string>2.0.3</string>
				<key>CanShowSelectedItemsWhenRun</key>
				<false/>
				<key>CanShowWhenRun</key>
				<true/>
				<key>Category</key>
				<array>
					<string>AMCategoryUtilities</string>
				</array>
				<key>Class Name</key>
				<string>RunShellScriptAction</string>
				<key>InputUUID</key>
				<string>9F01B347-1FE9-4B52-870E-DE492723A65C</string>
				<key>Keywords</key>
				<array>
					<string>Shell</string>
					<string>Script</string>
					<string>Command</string>
					<string>Run</string>
					<string>Unix</string>
				</array>
				<key>OutputUUID</key>
				<string>5DB741BA-CD24-4B39-95E2-F4B65B5B4CF2</string>
				<key>UUID</key>
				<string>1A6F2423-D5BE-4458-9A78-8314F2309C15</string>
				<key>UnlocalizedApplications</key>
				<array>
					<string>Automator</string>
				</array>
				<key>arguments</key>
				<dict>
					<key>0</key>
					<dict>
						<key>default value</key>
						<integer>0</integer>
						<key>name</key>
						<string>inputMethod</string>
						<key>required</key>
						<string>0</string>
						<key>type</key>
						<string>0</string>
						<key>value</key>
						<integer>1</integer>
					</dict>
					<key>1</key>
					<dict>
						<key>default value</key>
						<string></string>
						<key>name</key>
						<string>source</string>
						<key>required</key>
						<string>0</string>
						<key>type</key>
						<string>0</string>
						<key>value</key>
						<string></string>
					</dict>
					<key>2</key>
					<dict>
						<key>default value</key>
						<false/>
						<key>name</key>
						<string>CheckedForUserDefaultShell</string>
						<key>required</key>
						<string>0</string>
						<key>type</key>
						<string>0</string>
						<key>value</key>
						<true/>
					</dict>
					<key>3</key>
					<dict>
						<key>default value</key>
						<string></string>
						<key>name</key>
						<string>COMMAND_STRING</string>
						<key>required</key>
						<string>0</string>
						<key>type</key>
						<string>0</string>
						<key>value</key>
						<string>{escapedScript}</string>
					</dict>
					<key>4</key>
					<dict>
						<key>default value</key>
						<string>/bin/sh</string>
						<key>name</key>
						<string>shell</string>
						<key>required</key>
						<string>0</string>
						<key>type</key>
						<string>0</string>
						<key>value</key>
						<string>/bin/zsh</string>
					</dict>
				</dict>
				<key>isViewVisible</key>
				<integer>1</integer>
				<key>location</key>
				<string>449.000000:305.000000</string>
				<key>nibPath</key>
				<string>/System/Library/Automator/Run Shell Script.action/Contents/Resources/Base.lproj/main.nib</string>
			</dict>
			<key>isViewVisible</key>
			<integer>1</integer>
		</dict>
	</array>
	<key>connectors</key>
	<dict/>
	<key>workflowMetaData</key>
	<dict>
		<key>applicationBundleIDsByPath</key>
		<dict/>
		<key>applicationPaths</key>
		<array/>
		<key>inputTypeIdentifier</key>
		<string>com.apple.Automator.fileSystemObject.image</string>
		<key>outputTypeIdentifier</key>
		<string>com.apple.Automator.nothing</string>
		<key>presentationMode</key>
		<integer>15</integer>
		<key>processesInput</key>
		<false/>
		<key>serviceApplicationBundleID</key>
		<string>com.apple.finder</string>
		<key>serviceApplicationPath</key>
		<string>/System/Library/CoreServices/Finder.app</string>
		<key>serviceInputTypeIdentifier</key>
		<string>com.apple.Automator.fileSystemObject.image</string>
		<key>serviceOutputTypeIdentifier</key>
		<string>com.apple.Automator.nothing</string>
		<key>serviceProcessesInput</key>
		<false/>
		<key>systemImageName</key>
		<string>NSTouchBarColorPickerFont</string>
		<key>useAutomaticInputType</key>
		<false/>
		<key>workflowTypeIdentifier</key>
		<string>com.apple.Automator.servicesMenu</string>
	</dict>
</dict>
</plist>";
        }

        private static void FlushServicesCache()
        {
            try
            {
                // CoreServices pbs updates the pasteboard and services cache
                string pbsPath = "/System/Library/CoreServices/pbs";
                if (File.Exists(pbsPath))
                {
                    using var p = Process.Start(new ProcessStartInfo
                    {
                        FileName = pbsPath,
                        Arguments = "-update",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                    p?.WaitForExit(2000);
                }
            }
            catch { }
        }

        private static void CopyDirectory(string sourceDir, string destinationDir)
        {
            Directory.CreateDirectory(destinationDir);

            foreach (string file in Directory.GetFiles(sourceDir))
            {
                string destFile = Path.Combine(destinationDir, Path.GetFileName(file));
                File.Copy(file, destFile, overwrite: true);
            }

            foreach (string subDir in Directory.GetDirectories(sourceDir))
            {
                string destSubDir = Path.Combine(destinationDir, Path.GetFileName(subDir));
                CopyDirectory(subDir, destSubDir);
            }
        }
    }
}

