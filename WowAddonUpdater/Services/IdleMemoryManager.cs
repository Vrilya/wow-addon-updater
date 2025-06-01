using System;
using System.Diagnostics;
using System.IO;
using System.Runtime;
using System.Windows.Threading;

namespace WowAddonUpdater.Services
{
    /// <summary>
    /// Service som hanterar minnesrensning när applikationen är inaktiv
    /// </summary>
    public class IdleMemoryManager : IDisposable
    {
        private readonly DispatcherTimer _idleTimer;
        private readonly DispatcherTimer _memoryCheckTimer;
        private DateTime _lastActivity;
        private bool _disposed = false;
        private const int IDLE_THRESHOLD_MINUTES = 2; // Minuter av inaktivitet innan cleanup
        private const int MEMORY_CHECK_INTERVAL_SECONDS = 30; // Kontrollera minne var 30:e sekund
        private const long HIGH_MEMORY_THRESHOLD_MB = 200; // MB - när ska vi trigga aggressive cleanup

        public event EventHandler<MemoryCleanupEventArgs> MemoryCleanupPerformed;

        public IdleMemoryManager()
        {
            _lastActivity = DateTime.Now;

            // Timer för att kontrollera idle status
            _idleTimer = new DispatcherTimer();
            _idleTimer.Interval = TimeSpan.FromMinutes(1); // Kolla varje minut
            _idleTimer.Tick += IdleTimer_Tick;

            // Timer för minnesövervakning
            _memoryCheckTimer = new DispatcherTimer();
            _memoryCheckTimer.Interval = TimeSpan.FromSeconds(MEMORY_CHECK_INTERVAL_SECONDS);
            _memoryCheckTimer.Tick += MemoryCheckTimer_Tick;
        }

        public void Start()
        {
            _idleTimer.Start();
            _memoryCheckTimer.Start();
            LogMessage("IdleMemoryManager started");
        }

        public void Stop()
        {
            _idleTimer?.Stop();
            _memoryCheckTimer?.Stop();
            LogMessage("IdleMemoryManager stopped");
        }

        /// <summary>
        /// Anropa denna när användaren gör något för att återställa idle timer
        /// </summary>
        public void NotifyUserActivity()
        {
            _lastActivity = DateTime.Now;
        }

        private void IdleTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                TimeSpan idleTime = DateTime.Now - _lastActivity;

                if (idleTime.TotalMinutes >= IDLE_THRESHOLD_MINUTES)
                {
                    LogMessage($"Application idle for {idleTime.TotalMinutes:F1} minutes - performing memory cleanup");
                    PerformIdleMemoryCleanup();
                }
            }
            catch (Exception ex)
            {
                LogError("Error in idle timer tick", ex);
            }
        }

        private void MemoryCheckTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                long currentMemoryMB = GetCurrentMemoryUsageMB();

                if (currentMemoryMB > HIGH_MEMORY_THRESHOLD_MB)
                {
                    LogMessage($"High memory usage detected: {currentMemoryMB}MB - performing memory cleanup");
                    PerformMemoryCleanup(aggressive: true);
                }
            }
            catch (Exception ex)
            {
                LogError("Error in memory check timer tick", ex);
            }
        }

        private void PerformIdleMemoryCleanup()
        {
            var stopwatch = Stopwatch.StartNew();
            long memoryBeforeMB = GetCurrentMemoryUsageMB();

            try
            {
                // Rensa stora objekt och cachar
                ClearLargeObjectCaches();

                // Kompaktera Large Object Heap
                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;

                // Utför minnesrensning
                GC.Collect(2, GCCollectionMode.Forced, true);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Forced, true);

                // Trimma working set (be OS att frigöra oanvänt minne)
                TrimWorkingSet();

                long memoryAfterMB = GetCurrentMemoryUsageMB();
                long freedMB = memoryBeforeMB - memoryAfterMB;

                stopwatch.Stop();

                string message = $"Idle cleanup completed in {stopwatch.ElapsedMilliseconds}ms. " +
                               $"Memory: {memoryBeforeMB}MB → {memoryAfterMB}MB (freed {freedMB}MB)";

                LogMessage(message);

                MemoryCleanupPerformed?.Invoke(this, new MemoryCleanupEventArgs
                {
                    MemoryBeforeMB = memoryBeforeMB,
                    MemoryAfterMB = memoryAfterMB,
                    FreedMemoryMB = freedMB,
                    CleanupType = "Idle",
                    DurationMs = stopwatch.ElapsedMilliseconds
                });
            }
            catch (Exception ex)
            {
                LogError("Error during idle memory cleanup", ex);
            }
        }

        private void PerformMemoryCleanup(bool aggressive = false)
        {
            var stopwatch = Stopwatch.StartNew();
            long memoryBeforeMB = GetCurrentMemoryUsageMB();

            try
            {
                if (aggressive)
                {
                    ClearLargeObjectCaches();
                    GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                }

                // Utför garbage collection
                if (aggressive)
                {
                    GC.Collect(2, GCCollectionMode.Forced, true);
                    GC.WaitForPendingFinalizers();
                    GC.Collect(2, GCCollectionMode.Forced, true);
                    TrimWorkingSet();
                }
                else
                {
                    GC.Collect(0, GCCollectionMode.Optimized);
                }

                long memoryAfterMB = GetCurrentMemoryUsageMB();
                long freedMB = memoryBeforeMB - memoryAfterMB;

                stopwatch.Stop();

                string cleanupType = aggressive ? "Aggressive" : "Normal";
                string message = $"{cleanupType} cleanup completed in {stopwatch.ElapsedMilliseconds}ms. " +
                               $"Memory: {memoryBeforeMB}MB → {memoryAfterMB}MB (freed {freedMB}MB)";

                LogMessage(message);

                MemoryCleanupPerformed?.Invoke(this, new MemoryCleanupEventArgs
                {
                    MemoryBeforeMB = memoryBeforeMB,
                    MemoryAfterMB = memoryAfterMB,
                    FreedMemoryMB = freedMB,
                    CleanupType = cleanupType,
                    DurationMs = stopwatch.ElapsedMilliseconds
                });
            }
            catch (Exception ex)
            {
                LogError("Error during memory cleanup", ex);
            }
        }

        private void ClearLargeObjectCaches()
        {
            // Här kan du rensa applikationsspecifika cachar
            try
            {
                // Rensa JIT cache för oanvända metoder
                GC.Collect();

                // Du kan lägga till rensning av applikationsspecifika cachar här
                // Exempel:
                // SomeStaticCache.Clear();
                // SomeService.ClearCache();
            }
            catch (Exception ex)
            {
                LogError("Error clearing large object caches", ex);
            }
        }

        private void TrimWorkingSet()
        {
            try
            {
                // Be Windows att trimma working set (frigör fysisk RAM)
                Process.GetCurrentProcess().Refresh();

                // Detta är en Windows API som ber OS att minska working set
                if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                {
                    NativeMethods.SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, -1, -1);
                }
            }
            catch (Exception ex)
            {
                LogError("Error trimming working set", ex);
            }
        }

        private long GetCurrentMemoryUsageMB()
        {
            try
            {
                Process currentProcess = Process.GetCurrentProcess();
                currentProcess.Refresh();
                return currentProcess.WorkingSet64 / (1024 * 1024); // Convert to MB
            }
            catch
            {
                return 0;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                Stop();
                _idleTimer?.Stop();
                _memoryCheckTimer?.Stop();
                LogMessage("IdleMemoryManager disposed");
            }
            _disposed = true;
        }

        private void LogMessage(string message)
        {
            try
            {
                string logFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "WowAddonUpdater");

                Directory.CreateDirectory(logFolder);

                string logFile = Path.Combine(logFolder, "memory.log");

                File.AppendAllText(logFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}: {message}\r\n");
            }
            catch
            {
                // Om loggning misslyckas, fortsätt ändå
            }
        }

        private void LogError(string context, Exception ex)
        {
            try
            {
                string logFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "WowAddonUpdater");

                Directory.CreateDirectory(logFolder);

                string logFile = Path.Combine(logFolder, "memory.log");

                File.AppendAllText(logFile,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}: ERROR: {context}: {ex?.Message}\r\n{ex?.StackTrace}\r\n\r\n");
            }
            catch
            {
                // Om loggning misslyckas är det illa
            }
        }
    }

    public class MemoryCleanupEventArgs : EventArgs
    {
        public long MemoryBeforeMB { get; set; }
        public long MemoryAfterMB { get; set; }
        public long FreedMemoryMB { get; set; }
        public string CleanupType { get; set; }
        public long DurationMs { get; set; }
    }

    // Inbyggda metoder för Windows API
    internal static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        internal static extern bool SetProcessWorkingSetSize(IntPtr hProcess, int dwMinimumWorkingSetSize, int dwMaximumWorkingSetSize);
    }
}