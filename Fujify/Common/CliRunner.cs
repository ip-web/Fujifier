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
using System.Linq;
using System.Text;
using Fujify.Services;

namespace Fujify.Common
{
    public static class CliRunner
    {
        public static readonly string[] SupportedExtensions = new[]
        {
            ".arw", ".cr2", ".cr3", ".crw", ".dng", ".nef", ".nrw", ".orf", ".raf", ".rw2",
            ".pef", ".srw", ".srf", ".sr2", ".3fr", ".erf", ".kdc", ".dcs", ".dcr", ".iiq",
            ".mos", ".mef", ".mrw"
        };

        public static readonly string[] SupportedCameraModels = new[]
        {
            "Fujifilm X-T5",
            "Fujifilm X-T4",
            "Fujifilm X-T3",
            "Fujifilm X-T30 II",
            "Fujifilm X-T30",
            "Fujifilm X-T20",
            "Fujifilm X-T2",
            "Fujifilm X-Pro3",
            "Fujifilm X-Pro2",
            "Fujifilm X100VI",
            "Fujifilm X100V",
            "Fujifilm X100F",
            "Fujifilm X-H2S",
            "Fujifilm X-H2",
            "Fujifilm X-H1",
            "Fujifilm X-S20",
            "Fujifilm X-S10",
            "Fujifilm GFX 100 II",
            "Fujifilm GFX 100S",
            "Fujifilm GFX 50S II"
        };

        public static void PrintHelp()
        {
            Console.WriteLine(@"Fujifier - Fujifilm Film Simulation Profiles for Any RAW Photo
Usage:
  Fujifier [options] <files|directories...>

Options:
  -h, --help                  Show this help text and exit
  -v, --version               Display application version and exit
  -c, --cli                   Run in headless Command Line Interface (CLI) mode
  -p, --process               Direct conversion mode
  -o, --out <dir>             Specify output directory for converted DNG files
  -m, --model <camera>        Target Fujifilm camera model (default: 'Fujifilm X-T5')
  -s, --simulation <name>     Apply a specific film simulation (e.g., 'Classic Chrome')
      --list-models           List all supported Fujifilm camera models
      --list-simulations      List all 20 available Fujifilm film simulations
      --backup                Preserve original RAW files (default)
      --no-backup             Do not keep original files after conversion
      --embed-raw             Embed original RAW data inside output DNG
      --no-embed-raw          Do not embed original RAW data (default, smaller size)
      --lossless              Use lossless DNG compression (default)
      --uncompressed          Produce uncompressed DNG files
      --verify, --verify-integrity Test file integrity after conversion
      --silent, --quiet       Suppress progress output
      --register-context-menu Register Explorer context menu and Open With associations
      --unregister-context-menu Remove Explorer context menu and Open With associations

Examples:
  # Convert a photo in CLI mode using default X-T5 profile:
  Fujifier --cli photo.arw

  # Convert multiple photos and save to a specific directory:
  Fujifier -c -o ./converted/ *.arw *.cr3

  # Convert and pre-apply 'Classic Chrome' film simulation sidecar:
  Fujifier -c -s ""Classic Chrome"" photo.nef

  # Convert all photos in a folder:
  Fujifier -c -o ./output/ /path/to/raw/photos/

Supported RAW formats:
  ARW, CR2, CR3, CRW, DNG, NEF, NRW, ORF, RAF, RW2, PEF, SRW, SRF, SR2,
  3FR, ERF, KDC, DCS, DCR, IIQ, MOS, MEF, MRW");
        }

        public static void PrintVersion()
        {
            var version = typeof(CliRunner).Assembly.GetName().Version?.ToString(3) ?? "2.2.1";
            Console.WriteLine($"Fujifier {version}");
        }

        public static void PrintModels()
        {
            Console.WriteLine("Supported Fujifilm Camera Profiles:");
            foreach (var model in SupportedCameraModels)
            {
                Console.WriteLine($"  - {model}");
            }
        }

        public static void PrintSimulations()
        {
            var presetService = new LightroomPresetService();
            Console.WriteLine("Available Fujifilm Film Simulations:");
            foreach (var preset in presetService.Presets)
            {
                Console.WriteLine($"  - {preset.Id.PadRight(22)} {preset.DisplayName} ({preset.Description})");
            }
        }

        public static List<string> CollectFiles(IEnumerable<string> inputs)
        {
            var collected = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var input in inputs)
            {
                if (string.IsNullOrWhiteSpace(input)) continue;

                if (Directory.Exists(input))
                {
                    try
                    {
                        foreach (var file in Directory.EnumerateFiles(input, "*.*", SearchOption.AllDirectories))
                        {
                            string ext = Path.GetExtension(file);
                            if (SupportedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
                            {
                                if (seen.Add(Path.GetFullPath(file)))
                                {
                                    collected.Add(Path.GetFullPath(file));
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"Warning: Error scanning directory '{input}': {ex.Message}");
                    }
                }
                else if (File.Exists(input))
                {
                    string ext = Path.GetExtension(input);
                    if (SupportedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
                    {
                        if (seen.Add(Path.GetFullPath(input)))
                        {
                            collected.Add(Path.GetFullPath(input));
                        }
                    }
                    else
                    {
                        Console.Error.WriteLine($"Warning: Unsupported file format '{ext}' for file '{input}'");
                    }
                }
                else
                {
                    Console.Error.WriteLine($"Warning: File or directory not found: '{input}'");
                }
            }

            return collected;
        }

        public static int Run(CommandLineArgs args)
        {
            if (args.UnregisterContextMenu)
            {
                if (OperatingSystem.IsWindows())
                {
                    WindowsContextMenuService.UnregisterContextMenu();
                    WindowsContextMenuService.UnregisterOpenWith();
                }
                else if (OperatingSystem.IsMacOS())
                {
                    MacQuickActionService.UnregisterQuickActions();
                }
                return 0;
            }

            if (args.RegisterContextMenu)
            {
                if (OperatingSystem.IsWindows())
                {
                    WindowsContextMenuService.RegisterContextMenu();
                    WindowsContextMenuService.RegisterOpenWith();
                }
                else if (OperatingSystem.IsMacOS())
                {
                    MacQuickActionService.RegisterQuickActions();
                }
                return 0;
            }

            if (args.ShowHelp)
            {
                PrintHelp();
                return 0;
            }

            if (args.ShowVersion)
            {
                PrintVersion();
                return 0;
            }

            if (args.ListModels)
            {
                PrintModels();
                return 0;
            }

            if (args.ListSimulations)
            {
                PrintSimulations();
                return 0;
            }

            var files = CollectFiles(args.Files);
            if (files.Count == 0)
            {
                Console.Error.WriteLine("Error: No valid RAW or DNG files found to process.");
                Console.Error.WriteLine("Run 'Fujifier --help' for usage instructions.");
                return 1;
            }

            var rawConverter = new RawConverterService();
            var metadataService = new MetadataService();
            var presetService = new LightroomPresetService();
            var fileIntegrityService = new FileIntegrityService();

            string cameraModel = !string.IsNullOrWhiteSpace(args.CameraModel) ? args.CameraModel : "Fujifilm X-T5";
            string? outDir = args.OutputDirectory;
            if (!string.IsNullOrEmpty(outDir))
            {
                try
                {
                    Directory.CreateDirectory(outDir);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error: Failed to create output directory '{outDir}': {ex.Message}");
                    return 1;
                }
            }

            // Find matching simulation preset if specified
            LightroomPresetInfo? selectedPreset = null;
            if (!string.IsNullOrWhiteSpace(args.FilmSimulation))
            {
                string query = args.FilmSimulation.Trim();
                selectedPreset = presetService.Presets.FirstOrDefault(p =>
                    p.Id.Equals(query, StringComparison.OrdinalIgnoreCase) ||
                    p.DisplayName.Equals(query, StringComparison.OrdinalIgnoreCase) ||
                    p.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase));

                if (selectedPreset == null)
                {
                    Console.Error.WriteLine($"Warning: Unknown film simulation '{query}'. Available simulations:");
                    foreach (var p in presetService.Presets)
                    {
                        Console.Error.WriteLine($"  {p.Id} ({p.DisplayName})");
                    }
                    return 1;
                }
            }

            if (!args.Quiet)
            {
                Console.WriteLine("============================================================");
                Console.WriteLine("Fujifier CLI - Fujifilm RAW & DNG Conversion Engine");
                Console.WriteLine("============================================================");
                Console.WriteLine($"Target Camera Model : {cameraModel}");
                if (selectedPreset != null)
                {
                    Console.WriteLine($"Film Simulation     : {selectedPreset.DisplayName}");
                }
                Console.WriteLine($"Output Destination  : {(string.IsNullOrEmpty(outDir) ? "Same as source file" : outDir)}");
                Console.WriteLine($"Backup Original     : {args.BackupOriginalFile}");
                Console.WriteLine($"Embed Original RAW  : {args.EmbedOriginalRaw}");
                Console.WriteLine($"Lossless Compression: {args.LosslessCompression}");
                Console.WriteLine($"Files to Process    : {files.Count}");
                Console.WriteLine("------------------------------------------------------------");
            }

            int successCount = 0;
            int errorCount = 0;
            int completedCount = 0;
            var stopwatch = Stopwatch.StartNew();
            var rafConverter = new RafConverterService();
            var reservedOutputPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var consoleLock = new object();
            int maxConcurrency = DrivePerformanceHelper.GetOptimalConversionConcurrency(
                files[0],
                configuredConcurrency: 0,
                isPro: true);

            var items = files.Select((f, idx) => (File: f, Index: idx)).ToList();
            System.Threading.Tasks.Parallel.ForEachAsync(
                items,
                new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = maxConcurrency },
                async (item, ct) =>
                {
                    string file = item.File;
                    string fileName = Path.GetFileName(file);
                    var fileSw = Stopwatch.StartNew();

                    try
                    {
                        string initialTarget = !string.IsNullOrEmpty(outDir)
                            ? Path.Combine(outDir, Path.ChangeExtension(fileName, ".dng"))
                            : Path.ChangeExtension(file, ".dng");

                        string finalTarget = initialTarget;
                        bool isOriginallyDng = file.EndsWith(".dng", StringComparison.OrdinalIgnoreCase);
                        string inputDir = Path.GetDirectoryName(file) ?? string.Empty;
                        string outputDir = Path.GetDirectoryName(initialTarget) ?? string.Empty;
                        bool isSameFolder = string.Equals(Path.GetFullPath(inputDir), Path.GetFullPath(outputDir), StringComparison.OrdinalIgnoreCase);
                        bool isExactSame = isOriginallyDng && string.Equals(Path.GetFullPath(file), Path.GetFullPath(initialTarget), StringComparison.OrdinalIgnoreCase);

                        lock (reservedOutputPaths)
                        {
                            if (!isExactSame && !(isOriginallyDng && isSameFolder && args.BackupOriginalFile) &&
                                (File.Exists(initialTarget) || reservedOutputPaths.Contains(initialTarget)))
                            {
                                finalTarget = ConversionPipelineHelper.GetUniqueFilePath(initialTarget, reservedOutputPaths);
                            }
                            else
                            {
                                reservedOutputPaths.Add(initialTarget);
                            }
                        }

                        var request = new SingleConversionRequest(
                            SourceFilePath: file,
                            FinalDestFilePath: finalTarget,
                            OutputFolder: outDir,
                            CreateBackup: args.BackupOriginalFile,
                            EmbedOriginalRaw: args.EmbedOriginalRaw,
                            LosslessCompression: args.LosslessCompression,
                            VerifyFileIntegrity: args.VerifyFileIntegrity,
                            IsCaptureOneMode: false,
                            RafContainerPath: null,
                            TargetUniqueCameraModel: cameraModel);

                        string finalCustomOut = await ConversionPipelineHelper.ExecuteSingleConversionAsync(
                            request,
                            rawConverter,
                            rafConverter,
                            metadataService,
                            fileIntegrityService,
                            ct);

                        // If film simulation was specified, generate and write sidecar XMP
                        if (selectedPreset != null)
                        {
                            string xmpPath = Path.ChangeExtension(finalCustomOut, ".xmp");
                            string xmpContent = presetService.GeneratePresetXml(selectedPreset);
                            await File.WriteAllTextAsync(xmpPath, xmpContent, Encoding.UTF8, ct);
                        }

                        fileSw.Stop();
                        System.Threading.Interlocked.Increment(ref successCount);
                        int done = System.Threading.Interlocked.Increment(ref completedCount);
                        if (!args.Quiet)
                        {
                            lock (consoleLock)
                            {
                                Console.WriteLine($"[{done}/{files.Count}] {fileName}... [OK] -> {Path.GetFileName(finalCustomOut)} ({fileSw.Elapsed.TotalSeconds:F1}s)");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        fileSw.Stop();
                        System.Threading.Interlocked.Increment(ref errorCount);
                        int done = System.Threading.Interlocked.Increment(ref completedCount);
                        lock (consoleLock)
                        {
                            if (!args.Quiet)
                            {
                                Console.WriteLine($"[{done}/{files.Count}] {fileName}... [FAILED] ({ex.Message})");
                            }
                            else
                            {
                                Console.Error.WriteLine($"Error processing '{fileName}': {ex.Message}");
                            }
                        }
                    }
                }).GetAwaiter().GetResult();

            stopwatch.Stop();
            if (!args.Quiet)
            {
                Console.WriteLine("------------------------------------------------------------");
                Console.WriteLine($"Done: {successCount} succeeded, {errorCount} failed in {stopwatch.Elapsed.TotalSeconds:F1}s.");
                Console.WriteLine("============================================================");
            }

            return errorCount == 0 ? 0 : 1;
        }
    }
}
