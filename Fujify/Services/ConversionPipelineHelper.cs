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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Fujify.Common;
using Fujify.Localization;

namespace Fujify.Services
{
    public sealed record SingleConversionRequest(
        string SourceFilePath,
        string FinalDestFilePath,
        string? OutputFolder,
        bool CreateBackup,
        bool EmbedOriginalRaw,
        bool LosslessCompression,
        bool VerifyFileIntegrity,
        bool IsCaptureOneMode,
        string? RafContainerPath,
        string TargetUniqueCameraModel,
        Action<string>? StatusCallback = null);

    /// <summary>
    /// Shared single-file conversion, metadata injection, integrity check, rollback, and cleanup pipeline
    /// used consistently by the desktop/mobile GUI, Quick Process window, and CLI runner.
    /// </summary>
    public static class ConversionPipelineHelper
    {
        public static string GetUniqueFilePath(string filePath, HashSet<string>? reservedPaths = null)
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

        private static void EnsureDirectoryExists(string? filePath)
        {
            string? dir = Path.GetDirectoryName(filePath);
            if (string.IsNullOrEmpty(dir) || Directory.Exists(dir)) return;

            try
            {
                Directory.CreateDirectory(dir);
            }
            catch (Exception ex)
            {
                if (OperatingSystem.IsAndroid())
                {
                    DialogService.PlatformEnsurePermissionsCallback?.Invoke();
                }
                throw new UnauthorizedAccessException($"Cannot create output directory '{dir}': {ex.Message}.", ex);
            }
        }

        public static async Task<string> ExecuteSingleConversionAsync(
            SingleConversionRequest request,
            IRawConverterService rawConverterService,
            IRafConverterService rafConverterService,
            IMetadataService metadataService,
            IFileIntegrityService fileIntegrityService,
            CancellationToken ct = default)
        {
            string filePath = request.SourceFilePath;
            string finalDestFilePath = request.FinalDestFilePath;
            bool createBackup = request.CreateBackup;
            string inputDir = Path.GetDirectoryName(filePath) ?? string.Empty;
            string outputDir = Path.GetDirectoryName(finalDestFilePath) ?? string.Empty;
            bool isSameFolder = string.Equals(Path.GetFullPath(inputDir), Path.GetFullPath(outputDir), StringComparison.OrdinalIgnoreCase);

            string targetExt = request.IsCaptureOneMode ? ".raf" : ".dng";
            bool isOriginallyTarget = filePath.EndsWith(targetExt, StringComparison.OrdinalIgnoreCase);
            bool isExactSameFilePath = isOriginallyTarget && string.Equals(Path.GetFullPath(filePath), Path.GetFullPath(finalDestFilePath), StringComparison.OrdinalIgnoreCase);

            if (request.IsCaptureOneMode)
            {
                if (filePath.EndsWith(".raf", StringComparison.OrdinalIgnoreCase))
                {
                    bool isBayer = rafConverterService.IsBayerSensor(filePath);
                    string targetModel = isBayer ? "GFX50S II" : "X-T5";
                    request.StatusCallback?.Invoke(isBayer ? "Bayer RAF → GFX50S II" : "X-Trans RAF → X-T5");

                    EnsureDirectoryExists(finalDestFilePath);

                    string sourceForPatch = filePath;
                    if (isExactSameFilePath && createBackup && isSameFolder)
                    {
                        string origBaseName = Path.GetFileNameWithoutExtension(filePath);
                        string origBackup = Path.Combine(inputDir, $"{origBaseName}ORIG.RAF");
                        if (File.Exists(origBackup)) File.Delete(origBackup);
                        File.Move(filePath, origBackup);
                        sourceForPatch = origBackup;
                    }

                    var patchResult = await rafConverterService.PatchModelAsync(sourceForPatch, finalDestFilePath, targetModel, ct);
                    if (!patchResult.Success)
                    {
                        if (isExactSameFilePath && createBackup && isSameFolder && File.Exists(sourceForPatch) && !File.Exists(filePath))
                        {
                            try { File.Move(sourceForPatch, filePath); } catch { }
                        }
                        throw new InvalidOperationException(patchResult.ErrorMessage ?? "RAF model patch failed");
                    }

                    if (request.VerifyFileIntegrity)
                    {
                        request.StatusCallback?.Invoke("Verifying file integrity");
                        var integrity = await fileIntegrityService.VerifyIntegrityAsync(finalDestFilePath, ct);
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

                string resolvedContainer = rafConverterService.ResolveContainerArgument(request.RafContainerPath);
                if (string.IsNullOrWhiteSpace(resolvedContainer) || !rafConverterService.HasValidContainer(resolvedContainer))
                {
                    throw new InvalidOperationException(LocalizationManager.Tr("MissingRafContainerError"));
                }

                EnsureDirectoryExists(finalDestFilePath);
                request.StatusCallback?.Invoke(LocalizationManager.Tr("ConvertingToRaf"));

                var rafResult = await rafConverterService.ConvertToRafAsync(filePath, finalDestFilePath, resolvedContainer, null, ct);
                if (!rafResult.Success || string.IsNullOrEmpty(rafResult.OutputRafPath))
                {
                    throw new InvalidOperationException(rafResult.ErrorMessage ?? "Conversion to RAF failed");
                }

                if (request.VerifyFileIntegrity)
                {
                    request.StatusCallback?.Invoke("Verifying file integrity");
                    var integrity = await fileIntegrityService.VerifyIntegrityAsync(finalDestFilePath, ct);
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

            bool isOriginallyDng = filePath.EndsWith(".dng", StringComparison.OrdinalIgnoreCase);
            string dngFilePath = filePath;
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
                    request.StatusCallback?.Invoke("Converting to DNG");
                    EnsureDirectoryExists(finalDestFilePath);

                    ct.ThrowIfCancellationRequested();
                    var convResult = rawConverterService.ConvertToDng(
                        filePath,
                        request.EmbedOriginalRaw,
                        finalDestFilePath,
                        request.LosslessCompression);

                    if (!convResult.Success || string.IsNullOrEmpty(convResult.OutputDngPath))
                    {
                        throw new InvalidOperationException(convResult.ErrorMessage ?? "Conversion failed");
                    }

                    dngFilePath = convResult.OutputDngPath;
                    request.StatusCallback?.Invoke("Conversion successful");
                }

                string? customOutputPath = tempInPlaceOutputFile ?? ((!string.IsNullOrEmpty(request.OutputFolder) || finalDestFilePath != dngFilePath)
                    ? finalDestFilePath
                    : null);

                if (!string.IsNullOrEmpty(customOutputPath))
                {
                    EnsureDirectoryExists(customOutputPath);
                }

                ct.ThrowIfCancellationRequested();
                var metaResult = metadataService.InjectFujifilmMetadata(
                    dngFilePath,
                    request.OutputFolder,
                    createBackup: false,
                    isOriginallyDng: isOriginallyDng,
                    customOutputFilePath: customOutputPath,
                    targetUniqueCameraModel: request.TargetUniqueCameraModel,
                    sourceRawFilePath: filePath);

                if (!metaResult.Success)
                {
                    throw new InvalidOperationException(metaResult.ErrorMessage ?? "Error writing metadata");
                }

                string targetToVerify = !string.IsNullOrEmpty(tempInPlaceOutputFile)
                    ? tempInPlaceOutputFile
                    : finalDestFilePath;

                if (request.VerifyFileIntegrity)
                {
                    request.StatusCallback?.Invoke("Verifying file integrity");
                    var integrity = await fileIntegrityService.VerifyIntegrityAsync(targetToVerify, ct);
                    if (!integrity.IsValid)
                    {
                        throw new InvalidOperationException($"File integrity check failed: {integrity.ErrorMessage}");
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
}
