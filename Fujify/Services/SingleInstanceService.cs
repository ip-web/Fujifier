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
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Fujify.Common;

namespace Fujify.Services
{
    public class IpcPayload
    {
        public string Mode { get; set; } = "queue"; // "queue", "process", "process-to"
        public List<string> Files { get; set; } = new();
    }

    public static class SingleInstanceService
    {
        public const string ChannelMain = "main";
        public const string ChannelQuick = "quick";

        private static readonly Dictionary<string, Mutex> _mutexes = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, CancellationTokenSource> _serverCtss = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Action<IpcPayload>> _payloadCallbacks = new(StringComparer.OrdinalIgnoreCase);

        private static readonly ConcurrentDictionary<string, ConcurrentQueue<IpcPayload>> _incomingQueues = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, Timer> _debounceTimers = new(StringComparer.OrdinalIgnoreCase);
        private static readonly object _syncLock = new();

        public static string GetPipeName(string channel = ChannelMain)
        {
            string user = Environment.UserName.Replace("\\", "_").Replace("/", "_");
            if (channel.Equals(ChannelQuick, StringComparison.OrdinalIgnoreCase))
                return $"Fujifier_QuickProcess_Pipe_{user}";
            if (channel.Equals(ChannelMain, StringComparison.OrdinalIgnoreCase))
                return $"Fujifier_IPC_Pipe_{user}";
            return $"Fujifier_Pipe_{channel}_{user}";
        }

        public static string GetMutexName(string channel = ChannelMain)
        {
            string user = Environment.UserName.Replace("\\", "_").Replace("/", "_");
            if (channel.Equals(ChannelQuick, StringComparison.OrdinalIgnoreCase))
                return $"Fujifier_QuickProcess_Mutex_{user}";
            if (channel.Equals(ChannelMain, StringComparison.OrdinalIgnoreCase))
                return $"Fujifier_SingleInstance_Mutex_{user}";
            return $"Fujifier_Mutex_{channel}_{user}";
        }

        /// <summary>
        /// Attempts to acquire the single-instance mutex for the specified channel ("main" or "quick").
        /// Returns true if this is the first running instance for this channel.
        /// </summary>
        public static bool TryAcquireMutex(string channel = ChannelMain)
        {
            try
            {
                lock (_syncLock)
                {
                    if (_mutexes.TryGetValue(channel, out var existing))
                    {
                        return true;
                    }

                    var mutex = new Mutex(true, GetMutexName(channel), out bool createdNew);
                    _mutexes[channel] = mutex;
                    return createdNew;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SingleInstanceService] Mutex error on channel '{channel}': {ex.Message}");
                return true; // Fallback to running if mutex fails
            }
        }

        /// <summary>
        /// Sends an IPC payload to the running primary instance of the specified channel via Named Pipe.
        /// Retries with backoff up to timeoutMs to handle high concurrency during Windows Explorer batch launches.
        /// </summary>
        public static bool SendPayloadToExistingInstance(IpcPayload payload, string channel = ChannelMain, int timeoutMs = 6000)
        {
            var sw = Stopwatch.StartNew();
            int attempt = 0;
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                attempt++;
                try
                {
                    using var pipeClient = new NamedPipeClientStream(".", GetPipeName(channel), PipeDirection.Out);
                    int remaining = (int)Math.Max(100, timeoutMs - sw.ElapsedMilliseconds);
                    pipeClient.Connect(remaining);

                    using var writer = new StreamWriter(pipeClient, Encoding.UTF8) { AutoFlush = true };
                    string json = JsonSerializer.Serialize(payload ?? new IpcPayload());
                    writer.WriteLine(json);
                    return true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SingleInstanceService] Attempt {attempt} failed on channel '{channel}': {ex.Message}");
                    int remaining = (int)(timeoutMs - sw.ElapsedMilliseconds);
                    if (remaining <= 0) break;
                    Thread.Sleep(Math.Min(remaining, 50));
                }
            }
            return false;
        }

        /// <summary>
        /// Backwards-compatible overload: parses command-line args and forwards them to the appropriate channel.
        /// </summary>
        public static bool SendArgsToExistingInstance(string[] args, int timeoutMs = 6000)
        {
            var parsed = CommandLineArgs.Parse(args);
            string channel = parsed.IsQuickProcess ? ChannelQuick : ChannelMain;
            return SendPayloadToExistingInstance(new IpcPayload
            {
                Mode = parsed.Mode.ToString().ToLowerInvariant(),
                Files = parsed.Files
            }, channel, timeoutMs);
        }

        /// <summary>
        /// Starts the Named Pipe server loop in the background for the specified channel.
        /// </summary>
        public static void StartServer(Action<IpcPayload> onPayloadReceived, string channel = ChannelMain)
        {
            lock (_syncLock)
            {
                _payloadCallbacks[channel] = onPayloadReceived;
                if (_serverCtss.TryGetValue(channel, out var existingCts))
                {
                    existingCts.Cancel();
                    existingCts.Dispose();
                }

                var cts = new CancellationTokenSource();
                _serverCtss[channel] = cts;

                Task.Run(async () => await RunServerLoopAsync(channel, cts.Token));
            }
        }

        /// <summary>
        /// Backwards-compatible overload for full UI file callback.
        /// </summary>
        public static void StartServer(Action<IReadOnlyList<string>> onFilesReceived)
        {
            StartServer(payload => onFilesReceived(payload.Files), ChannelMain);
        }

        private static async Task RunServerLoopAsync(string channel, CancellationToken token)
        {
            string pipeName = GetPipeName(channel);

            // Maintain concurrent listening instances so simultaneous clients never face an accept gap
            const int concurrentListeners = 4;
            var listenerTasks = new List<Task>();

            for (int i = 0; i < concurrentListeners; i++)
            {
                listenerTasks.Add(RunSingleListenerLoopAsync(pipeName, channel, token));
            }

            try
            {
                await Task.WhenAll(listenerTasks);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SingleInstanceService] Listener group error on channel '{channel}': {ex.Message}");
            }
        }

        private static async Task RunSingleListenerLoopAsync(string pipeName, string channel, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                NamedPipeServerStream? server = null;
                try
                {
                    server = new NamedPipeServerStream(
                        pipeName,
                        PipeDirection.In,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync(token);

                    var connectedServer = server;
                    server = null; // Ownership transferred to consumer task

                    // Process client connection asynchronously
                    _ = Task.Run(async () =>
                    {
                        using (connectedServer)
                        using (var reader = new StreamReader(connectedServer, Encoding.UTF8))
                        {
                            try
                            {
                                var line = await reader.ReadLineAsync(token);
                                if (!string.IsNullOrWhiteSpace(line))
                                {
                                    IpcPayload? payload = null;
                                    if (line.TrimStart().StartsWith("{", StringComparison.Ordinal))
                                    {
                                        payload = JsonSerializer.Deserialize<IpcPayload>(line);
                                    }
                                    else if (line.TrimStart().StartsWith("[", StringComparison.Ordinal))
                                    {
                                        var rawArray = JsonSerializer.Deserialize<string[]>(line);
                                        if (rawArray != null)
                                        {
                                            var parsed = CommandLineArgs.Parse(rawArray);
                                            payload = new IpcPayload
                                            {
                                                Mode = parsed.Mode.ToString().ToLowerInvariant(),
                                                Files = parsed.Files
                                            };
                                        }
                                    }

                                    if (payload != null)
                                    {
                                        EnqueueIncomingPayload(channel, payload);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"[SingleInstanceService] Client reader error on channel '{channel}': {ex.Message}");
                            }
                        }
                    }, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SingleInstanceService] Single listener error on channel '{channel}': {ex.Message}");
                    try
                    {
                        await Task.Delay(50, token);
                    }
                    catch { break; }
                }
                finally
                {
                    server?.Dispose();
                }
            }
        }

        private static void EnqueueIncomingPayload(string channel, IpcPayload payload)
        {
            var queue = _incomingQueues.GetOrAdd(channel, _ => new ConcurrentQueue<IpcPayload>());
            queue.Enqueue(payload);

            lock (_syncLock)
            {
                if (_debounceTimers.TryGetValue(channel, out var timer))
                {
                    timer.Dispose();
                }

                _debounceTimers[channel] = new Timer(_ =>
                {
                    var allFiles = new List<string>();
                    string mode = "queue";

                    while (queue.TryDequeue(out var p))
                    {
                        if (p.Files != null)
                        {
                            allFiles.AddRange(p.Files);
                        }
                        if (!string.IsNullOrWhiteSpace(p.Mode))
                        {
                            mode = p.Mode;
                        }
                    }

                    if (allFiles.Count > 0 && _payloadCallbacks.TryGetValue(channel, out var callback))
                    {
                        callback.Invoke(new IpcPayload
                        {
                            Mode = mode,
                            Files = allFiles
                        });
                    }
                }, null, 150, Timeout.Infinite);
            }
        }

        public static void Release(string? channel = null)
        {
            lock (_syncLock)
            {
                if (channel != null)
                {
                    ReleaseChannel(channel);
                }
                else
                {
                    foreach (var key in new List<string>(_mutexes.Keys))
                    {
                        ReleaseChannel(key);
                    }
                }
            }
        }

        private static void ReleaseChannel(string channel)
        {
            try
            {
                if (_serverCtss.TryGetValue(channel, out var cts))
                {
                    cts.Cancel();
                    cts.Dispose();
                    _serverCtss.Remove(channel);
                }

                if (_debounceTimers.TryGetValue(channel, out var timer))
                {
                    timer.Dispose();
                    _debounceTimers.TryRemove(channel, out _);
                }

                if (_mutexes.TryGetValue(channel, out var mutex))
                {
                    mutex.ReleaseMutex();
                    mutex.Dispose();
                    _mutexes.Remove(channel);
                }
            }
            catch { }
        }
    }
}
