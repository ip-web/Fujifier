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
using System.Runtime.InteropServices;

namespace Fujify.Common
{
    /// <summary>
    /// Helper to detect CPU topology (Performance vs Efficient cores on hybrid architectures)
    /// to optimize multi-threaded thread pools (like Rayon) and prevent straggler bottlenecks.
    /// </summary>
    public static class CpuTopologyHelper
    {
        private const int RelationProcessorCore = 0;
        private const byte LTP_PC_SMT = 1;

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESSOR_RELATIONSHIP
        {
            public byte Flags;
            public byte EfficiencyClass;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
            public byte[] Reserved;
            public ushort GroupCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX
        {
            public int Relationship;
            public int Size;
            public PROCESSOR_RELATIONSHIP Processor;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetLogicalProcessorInformationEx(
            int relationshipType,
            IntPtr buffer,
            ref int returnedLength);

        private static readonly Lazy<(int PThreads, int PCores, int ECores, bool IsHybrid)> _topology = new(DetectTopology);

        /// <summary>
        /// Gets the number of logical threads belonging to Performance (P) cores.
        /// On homogeneous processors (AMD, non-hybrid Intel, macOS Apple Silicon), returns Environment.ProcessorCount.
        /// </summary>
        public static int PerformanceThreadCount => _topology.Value.PThreads;

        /// <summary>
        /// Gets whether the current CPU features a hybrid architecture with asymmetric Performance and Efficiency cores.
        /// </summary>
        public static bool IsHybridArchitecture => _topology.Value.IsHybrid;

        /// <summary>
        /// Gets the detected P-core physical count.
        /// </summary>
        public static int PerformanceCoreCount => _topology.Value.PCores;

        /// <summary>
        /// Gets the detected E-core physical count.
        /// </summary>
        public static int EfficientCoreCount => _topology.Value.ECores;

        private static (int PThreads, int PCores, int ECores, bool IsHybrid) DetectTopology()
        {
            int fallbackThreads = Environment.ProcessorCount;

            if (!OperatingSystem.IsWindows())
            {
                return (fallbackThreads, fallbackThreads, 0, false);
            }

            try
            {
                int length = 0;
                GetLogicalProcessorInformationEx(RelationProcessorCore, IntPtr.Zero, ref length);
                if (length == 0)
                {
                    return (fallbackThreads, fallbackThreads, 0, false);
                }

                IntPtr ptr = Marshal.AllocHGlobal(length);
                try
                {
                    if (GetLogicalProcessorInformationEx(RelationProcessorCore, ptr, ref length))
                    {
                        int offset = 0;
                        int pCores = 0;
                        int eCores = 0;
                        int pThreads = 0;
                        byte maxEfficiency = 0;

                        // First pass: check if any core has EfficiencyClass > 0
                        int scanOffset = 0;
                        while (scanOffset < length)
                        {
                            var info = Marshal.PtrToStructure<SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX>(IntPtr.Add(ptr, scanOffset));
                            if (info.Relationship == RelationProcessorCore && info.Processor.EfficiencyClass > maxEfficiency)
                            {
                                maxEfficiency = info.Processor.EfficiencyClass;
                            }
                            scanOffset += info.Size;
                        }

                        // If all cores share the same efficiency class, it's homogeneous (not hybrid)
                        if (maxEfficiency == 0)
                        {
                            return (fallbackThreads, fallbackThreads, 0, false);
                        }

                        while (offset < length)
                        {
                            var info = Marshal.PtrToStructure<SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX>(IntPtr.Add(ptr, offset));
                            if (info.Relationship == RelationProcessorCore)
                            {
                                bool isSmt = (info.Processor.Flags & LTP_PC_SMT) != 0;
                                if (info.Processor.EfficiencyClass == maxEfficiency)
                                {
                                    pCores++;
                                    pThreads += isSmt ? 2 : 1;
                                }
                                else
                                {
                                    eCores++;
                                }
                            }
                            offset += info.Size;
                        }

                        if (pCores > 0 && eCores > 0 && pThreads > 0)
                        {
                            Debug.WriteLine($"[CpuTopologyHelper] Hybrid CPU detected: {pCores} P-cores ({pThreads} threads), {eCores} E-cores.");
                            return (pThreads, pCores, eCores, true);
                        }
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(ptr);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CpuTopologyHelper] Detection failed: {ex.Message}");
            }

            return (fallbackThreads, fallbackThreads, 0, false);
        }
    }
}

