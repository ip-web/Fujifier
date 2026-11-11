using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fujify.Services;
using Fujify.ViewModels;
using Xunit;

namespace Fujify.Tests
{
    public class SingleInstanceIpcTests
    {
        private class MockSettingsService : ISettingsService
        {
            public AppSettings Settings { get; } = new();
            public void Save() { }
            public void Load() { }
        }

        [Fact]
        public async Task SingleInstanceService_ConcurrentPayloads_DeliversAllFilesWithoutLoss()
        {
            if (!OperatingSystem.IsWindows()) return;

            string channel = $"test_{Guid.NewGuid():N}";
            SingleInstanceService.Release(channel);

            bool acquired = SingleInstanceService.TryAcquireMutex(channel);
            Assert.True(acquired);

            var receivedFiles = new ConcurrentBag<string>();
            var receivedTcs = new TaskCompletionSource<bool>();
            const int totalClients = 15;

            SingleInstanceService.StartServer(payload =>
            {
                foreach (var f in payload.Files)
                {
                    receivedFiles.Add(f);
                }
                if (receivedFiles.Count >= totalClients)
                {
                    receivedTcs.TrySetResult(true);
                }
            }, channel);

            try
            {
                // Send 15 payloads concurrently from separate tasks
                var sendTasks = Enumerable.Range(0, totalClients).Select(i => Task.Run(() =>
                {
                    var payload = new IpcPayload
                    {
                        Mode = "process",
                        Files = new List<string> { $"concurrent_file_{i}.arw" }
                    };
                    bool ok = SingleInstanceService.SendPayloadToExistingInstance(payload, channel, timeoutMs: 8000);
                    Assert.True(ok, $"Payload {i} failed to send");
                })).ToArray();

                await Task.WhenAll(sendTasks);

                // Wait up to 5 seconds for debounced delivery
                var completedTask = await Task.WhenAny(receivedTcs.Task, Task.Delay(5000));
                Assert.Equal(receivedTcs.Task, completedTask);

                for (int i = 0; i < totalClients; i++)
                {
                    Assert.Contains($"concurrent_file_{i}.arw", receivedFiles);
                }
            }
            finally
            {
                SingleInstanceService.Release(channel);
            }
        }

        [Fact]
        public async Task QuickProcessViewModel_ConcurrentAddFiles_ThreadSafeAndLossless()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), $"fujify_test_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            try
            {
                const int fileCount = 20;
                var createdFiles = new List<string>();
                for (int i = 0; i < fileCount; i++)
                {
                    string path = Path.Combine(tempDir, $"photo_{i:D2}.arw");
                    File.WriteAllText(path, "dummy raw content");
                    createdFiles.Add(path);
                }

                var mockSettings = new MockSettingsService();
                mockSettings.Settings.EnableCaptureOneMode = false;

                var vm = new QuickProcessViewModel(
                    initialFiles: new[] { createdFiles[0] },
                    showOptions: true,
                    settingsService: mockSettings);

                // Concurrently call AddFiles from 19 separate tasks
                var tasks = createdFiles.Skip(1).Select(f => Task.Run(() =>
                {
                    vm.AddFiles(new[] { f });
                })).ToArray();

                await Task.WhenAll(tasks);

                Assert.Equal($"{fileCount} photos selected", vm.FilesSummaryText);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }
    }
}
