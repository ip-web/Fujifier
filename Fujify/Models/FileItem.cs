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
using System.ComponentModel;
using System.IO;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Fujify.Localization;

namespace Fujify.Models
{
    public enum FileProcessingState
    {
        Pending,
        Processing,
        Completed,
        Skipped,
        Error
    }

    public class FileItem : INotifyPropertyChanged, IDisposable
    {
        private string _filePath = string.Empty;
        private string _status = "Pending";
        private FileProcessingState _processingState = FileProcessingState.Pending;
        private string? _errorMessage;
        private Bitmap? _thumbnail;
        private string _cameraMakeAndModel = string.Empty;

        public bool HasAnimationPlayed { get; set; } = false;
        public System.Windows.Input.ICommand? RemoveCommand { get; set; }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged(nameof(IsSelected));
                }
            }
        }

        public required string FilePath
        {
            get => _filePath;
            set
            {
                if (_filePath != value)
                {
                    _filePath = value;
                    OnPropertyChanged(nameof(FilePath));
                    OnPropertyChanged(nameof(FileName));
                }
            }
        }

        public string FileName => Path.GetFileName(_filePath);

        public FileProcessingState ProcessingState
        {
            get => _processingState;
            private set
            {
                if (_processingState != value)
                {
                    _processingState = value;
                    OnPropertyChanged(nameof(ProcessingState));
                }
            }
        }

        public void SetStatus(string status, FileProcessingState explicitState, string? errorMessage = null)
        {
            _processingState = explicitState;
            if (!string.IsNullOrEmpty(errorMessage))
            {
                ErrorMessage = errorMessage;
            }
            if (_status != status)
            {
                _status = status;
                RaiseStatusNotifications();
            }
            else
            {
                OnPropertyChanged(nameof(ProcessingState));
                RaiseStatusNotifications();
            }
        }

        public string Status
        {
            get => _status;
            set
            {
                if (_status != value)
                {
                    _status = value;
                    _processingState = InferProcessingState(value);
                    OnPropertyChanged(nameof(ProcessingState));
                    RaiseStatusNotifications();
                }
            }
        }

        private void RaiseStatusNotifications()
        {
            OnPropertyChanged(nameof(Status));
            OnPropertyChanged(nameof(LocalizedStatus));
            OnPropertyChanged(nameof(StatusIcon));
            OnPropertyChanged(nameof(StatusColor));
            OnPropertyChanged(nameof(IsCompleted));
            OnPropertyChanged(nameof(IsError));
            OnPropertyChanged(nameof(IsActive));
            OnPropertyChanged(nameof(ErrorBackground));
            OnPropertyChanged(nameof(ErrorBorderBrush));
            OnPropertyChanged(nameof(ErrorBorderThickness));
        }

        public static FileProcessingState InferProcessingState(string? status)
        {
            if (string.IsNullOrWhiteSpace(status) || status.Equals("Pending", StringComparison.OrdinalIgnoreCase))
            {
                return FileProcessingState.Pending;
            }
            if (status == "✔" || status == "✓" ||
                status.Contains("Done", StringComparison.OrdinalIgnoreCase) ||
                status.Contains("Complete", StringComparison.OrdinalIgnoreCase) ||
                status.Contains("successful", StringComparison.OrdinalIgnoreCase))
            {
                return FileProcessingState.Completed;
            }
            if (status.StartsWith("Skipped", StringComparison.OrdinalIgnoreCase))
            {
                return FileProcessingState.Skipped;
            }
            if (status.Contains("Error", StringComparison.OrdinalIgnoreCase) ||
                status.Contains("Fail", StringComparison.OrdinalIgnoreCase) ||
                status.Contains("Invalid", StringComparison.OrdinalIgnoreCase))
            {
                return FileProcessingState.Error;
            }
            if (status.Equals("Processing", StringComparison.OrdinalIgnoreCase) ||
                status.Contains("...", StringComparison.OrdinalIgnoreCase) ||
                status.Contains("Converting", StringComparison.OrdinalIgnoreCase) ||
                status.Contains("Verifying", StringComparison.OrdinalIgnoreCase) ||
                status.Contains("RAF →", StringComparison.OrdinalIgnoreCase))
            {
                return FileProcessingState.Processing;
            }
            return FileProcessingState.Pending;
        }

        public string LocalizedStatus
        {
            get
            {
                if (string.IsNullOrEmpty(_status)) return "—";
                if (IsCompleted) return _status == "✔" || _status == "✓" ? _status : LocalizationManager.Tr("StatusDone");
                if (_processingState == FileProcessingState.Pending) return LocalizationManager.Tr("StatusPending");
                if (IsActive) return LocalizationManager.Tr("StatusProcessing");
                if (IsError) return LocalizationManager.Tr("StatusError");
                return _status;
            }
        }

        public void NotifyLanguageChanged()
        {
            OnPropertyChanged(nameof(LocalizedStatus));
        }

        public string StatusIcon => IsCompleted ? "✓" : (IsError ? "✕" : (IsActive ? "⚡" : "⏳"));

        public string? ErrorMessage
        {
            get => _errorMessage;
            set
            {
                if (_errorMessage != value)
                {
                    _errorMessage = value;
                    OnPropertyChanged(nameof(ErrorMessage));
                    OnPropertyChanged(nameof(HasErrorMessage));
                }
            }
        }
        public bool HasErrorMessage => !string.IsNullOrEmpty(_errorMessage);

        public Bitmap? Thumbnail
        {
            get => _thumbnail;
            set
            {
                if (_thumbnail != value)
                {
                    _thumbnail = value;
                    OnPropertyChanged(nameof(Thumbnail));
                }
            }
        }

        public string CameraMakeAndModel
        {
            get => _cameraMakeAndModel;
            set
            {
                if (_cameraMakeAndModel != value)
                {
                    _cameraMakeAndModel = value;
                    OnPropertyChanged(nameof(CameraMakeAndModel));
                }
            }
        }

        private string? _lens;
        public string? Lens
        {
            get => _lens;
            set
            {
                if (_lens != value)
                {
                    _lens = value;
                    OnPropertyChanged(nameof(Lens));
                    OnPropertyChanged(nameof(HasLens));
                }
            }
        }
        public bool HasLens => !string.IsNullOrWhiteSpace(_lens);

        private string? _aperture;
        public string? Aperture
        {
            get => _aperture;
            set
            {
                if (_aperture != value)
                {
                    _aperture = value;
                    OnPropertyChanged(nameof(Aperture));
                    OnPropertyChanged(nameof(FormattedExposure));
                }
            }
        }

        private string? _shutterSpeed;
        public string? ShutterSpeed
        {
            get => _shutterSpeed;
            set
            {
                if (_shutterSpeed != value)
                {
                    _shutterSpeed = value;
                    OnPropertyChanged(nameof(ShutterSpeed));
                    OnPropertyChanged(nameof(FormattedExposure));
                }
            }
        }

        private string? _iso;
        public string? Iso
        {
            get => _iso;
            set
            {
                if (_iso != value)
                {
                    _iso = value;
                    OnPropertyChanged(nameof(Iso));
                    OnPropertyChanged(nameof(FormattedExposure));
                }
            }
        }

        private string? _focalLength;
        public string? FocalLength
        {
            get => _focalLength;
            set
            {
                if (_focalLength != value)
                {
                    _focalLength = value;
                    OnPropertyChanged(nameof(FocalLength));
                    OnPropertyChanged(nameof(FormattedExposure));
                }
            }
        }

        private string? _dateTimeOriginal;
        public string? DateTimeOriginal
        {
            get => _dateTimeOriginal;
            set
            {
                if (_dateTimeOriginal != value)
                {
                    _dateTimeOriginal = value;
                    OnPropertyChanged(nameof(DateTimeOriginal));
                }
            }
        }

        private string? _orientation;
        public string? Orientation
        {
            get => _orientation;
            set
            {
                if (_orientation != value)
                {
                    _orientation = value;
                    OnPropertyChanged(nameof(Orientation));
                    OnPropertyChanged(nameof(HasOrientation));
                }
            }
        }
        public bool HasOrientation => !string.IsNullOrWhiteSpace(_orientation);

        public bool HasExifLoaded { get; set; }

        public string FormattedExposure
        {
            get
            {
                var parts = new System.Collections.Generic.List<string>();
                if (!string.IsNullOrWhiteSpace(Aperture)) parts.Add(Aperture.StartsWith("ƒ") || Aperture.StartsWith("f/") ? Aperture : $"ƒ/{Aperture}");
                if (!string.IsNullOrWhiteSpace(ShutterSpeed)) parts.Add(ShutterSpeed.EndsWith("s") ? ShutterSpeed : $"{ShutterSpeed}s");
                if (!string.IsNullOrWhiteSpace(Iso)) parts.Add(Iso.StartsWith("ISO", System.StringComparison.OrdinalIgnoreCase) ? Iso : $"ISO {Iso}");
                if (!string.IsNullOrWhiteSpace(FocalLength)) parts.Add(FocalLength);
                return parts.Count > 0 ? string.Join("  •  ", parts) : "";
            }
        }

        public bool HasExposureInfo => !string.IsNullOrEmpty(FormattedExposure);

        private long _fileSizeBytes = -1;
        public long FileSizeBytes
        {
            get
            {
                if (_fileSizeBytes < 0)
                {
                    try
                    {
                        if (File.Exists(_filePath))
                        {
                            _fileSizeBytes = new FileInfo(_filePath).Length;
                        }
                    }
                    catch { _fileSizeBytes = 0; }
                }
                return _fileSizeBytes;
            }
        }

        public string FormattedFileSize
        {
            get
            {
                long bytes = FileSizeBytes;
                if (bytes > 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F1} MB";
                if (bytes > 1024) return $"{bytes / 1024.0:F1} KB";
                if (bytes > 0) return $"{bytes} B";
                return "";
            }
        }

        public bool IsCompleted => _processingState == FileProcessingState.Completed
            || _status.Contains("Done", System.StringComparison.OrdinalIgnoreCase)
            || _status.Contains("Complete", System.StringComparison.OrdinalIgnoreCase)
            || _status == "✔" || _status == "✓";

        public bool IsError => _processingState == FileProcessingState.Error
            || _status.Contains("Error", System.StringComparison.OrdinalIgnoreCase)
            || _status.Contains("Fail", System.StringComparison.OrdinalIgnoreCase)
            || _status.Contains("Invalid", System.StringComparison.OrdinalIgnoreCase);

        public bool IsActive => _processingState == FileProcessingState.Processing
            || _status.Contains("...", System.StringComparison.OrdinalIgnoreCase)
            || _status.Contains("Converting", System.StringComparison.OrdinalIgnoreCase);

        public string StatusColor => IsError ? "#E74C3C" : (IsCompleted ? "#01D701" : "#A0A0AC");
        public string ErrorBackground => IsError ? "#22E74C3C" : "Transparent";
        public string ErrorBorderBrush => IsError ? "#80E74C3C" : "Transparent";
        public double ErrorBorderThickness => IsError ? 1.0 : 0.0;

        public void Dispose()
        {
            DisposeThumbnail();
        }

        public void DisposeThumbnail()
        {
            var thumb = _thumbnail;
            _thumbnail = null;
            if (thumb != null && !ReferenceEquals(thumb, Services.ThumbnailService.CachedPlaceholder))
            {
                thumb.Dispose();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            if (Avalonia.Application.Current == null || Dispatcher.UIThread.CheckAccess())
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
            else
            {
                Dispatcher.UIThread.Post(() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName)));
            }
        }
    }
}
