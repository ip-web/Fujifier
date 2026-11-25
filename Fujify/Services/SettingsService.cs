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
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fujify.Services
{
    public class SettingsService : ISettingsService
    {
        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };

        private readonly object _ioLock = new();
        private readonly string _settingsFilePath;
        private AppSettings _settings;

        public AppSettings Settings => _settings;

        public string SettingsFilePath => _settingsFilePath;

        public SettingsService(string? customPath = null)
        {
            if (!string.IsNullOrEmpty(customPath))
            {
                _settingsFilePath = customPath;
            }
            else
            {
                string folder = GetDefaultSettingsDirectory();
                try { Directory.CreateDirectory(folder); } catch { }
                _settingsFilePath = Path.Combine(folder, "settings.json");
            }

            _settings = new AppSettings();
            Load();
        }

        public static string GetDefaultSettingsDirectory()
        {
            string appData = OperatingSystem.IsIOS() || OperatingSystem.IsAndroid()
                ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
                : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            if (string.IsNullOrWhiteSpace(appData) || !Directory.Exists(appData))
            {
                appData = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }

            if (string.IsNullOrWhiteSpace(appData) || !Directory.Exists(appData))
            {
                appData = AppDomain.CurrentDomain.BaseDirectory;
            }

            return Path.Combine(appData, "Fujifier");
        }

        private IEnumerable<string> GetCandidateSettingsPaths()
        {
            var candidates = new List<string>();

            // 1. Primary path
            candidates.Add(_settingsFilePath);

            // 2. Backup file in the same directory
            candidates.Add(_settingsFilePath + ".bak");

            // 3. Temporary file (in case application was terminated during save)
            candidates.Add(_settingsFilePath + ".tmp");

            // If a custom path was specified for testing, don't query global user directories
            string primaryDir = Path.GetDirectoryName(_settingsFilePath) ?? string.Empty;
            string defaultDir = GetDefaultSettingsDirectory();
            bool isCustomTestPath = !string.IsNullOrEmpty(_settingsFilePath) &&
                                   !primaryDir.StartsWith(defaultDir, StringComparison.OrdinalIgnoreCase) &&
                                   primaryDir.Contains(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

            if (isCustomTestPath)
            {
                return candidates;
            }

            // 4. LocalApplicationData mirror (resilient against roaming profile cleanup or installer elevation)
            try
            {
                string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (!string.IsNullOrWhiteSpace(localApp))
                {
                    candidates.Add(Path.Combine(localApp, "Fujifier", "settings.json"));
                    candidates.Add(Path.Combine(localApp, "Fujifier", "settings.json.bak"));
                }
            }
            catch { }

            // 5. UserProfile hidden directory mirror (~/.fujifier/settings.json)
            try
            {
                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrWhiteSpace(userProfile))
                {
                    candidates.Add(Path.Combine(userProfile, ".fujifier", "settings.json"));
                }
            }
            catch { }

            // 6. Legacy Fujify folder in ApplicationData (Fujify -> Fujifier migration)
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                if (!string.IsNullOrWhiteSpace(appData))
                {
                    candidates.Add(Path.Combine(appData, "Fujify", "settings.json"));
                }
            }
            catch { }

            // 7. Legacy Fujify folder in LocalApplicationData
            try
            {
                string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (!string.IsNullOrWhiteSpace(localApp))
                {
                    candidates.Add(Path.Combine(localApp, "Fujify", "settings.json"));
                }
            }
            catch { }

            // 8. Portable directory next to Fujifier.exe
            try
            {
                string appDir = AppDomain.CurrentDomain.BaseDirectory;
                if (!string.IsNullOrWhiteSpace(appDir))
                {
                    candidates.Add(Path.Combine(appDir, "settings.json"));
                }
            }
            catch { }

            return candidates;
        }

        public void Load()
        {
            lock (_ioLock)
            {
                LoadInternal();
            }
        }

        private void LoadInternal()
        {
            AppSettings? loadedSettings = null;
            string? loadedSourcePath = null;
            bool primaryFileWasCorrupt = false;

            foreach (var candidatePath in GetCandidateSettingsPaths())
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(candidatePath) || !File.Exists(candidatePath))
                    {
                        continue;
                    }

                    var fileInfo = new FileInfo(candidatePath);
                    if (fileInfo.Length == 0)
                    {
                        continue;
                    }

                    string json = File.ReadAllText(candidatePath);
                    if (string.IsNullOrWhiteSpace(json))
                    {
                        continue;
                    }

                    var parsed = JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions);
                    if (parsed != null)
                    {
                        loadedSettings = parsed;
                        loadedSourcePath = candidatePath;
                        break;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SettingsService] Failed to load candidate '{candidatePath}': {ex.Message}");
                    if (string.Equals(candidatePath, _settingsFilePath, StringComparison.OrdinalIgnoreCase))
                    {
                        primaryFileWasCorrupt = true;
                    }
                }
            }

            if (loadedSettings != null)
            {
                _settings = loadedSettings;

                // If loaded from a fallback/backup or legacy path, restore it to the primary path
                if (loadedSourcePath != null && !string.Equals(Path.GetFullPath(loadedSourcePath), Path.GetFullPath(_settingsFilePath), StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        Save();
                    }
                    catch { }
                }
            }
            else
            {
                // Preserve corrupted file for recovery instead of silently wiping it
                if (primaryFileWasCorrupt && File.Exists(_settingsFilePath))
                {
                    try
                    {
                        string corruptBackup = _settingsFilePath + $".corrupt_{DateTime.UtcNow:yyyyMMddHHmmss}";
                        File.Copy(_settingsFilePath, corruptBackup, overwrite: true);
                    }
                    catch { }
                }

                _settings = new AppSettings();
            }
        }

        public void Save()
        {
            lock (_ioLock)
            {
                SaveInternal();
            }
        }

        private void SaveInternal()
        {
            string tempPath = _settingsFilePath + ".tmp";
            try
            {
                string json = JsonSerializer.Serialize(_settings, SerializerOptions);
                string? primaryDir = Path.GetDirectoryName(_settingsFilePath);
                if (!string.IsNullOrEmpty(primaryDir) && !Directory.Exists(primaryDir))
                {
                    Directory.CreateDirectory(primaryDir);
                }

                string bakPath = _settingsFilePath + ".bak";

                // 1. Atomic write to temporary file
                File.WriteAllText(tempPath, json);

                // 2. Backup previous valid settings file
                if (File.Exists(_settingsFilePath))
                {
                    try
                    {
                        var info = new FileInfo(_settingsFilePath);
                        if (info.Length > 0)
                        {
                            File.Copy(_settingsFilePath, bakPath, overwrite: true);
                        }
                    }
                    catch { }
                }

                // 3. Atomically commit temp file to primary settings file
                File.Move(tempPath, _settingsFilePath, overwrite: true);
                RestrictFilePermissionsIfUnix(_settingsFilePath);

                // If custom path (e.g. unit tests in temp directory), do not touch user directories
                bool isCustomTestPath = !string.IsNullOrEmpty(primaryDir) &&
                                       primaryDir.Contains(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

                if (!isCustomTestPath)
                {
                    // Strip transient OAuth bearer tokens from secondary backup mirrors
                    string mirrorJson = CreateRedactedMirrorJson(json);

                    // 4. Mirror to LocalApplicationData
                    try
                    {
                        string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                        if (!string.IsNullOrWhiteSpace(localApp))
                        {
                            string localDir = Path.Combine(localApp, "Fujifier");
                            Directory.CreateDirectory(localDir);
                            string localPath = Path.Combine(localDir, "settings.json");
                            File.WriteAllText(localPath, mirrorJson);
                            RestrictFilePermissionsIfUnix(localPath);
                        }
                    }
                    catch { }

                    // 5. Mirror to UserProfile (~/.fujifier/settings.json)
                    try
                    {
                        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                        if (!string.IsNullOrWhiteSpace(userProfile))
                        {
                            string userDir = Path.Combine(userProfile, ".fujifier");
                            Directory.CreateDirectory(userDir);
                            string userPath = Path.Combine(userDir, "settings.json");
                            File.WriteAllText(userPath, mirrorJson);
                            RestrictFilePermissionsIfUnix(userPath);
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SettingsService] Failed to save settings: {ex.Message}");
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            }
        }

        private static string CreateRedactedMirrorJson(string json)
        {
            try
            {
                var clone = JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions);
                if (clone != null)
                {
                    clone.AccessToken = null;
                    clone.RefreshToken = null;
                    clone.OfflineToken = null;
                    return JsonSerializer.Serialize(clone, SerializerOptions);
                }
            }
            catch { }
            return json;
        }

        private static void RestrictFilePermissionsIfUnix(string filePath)
        {
            if (OperatingSystem.IsWindows()) return;
            try
            {
                File.SetUnixFileMode(filePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            catch { }
        }
    }
}
