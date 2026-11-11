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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Fujify.Common;
using Fujify.Localization;
using Fujify.Services;
using Fujify.Views;

namespace Fujify.ViewModels
{
    public class QuickProcessViewModel : ViewModelBase
    {
        private readonly IRawConverterService _rawConverterService;
        private readonly IRafConverterService _rafConverterService;
        private readonly IMetadataService _metadataService;
        private readonly IDialogService _dialogService;
        private readonly ISettingsService _settingsService;
        private readonly ILicenseService _licenseService;
        private readonly IFileIntegrityService _fileIntegrityService;

        public bool IsPro => _licenseService.IsPro;

        private readonly ConcurrentQueue<string> _incomingQueue = new();
        private readonly List<string> _allFiles = new();
        private readonly List<string> _convertedDngFiles = new();
        private readonly HashSet<string> _reservedOutputPaths = new(StringComparer.OrdinalIgnoreCase);

        private CancellationTokenSource? _cts;
        private bool _started;

        // Visual state: configuring options vs executing progress
        private bool _isConfiguringOptions;

        // Quick settings options
        private bool _isCaptureOneModeSelected;
        private string? _outputFolderPath;
        private bool _backupOriginalFile;
        private bool _verifyFileIntegrity;
        private bool _appendSuffix;
        private string _suffix = "-fujified";
        private bool _createSubfolder;
        private bool _subfolderUseOriginalName = true;
        private string _subfolderSuffix = "-fujified";
        private string _subfolderCustomName = "Fujified";
        private bool _autoOpenInCaptureOne;

        // Progress properties
        private string _title = "Processing with Fujifier";
        private string _subtitle = "Preparing files...";
        private string _statusIcon = "⚡";
        private string _statusIconColor = "#01D701";
        private double _progressValue = 0;
        private bool _isProgressIndeterminate = true;
        private string _progressDetailText = string.Empty;
        private string _progressPercentageText = "0%";
        private bool _isProcessing = true;
        private bool _isCompleted = false;
        private bool _canOpenFolder = false;

        public Window? OwnerWindow { get; set; }

        public bool IsConfiguringOptions
        {
            get => _isConfiguringOptions;
            set => SetProperty(ref _isConfiguringOptions, value);
        }

        public bool IsCaptureOneModeUnlocked => _settingsService.Settings.EnableCaptureOneMode;

        public bool IsCaptureOneModeSelected
        {
            get => _isCaptureOneModeSelected;
            set
            {
                if (SetProperty(ref _isCaptureOneModeSelected, value))
                {
                    OnPropertyChanged(nameof(IsLightroomModeSelected));
                    OnPropertyChanged(nameof(ShowOpenInCaptureOne));
                }
            }
        }

        public bool IsLightroomModeSelected => !IsCaptureOneModeSelected;

        public string? OutputFolderPath
        {
            get => _outputFolderPath;
            set
            {
                if (SetProperty(ref _outputFolderPath, value))
                {
                    OnPropertyChanged(nameof(FormattedOutputFolderPath));
                    OnPropertyChanged(nameof(HasCustomOutputFolder));
                    OnPropertyChanged(nameof(SubfolderPreview));
                }
            }
        }

        public bool HasCustomOutputFolder => !string.IsNullOrWhiteSpace(_outputFolderPath);

        public string FormattedOutputFolderPath =>
            string.IsNullOrWhiteSpace(_outputFolderPath)
                ? (LocalizationManager.Tr("OutputFolderOptional") ?? "(Optional)")
                : _outputFolderPath;

        public bool BackupOriginalFile
        {
            get => _backupOriginalFile;
            set => SetProperty(ref _backupOriginalFile, value);
        }

        public bool VerifyFileIntegrity
        {
            get => _verifyFileIntegrity;
            set => SetProperty(ref _verifyFileIntegrity, value);
        }

        public bool AppendSuffix
        {
            get => _appendSuffix;
            set => SetProperty(ref _appendSuffix, value);
        }

        public string Suffix
        {
            get => _suffix;
            set => SetProperty(ref _suffix, value);
        }

        public bool CreateSubfolder
        {
            get => _createSubfolder;
            set
            {
                if (SetProperty(ref _createSubfolder, value))
                {
                    OnPropertyChanged(nameof(SubfolderPreview));
                }
            }
        }

        public bool SubfolderUseOriginalName
        {
            get => _subfolderUseOriginalName;
            set
            {
                if (SetProperty(ref _subfolderUseOriginalName, value))
                {
                    OnPropertyChanged(nameof(SubfolderPreview));
                }
            }
        }

        public string SubfolderSuffix
        {
            get => _subfolderSuffix;
            set
            {
                if (SetProperty(ref _subfolderSuffix, value))
                {
                    OnPropertyChanged(nameof(SubfolderPreview));
                }
            }
        }

        public string SubfolderCustomName
        {
            get => _subfolderCustomName;
            set
            {
                if (SetProperty(ref _subfolderCustomName, value))
                {
                    OnPropertyChanged(nameof(SubfolderPreview));
                }
            }
        }

        private string GetEffectiveBaseFolderName()
        {
            if (!string.IsNullOrWhiteSpace(_outputFolderPath))
            {
                var name = Path.GetFileName(_outputFolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (!string.IsNullOrWhiteSpace(name))
                {
                    return name;
                }
            }

            string? firstFile = null;
            lock (_allFiles)
            {
                firstFile = _allFiles.FirstOrDefault();
            }
            if (string.IsNullOrWhiteSpace(firstFile) && _incomingQueue.TryPeek(out var queued))
            {
                firstFile = queued;
            }

            if (!string.IsNullOrWhiteSpace(firstFile))
            {
                var dir = Path.GetDirectoryName(firstFile);
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    var name = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        return name;
                    }
                }
            }

            return "Photos";
        }

        public string SubfolderPreview =>
            _subfolderUseOriginalName
                ? $"{GetEffectiveBaseFolderName()}{_subfolderSuffix}"
                : (string.IsNullOrWhiteSpace(_subfolderCustomName) ? "Fujified" : _subfolderCustomName);

        public bool ShowOpenInCaptureOne => IsCaptureOneModeSelected;

        public bool AutoOpenInCaptureOne
        {
            get => _autoOpenInCaptureOne;
            set => SetProperty(ref _autoOpenInCaptureOne, value);
        }

        public string FilesSummaryText
        {
            get
            {
                int count;
                lock (_allFiles)
                {
                    count = _allFiles.Count + _incomingQueue.Count;
                }
                if (count == 0) return "No files selected";
                return count == 1 ? "1 photo selected" : $"{count} photos selected";
            }
        }

        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        public string Subtitle
        {
            get => _subtitle;
            set => SetProperty(ref _subtitle, value);
        }

        public string StatusIcon
        {
            get => _statusIcon;
            set => SetProperty(ref _statusIcon, value);
        }

        public string StatusIconColor
        {
            get => _statusIconColor;
            set => SetProperty(ref _statusIconColor, value);
        }

        public double ProgressValue
        {
            get => _progressValue;
            set => SetProperty(ref _progressValue, value);
        }

        public bool IsProgressIndeterminate
        {
            get => _isProgressIndeterminate;
            set => SetProperty(ref _isProgressIndeterminate, value);
        }

        public string ProgressDetailText
        {
            get => _progressDetailText;
            set => SetProperty(ref _progressDetailText, value);
        }

        public string ProgressPercentageText
        {
            get => _progressPercentageText;
            set => SetProperty(ref _progressPercentageText, value);
        }

        public bool IsProcessing
        {
            get => _isProcessing;
            set => SetProperty(ref _isProcessing, value);
        }

        public bool IsCompleted
        {
            get => _isCompleted;
            set => SetProperty(ref _isCompleted, value);
        }

        public bool CanOpenFolder
        {
            get => _canOpenFolder;
            set => SetProperty(ref _canOpenFolder, value);
        }

        // Commands
        public ICommand SelectLightroomModeCommand { get; }
        public ICommand SelectCaptureOneModeCommand { get; }
        public ICommand BrowseOutputFolderCommand { get; }
        public ICommand ClearOutputFolderCommand { get; }
        public ICommand StartConversionCommand { get; }
        public ICommand CancelOptionsCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand DoneCommand { get; }
        public ICommand OpenFolderCommand { get; }

        public QuickProcessViewModel(
            IEnumerable<string>? initialFiles = null,
            bool showOptions = false,
            IRawConverterService? rawConverterService = null,
            IRafConverterService? rafConverterService = null,
            IMetadataService? metadataService = null,
            IDialogService? dialogService = null,
            ISettingsService? settingsService = null,
            ILicenseService? licenseService = null,
            IFileIntegrityService? fileIntegrityService = null)
        {
            _isConfiguringOptions = showOptions;
            _rawConverterService = rawConverterService ?? new RawConverterService();
            _rafConverterService = rafConverterService ?? new RafConverterService();
            _metadataService = metadataService ?? new MetadataService();
            _settingsService = settingsService ?? new SettingsService();
            _licenseService = licenseService ?? LicenseService.Instance ?? new LicenseService(_settingsService);
            _dialogService = dialogService ?? new DialogService(_settingsService, _licenseService);
            _fileIntegrityService = fileIntegrityService ?? new FileIntegrityService();

            // Load from previous settings saved in app
            var s = _settingsService.Settings;
            _isCaptureOneModeSelected = s.EnableCaptureOneMode && string.Equals(s.ConversionTargetMode, "CaptureOne", StringComparison.OrdinalIgnoreCase);
            _outputFolderPath = s.CustomOutputFolder;
            _backupOriginalFile = s.BackupOriginalFile;
            _verifyFileIntegrity = s.VerifyFileIntegrity;
            _appendSuffix = s.AppendSuffix;
            _suffix = string.IsNullOrWhiteSpace(s.Suffix) ? "-fujified" : s.Suffix;
            _createSubfolder = s.CreateSubfolder;
            _subfolderUseOriginalName = s.SubfolderUseOriginalName;
            _subfolderSuffix = string.IsNullOrWhiteSpace(s.SubfolderSuffix) ? "-fujified" : s.SubfolderSuffix;
            _subfolderCustomName = string.IsNullOrWhiteSpace(s.SubfolderCustomName) ? "Fujified" : s.SubfolderCustomName;
            _autoOpenInCaptureOne = s.AutoOpenInCaptureOne;

            // Initialize Commands
            SelectLightroomModeCommand = new RelayCommand(() => IsCaptureOneModeSelected = false);
            SelectCaptureOneModeCommand = new RelayCommand(() => IsCaptureOneModeSelected = true);
            BrowseOutputFolderCommand = new AsyncRelayCommand(BrowseOutputFolderAsync);
            ClearOutputFolderCommand = new RelayCommand(() => OutputFolderPath = null);
            StartConversionCommand = new AsyncRelayCommand(ExecuteStartConversionAsync);
            CancelOptionsCommand = new RelayCommand(() => OwnerWindow?.Close());
            CancelCommand = new RelayCommand(Cancel);
            DoneCommand = new RelayCommand(Done);
            OpenFolderCommand = new RelayCommand(OpenOutputFolder);

            if (initialFiles != null)
            {
                foreach (var file in initialFiles)
                {
                    if (!string.IsNullOrWhiteSpace(file))
                    {
                        _incomingQueue.Enqueue(file);
                    }
                }
            }
            DrainIncomingQueue();
        }

        public void AddFiles(IEnumerable<string> files)
        {
            if (files == null) return;
            foreach (var file in files)
            {
                if (!string.IsNullOrWhiteSpace(file))
                {
                    _incomingQueue.Enqueue(file);
                }
            }
            DrainIncomingQueue();

            // If processing already completed, restart conversion for late-arriving files
            if (_isCompleted && !_isConfiguringOptions)
            {
                _started = false;
                _isCompleted = false;
                _isProcessing = true;
                _ = StartAsync();
            }
        }

        private void DrainIncomingQueue()
        {
            lock (_allFiles)
            {
                while (_incomingQueue.TryDequeue(out var f))
                {
                    if (File.Exists(f) && !_allFiles.Contains(f, StringComparer.OrdinalIgnoreCase))
                    {
                        _allFiles.Add(f);
                    }
                }
            }
            OnPropertyChanged(nameof(FilesSummaryText));
            OnPropertyChanged(nameof(SubfolderPreview));
        }

        private async Task BrowseOutputFolderAsync()
        {
            string? selected = await _dialogService.ShowFolderBrowserDialogAsync(OwnerWindow, "Select Destination Folder");
            if (!string.IsNullOrWhiteSpace(selected) && Directory.Exists(selected))
            {
                OutputFolderPath = selected;
            }
        }

        private async Task ExecuteStartConversionAsync()
        {
            // Persist the selected quick settings as the latest settings in the app
            var s = _settingsService.Settings;
            s.ConversionTargetMode = IsCaptureOneModeSelected ? "CaptureOne" : "Lightroom";
            s.CustomOutputFolder = HasCustomOutputFolder ? OutputFolderPath : null;
            s.BackupOriginalFile = BackupOriginalFile;
            s.VerifyFileIntegrity = VerifyFileIntegrity;
            s.AppendSuffix = AppendSuffix;
            s.Suffix = Suffix;
            s.CreateSubfolder = CreateSubfolder;
            s.SubfolderUseOriginalName = SubfolderUseOriginalName;
            s.SubfolderSuffix = SubfolderSuffix;
            s.SubfolderCustomName = SubfolderCustomName;
            s.AutoOpenInCaptureOne = AutoOpenInCaptureOne;
            _settingsService.Save();

            IsConfiguringOptions = false;
            await StartAsync();
        }

        public async Task StartAsync()
        {
            if (_started) return;
            _started = true;

            _cts = new CancellationTokenSource();
            var ct = _cts.Token;

            try
            {
                // Buffer a quiet window to batch all incoming Explorer secondary instances
                Subtitle = "Gathering files...";
                int lastCount = -1;
                int stableChecks = 0;
                while (true)
                {
                    DrainIncomingQueue();
                    int currentCount;
                    lock (_allFiles) { currentCount = _allFiles.Count; }

                    if (currentCount != lastCount)
                    {
                        lastCount = currentCount;
                        stableChecks = 0;
                        Subtitle = $"Gathering files... ({lastCount} detected)";
                    }
                    else
                    {
                        stableChecks++;
                        if (stableChecks >= 2) // 2 consecutive checks without change (>= 500ms stable)
                        {
                            break;
                        }
                    }

                    await Task.Delay(250, ct);
                }

                int initialCount;
                lock (_allFiles) { initialCount = _allFiles.Count; }
                if (initialCount == 0)
                {
                    Title = "No Files to Process";
                    Subtitle = "No supported RAW files were provided.";
                    StatusIcon = "⚠";
                    StatusIconColor = "#F39C12";
                    IsProcessing = false;
                    IsCompleted = true;
                    IsProgressIndeterminate = false;
                    return;
                }

                var processedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                int processedCount = 0;
                int successCount = 0;
                int errorCount = 0;

                string? firstFile;
                lock (_allFiles) { firstFile = _allFiles.FirstOrDefault(); }
                int configuredConcurrency = _settingsService.Settings.MaxConcurrentConversions;
                int maxConcurrency = DrivePerformanceHelper.GetOptimalConversionConcurrency(
                    firstFile,
                    configuredConcurrency,
                    _licenseService.IsPro);

                var parallelOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = maxConcurrency,
                    CancellationToken = ct
                };

                string? effectiveOutputFolder = HasCustomOutputFolder ? OutputFolderPath : null;

                while (true)
                {
                    ct.ThrowIfCancellationRequested();
                    DrainIncomingQueue();

                    List<string> currentBatch;
                    lock (_allFiles)
                    {
                        currentBatch = _allFiles.Where(f => !processedFiles.Contains(f)).ToList();
                        if (!_licenseService.IsPro)
                        {
                            int allowedRemaining = _licenseService.MaxFreeQueueSize - processedFiles.Count;
                            if (allowedRemaining <= 0)
                            {
                                break;
                            }
                            currentBatch = currentBatch.Take(allowedRemaining).ToList();
                        }

                        foreach (var f in currentBatch)
                        {
                            processedFiles.Add(f);
                        }
                    }

                    if (currentBatch.Count == 0)
                    {
                        // Pause to check if any final secondary instances arrived
                        await Task.Delay(250, ct);
                        DrainIncomingQueue();
                        lock (_allFiles)
                        {
                            if (_allFiles.Any(f => !processedFiles.Contains(f)))
                            {
                                continue;
                            }
                        }
                        break;
                    }

                    int totalCount;
                    lock (_allFiles) { totalCount = _allFiles.Count; }
                    int totalTarget = _licenseService.IsPro
                        ? totalCount
                        : Math.Min(totalCount, _licenseService.MaxFreeQueueSize);

                    IsProgressIndeterminate = false;
                    ProgressDetailText = $"{processedCount} of {totalTarget} photos processed";
                    Subtitle = $"Processing {totalTarget} photo{(totalTarget == 1 ? "" : "s")}...";

                    await Parallel.ForEachAsync(currentBatch, parallelOptions, async (filePath, token) =>
                    {
                        token.ThrowIfCancellationRequested();

                        string fileName = Path.GetFileName(filePath);
                        Dispatcher.UIThread.Post(() =>
                        {
                            Subtitle = $"Converting: {fileName}";
                        });

                        bool success = false;
                        try
                        {
                            string destPath = await ConvertAndInjectSingleFileAsync(filePath, effectiveOutputFolder, token);
                            lock (_convertedDngFiles)
                            {
                                _convertedDngFiles.Add(destPath);
                            }
                            success = true;
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[QuickProcess] Error on '{filePath}': {ex.Message}");
                        }

                        int current = Interlocked.Increment(ref processedCount);
                        if (success) Interlocked.Increment(ref successCount);
                        else Interlocked.Increment(ref errorCount);

                        int totalFiles;
                        lock (_allFiles) { totalFiles = _allFiles.Count; }
                        int currentTotal = _licenseService.IsPro
                            ? totalFiles
                            : Math.Min(totalFiles, _licenseService.MaxFreeQueueSize);
                        int pct = currentTotal > 0 ? (int)((double)current / currentTotal * 100) : 100;

                        Dispatcher.UIThread.Post(() =>
                        {
                            ProgressValue = pct;
                            ProgressPercentageText = $"{pct}%";
                            ProgressDetailText = $"{current} of {currentTotal} photos processed";
                        });
                    });
                }

                // Completed successfully
                bool hasErrors = errorCount > 0;
                int finalTotal = successCount + errorCount;
                Title = hasErrors ? "Processing Finished with Errors" : "Processing Complete";
                Subtitle = hasErrors
                    ? $"{successCount} succeeded, {errorCount} failed. Enjoy your new Film simulations!"
                    : (finalTotal == 1
                        ? "1 photo converted successfully. Enjoy your new Film simulations!"
                        : $"{finalTotal} photos converted successfully. Enjoy your new Film simulations!");

                StatusIcon = hasErrors ? "⚠" : "✔";
                StatusIconColor = hasErrors ? "#F39C12" : "#01D701";

                string? effectiveFolder = HasCustomOutputFolder && Directory.Exists(OutputFolderPath)
                    ? OutputFolderPath
                    : (_convertedDngFiles.Count > 0 ? Path.GetDirectoryName(_convertedDngFiles[0]) : null);

                CanOpenFolder = !string.IsNullOrEmpty(effectiveFolder) && Directory.Exists(effectiveFolder);

                // Auto-Open in Capture One if enabled
                if (IsCaptureOneModeSelected)
                {
                    if (AutoOpenInCaptureOne && _convertedDngFiles.Count > 0)
                    {
                        CrossPlatformLauncher.OpenInCaptureOne(_convertedDngFiles);
                    }
                }

                // Auto-Open destination folder if enabled in global settings
                if (_settingsService.Settings.AutoOpenOutputFolder && !string.IsNullOrEmpty(effectiveFolder) && Directory.Exists(effectiveFolder))
                {
                    CrossPlatformLauncher.OpenFolder(effectiveFolder);
                }

                // Free Tier Post-Conversion 15s Delay Modal
                if (!_licenseService.IsPro)
                {
                    await FreeTierDelayDialog.ShowDelayDialogAsync(OwnerWindow, _licenseService, isPostConversion: true);
                }
            }
            catch (OperationCanceledException)
            {
                Title = "Processing Cancelled";
                Subtitle = "Conversion was stopped by the user.";
                StatusIcon = "⏹";
                StatusIconColor = "#A0A0AC";
            }
            catch (Exception ex)
            {
                Title = "Error Occurred";
                Subtitle = ex.Message;
                StatusIcon = "⚠";
                StatusIconColor = "#E74C3C";
            }
            finally
            {
                IsProcessing = false;
                IsCompleted = true;
                IsProgressIndeterminate = false;
                _cts?.Dispose();
                _cts = null;
            }
        }

        private async Task<string> ConvertAndInjectSingleFileAsync(string filePath, string? outputFolder, CancellationToken ct)
        {
            bool isCaptureOneMode = IsCaptureOneModeSelected;
            string targetExt = isCaptureOneMode ? ".raf" : ".dng";
            bool isOriginallyTarget = filePath.EndsWith(targetExt, StringComparison.OrdinalIgnoreCase);
            bool isOriginallyDng = filePath.EndsWith(".dng", StringComparison.OrdinalIgnoreCase);
            bool isOriginallyRaf = filePath.EndsWith(".raf", StringComparison.OrdinalIgnoreCase);

            string baseFileName = Path.GetFileNameWithoutExtension(filePath);
            bool appendSuffix = AppendSuffix && !string.IsNullOrWhiteSpace(Suffix);
            if (appendSuffix)
            {
                baseFileName += Suffix.Trim();
            }

            var snapshotSettings = new AppSettings
            {
                AppendSuffix = AppendSuffix,
                Suffix = Suffix,
                CreateSubfolder = CreateSubfolder,
                SubfolderUseOriginalName = SubfolderUseOriginalName,
                SubfolderSuffix = SubfolderSuffix,
                SubfolderCustomName = SubfolderCustomName
            };

            string initialDestFilePath = OutputLocationHelper.ComputeInitialDestinationFilePath(
                filePath,
                outputFolder,
                snapshotSettings,
                targetExt);

            string inputDir = Path.GetDirectoryName(filePath) ?? string.Empty;
            string outputDir = Path.GetDirectoryName(initialDestFilePath) ?? string.Empty;
            bool isSameFolder = string.Equals(Path.GetFullPath(inputDir), Path.GetFullPath(outputDir), StringComparison.OrdinalIgnoreCase);
            bool isExactSameFilePath = isOriginallyTarget && string.Equals(Path.GetFullPath(filePath), Path.GetFullPath(initialDestFilePath), StringComparison.OrdinalIgnoreCase);
            bool createBackup = BackupOriginalFile;

            string finalDestFilePath = initialDestFilePath;

            lock (_reservedOutputPaths)
            {
                if (!isExactSameFilePath && !(isOriginallyTarget && isSameFolder && createBackup) && (File.Exists(initialDestFilePath) || _reservedOutputPaths.Contains(initialDestFilePath)))
                {
                    finalDestFilePath = GetUniqueFilePath(initialDestFilePath, _reservedOutputPaths);
                }
                else
                {
                    _reservedOutputPaths.Add(initialDestFilePath);
                }
            }

            if (isCaptureOneMode)
            {
                // 1. If input file is ALREADY a Fujifilm RAF file, patch camera model
                if (isOriginallyRaf)
                {
                    bool isBayer = _rafConverterService.IsBayerSensor(filePath);
                    string targetModel = isBayer ? "GFX50S II" : "X-T5";

                    string? patchDestDir = Path.GetDirectoryName(finalDestFilePath);
                    if (!string.IsNullOrEmpty(patchDestDir) && !Directory.Exists(patchDestDir))
                    {
                        Directory.CreateDirectory(patchDestDir);
                    }

                    string sourceForPatch = filePath;
                    if (isExactSameFilePath && createBackup && isSameFolder)
                    {
                        string origBaseName = Path.GetFileNameWithoutExtension(filePath);
                        string origBackup = Path.Combine(inputDir, $"{origBaseName}ORIG.RAF");
                        if (File.Exists(origBackup)) File.Delete(origBackup);
                        File.Move(filePath, origBackup);
                        sourceForPatch = origBackup;
                    }

                    var patchResult = await _rafConverterService.PatchModelAsync(
                        sourceForPatch,
                        finalDestFilePath,
                        targetModel,
                        ct);

                    if (!patchResult.Success)
                    {
                        throw new InvalidOperationException(patchResult.ErrorMessage ?? "RAF model patch failed");
                    }

                    if (_settingsService.Settings.VerifyFileIntegrity || _verifyFileIntegrity)
                    {
                        var integrity = await _fileIntegrityService.VerifyIntegrityAsync(finalDestFilePath, ct);
                        if (!integrity.IsValid)
                        {
                            string err = $"File integrity check failed: {integrity.ErrorMessage}";
                            if (File.Exists(finalDestFilePath) && !string.Equals(Path.GetFullPath(sourceForPatch), Path.GetFullPath(finalDestFilePath), StringComparison.OrdinalIgnoreCase))
                            {
                                try { File.Delete(finalDestFilePath); } catch { }
                            }
                            if (isExactSameFilePath && createBackup && isSameFolder && File.Exists(sourceForPatch) && !File.Exists(filePath))
                            {
                                try { File.Move(sourceForPatch, filePath); } catch { }
                            }
                            throw new InvalidOperationException(err);
                        }
                    }

                    if (!createBackup && !string.Equals(Path.GetFullPath(sourceForPatch), Path.GetFullPath(finalDestFilePath), StringComparison.OrdinalIgnoreCase))
                    {
                        FileTrashHelper.SendToTrash(sourceForPatch);
                    }

                    return finalDestFilePath;
                }

                // 2. Repack RAW into Fujifilm RAF container
                string? containerPath = _settingsService.Settings.RafContainerPath;
                string resolvedContainer = _rafConverterService.ResolveContainerArgument(containerPath);
                if (string.IsNullOrWhiteSpace(resolvedContainer) || !_rafConverterService.HasValidContainer(resolvedContainer))
                {
                    throw new InvalidOperationException(LocalizationManager.Tr("MissingRafContainerError"));
                }

                string? destDir = Path.GetDirectoryName(finalDestFilePath);
                if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                {
                    Directory.CreateDirectory(destDir);
                }

                var convResult = await _rafConverterService.ConvertToRafAsync(
                    filePath,
                    finalDestFilePath,
                    resolvedContainer,
                    null,
                    ct);

                if (!convResult.Success || string.IsNullOrEmpty(convResult.OutputRafPath))
                {
                    throw new InvalidOperationException(convResult.ErrorMessage ?? "Conversion to RAF failed");
                }

                if (_settingsService.Settings.VerifyFileIntegrity || _verifyFileIntegrity)
                {
                    var integrity = await _fileIntegrityService.VerifyIntegrityAsync(finalDestFilePath, ct);
                    if (!integrity.IsValid)
                    {
                        string err = $"File integrity check failed: {integrity.ErrorMessage}";
                        if (File.Exists(finalDestFilePath) && !string.Equals(Path.GetFullPath(filePath), Path.GetFullPath(finalDestFilePath), StringComparison.OrdinalIgnoreCase))
                        {
                            try { File.Delete(finalDestFilePath); } catch { }
                        }
                        throw new InvalidOperationException(err);
                    }
                }

                if (!createBackup)
                {
                    FileTrashHelper.SendToTrash(filePath);
                }

                return finalDestFilePath;
            }
            else
            {
                // Lightroom (DNG) Mode
                string? dngFilePath = filePath;
                string? tempInPlaceOutputFile = null;
                string? origBackupPath = null;

                if (isOriginallyDng)
                {
                    if (createBackup && isSameFolder)
                    {
                        string origBaseName = Path.GetFileNameWithoutExtension(filePath);
                        origBackupPath = Path.Combine(inputDir, $"{origBaseName}ORIG.DNG");
                        if (File.Exists(origBackupPath))
                        {
                            File.Delete(origBackupPath);
                        }
                        File.Move(filePath, origBackupPath);
                        dngFilePath = origBackupPath;
                    }
                    else if (!createBackup && isExactSameFilePath)
                    {
                        string origBaseName = Path.GetFileNameWithoutExtension(filePath);
                        tempInPlaceOutputFile = Path.Combine(outputDir, $"{origBaseName}_tmp_{Guid.NewGuid():N}.dng");
                    }
                }

                try
                {
                    if (!isOriginallyDng)
                    {
                        bool embedOriginalRaw = _settingsService.Settings.EmbedOriginalRaw;

                        // Ensure destination directory exists directly before converting
                        string? targetDir = Path.GetDirectoryName(finalDestFilePath);
                        if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
                        {
                            Directory.CreateDirectory(targetDir);
                        }

                        // Convert directly into final destination path (no in-place temp files)
                        string targetDngForConversion = finalDestFilePath;

                        bool useLossless = _licenseService.IsPro && _settingsService.Settings.LosslessCompression;
                        var convResult = await Task.Run(() => _rawConverterService.ConvertToDng(
                            filePath,
                            embedOriginalRaw,
                            targetDngForConversion,
                            useLossless), ct);

                        if (!convResult.Success || string.IsNullOrEmpty(convResult.OutputDngPath))
                        {
                            throw new InvalidOperationException(convResult.ErrorMessage ?? "Conversion failed");
                        }

                        dngFilePath = convResult.OutputDngPath;
                    }

                    string? customOutputPath = tempInPlaceOutputFile ?? ((!string.IsNullOrEmpty(outputFolder) || finalDestFilePath != dngFilePath)
                        ? finalDestFilePath
                        : null);

                    if (!string.IsNullOrEmpty(customOutputPath))
                    {
                        string? destDir = Path.GetDirectoryName(customOutputPath);
                        if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                        {
                            Directory.CreateDirectory(destDir);
                        }
                    }

                    var metaResult = await Task.Run(() => _metadataService.InjectFujifilmMetadata(
                        dngFilePath,
                        outputFolder,
                        createBackup: false,
                        isOriginallyDng: isOriginallyDng,
                        customOutputFilePath: customOutputPath,
                        targetUniqueCameraModel: "Fujifilm X-T5",
                        sourceRawFilePath: filePath), ct);

                    if (!metaResult.Success)
                    {
                        if (!isOriginallyDng && !string.IsNullOrEmpty(finalDestFilePath) && File.Exists(finalDestFilePath))
                        {
                            try { File.Delete(finalDestFilePath); } catch { }
                        }
                        if (!string.IsNullOrEmpty(tempInPlaceOutputFile) && File.Exists(tempInPlaceOutputFile))
                        {
                            try { File.Delete(tempInPlaceOutputFile); } catch { }
                        }
                        if (isOriginallyDng && createBackup && isSameFolder && !string.IsNullOrEmpty(origBackupPath) && File.Exists(origBackupPath) && !File.Exists(filePath))
                        {
                            try { File.Move(origBackupPath, filePath); } catch { }
                        }
                        throw new InvalidOperationException(metaResult.ErrorMessage ?? "Error injecting Fujifilm metadata");
                    }

                    string targetToVerify = !string.IsNullOrEmpty(tempInPlaceOutputFile)
                        ? tempInPlaceOutputFile
                        : finalDestFilePath;

                    if (_settingsService.Settings.VerifyFileIntegrity || _verifyFileIntegrity)
                    {
                        var integrity = await _fileIntegrityService.VerifyIntegrityAsync(targetToVerify, ct);
                        if (!integrity.IsValid)
                        {
                            string err = $"File integrity check failed: {integrity.ErrorMessage}";
                            if (!string.IsNullOrEmpty(tempInPlaceOutputFile) && File.Exists(tempInPlaceOutputFile))
                            {
                                try { File.Delete(tempInPlaceOutputFile); } catch { }
                            }
                            if (!isOriginallyDng && !string.IsNullOrEmpty(finalDestFilePath) && File.Exists(finalDestFilePath))
                            {
                                try { File.Delete(finalDestFilePath); } catch { }
                            }
                            if (isOriginallyDng && createBackup && isSameFolder && !string.IsNullOrEmpty(origBackupPath) && File.Exists(origBackupPath) && !File.Exists(filePath))
                            {
                                try { File.Move(origBackupPath, filePath); } catch { }
                            }
                            throw new InvalidOperationException(err);
                        }
                    }

                    if (!string.IsNullOrEmpty(tempInPlaceOutputFile))
                    {
                        FileTrashHelper.SendToTrash(filePath);
                        File.Move(tempInPlaceOutputFile, finalDestFilePath, overwrite: true);
                    }
                    else if (!createBackup)
                    {
                        FileTrashHelper.SendToTrash(filePath);

                        // Also trash original sidecar if it was copied or embedded and differs from the final output sidecar
                        string? origSidecar = DngMetadataEditor.FindSidecarXmp(filePath);
                        if (!string.IsNullOrEmpty(origSidecar) && File.Exists(origSidecar))
                        {
                            string targetSidecar = Path.ChangeExtension(finalDestFilePath, ".xmp");
                            if (!string.Equals(Path.GetFullPath(origSidecar), Path.GetFullPath(targetSidecar), StringComparison.OrdinalIgnoreCase))
                            {
                                FileTrashHelper.SendToTrash(origSidecar);
                            }
                        }
                    }

                    return finalDestFilePath;
                }
                catch
                {
                    if (!isOriginallyDng && !string.IsNullOrEmpty(finalDestFilePath) && File.Exists(finalDestFilePath))
                    {
                        try { File.Delete(finalDestFilePath); } catch { }
                    }
                    if (!string.IsNullOrEmpty(tempInPlaceOutputFile) && File.Exists(tempInPlaceOutputFile))
                    {
                        try { File.Delete(tempInPlaceOutputFile); } catch { }
                    }
                    if (isOriginallyDng && createBackup && isSameFolder && !string.IsNullOrEmpty(origBackupPath) && File.Exists(origBackupPath) && !File.Exists(filePath))
                    {
                        try { File.Move(origBackupPath, filePath); } catch { }
                    }
                    throw;
                }
            }
        }

        private static string GetUniqueFilePath(string filePath, HashSet<string>? reservedPaths = null)
        {
            string directory = Path.GetDirectoryName(filePath) ?? string.Empty;
            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);
            string extension = Path.GetExtension(filePath);

            int counter = 1;
            string newFilePath;
            do
            {
                newFilePath = Path.Combine(directory, $"{fileNameWithoutExtension} ({counter}){extension}");
                counter++;
            } while (File.Exists(newFilePath) || (reservedPaths != null && reservedPaths.Contains(newFilePath)));

            reservedPaths?.Add(newFilePath);
            return newFilePath;
        }

        private void Cancel()
        {
            if (IsProcessing)
            {
                _cts?.Cancel();
                Subtitle = "Cancelling processing...";
            }
        }

        private void Done()
        {
            OwnerWindow?.Close();
        }

        private void OpenOutputFolder()
        {
            string? folder = HasCustomOutputFolder && Directory.Exists(OutputFolderPath)
                ? OutputFolderPath
                : (_convertedDngFiles.Count > 0 ? Path.GetDirectoryName(_convertedDngFiles[0]) : null);

            if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
            {
                CrossPlatformLauncher.OpenFolder(folder);
            }
        }
    }
}
