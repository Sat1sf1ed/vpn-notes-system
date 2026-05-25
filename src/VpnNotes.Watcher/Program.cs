using Npgsql;
using Serilog;
using System.Diagnostics;
using System.Text;
using VpnNotes.Core.Models;
using VpnNotes.Core.Repositories;
using VpnNotes.Watcher.Collectors;

namespace VpnNotes.Watcher
{
    internal class Program
    {
        static async Task<int> Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            // Настройка логирования в файл
            string baseDir = AppContext.BaseDirectory;
            string logPath = Path.Combine(baseDir, "logs", "watcher-.log");

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.File(
                    logPath,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();

            Log.Information("=== Watcher starting ===");
            Log.Information("Args count: {Count}", args.Length);

            try
            {
                WatcherOptions options = WatcherOptions.Parse(args);
                Log.Information("Parsed options: machine={Machine}, parent={Pid}, interval={Sec}s",
                    options.MachineName, options.ParentPid, options.IntervalSeconds);

                string? password = Environment.GetEnvironmentVariable("VPNNOTES_DB_PASSWORD");
                if (string.IsNullOrEmpty(password))
                {
                    Log.Fatal("VPNNOTES_DB_PASSWORD environment variable not set");
                    return 1;
                }

                CancellationTokenSource cts = new CancellationTokenSource();

                // Запускаем мониторинг родительского процесса в фоне
                _ = Task.Run(() => MonitorParentProcess(options.ParentPid, cts));

                await RunWorkerLoopAsync(options, password, cts.Token);

                Log.Information("=== Watcher stopped ===");
                return 0;
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Watcher crashed with unhandled exception");
                return 1;
            }
            finally
            {
                await Log.CloseAndFlushAsync();
            }
        }

        // === МОНИТОРИНГ РОДИТЕЛЯ ===

        private static void MonitorParentProcess(int parentPid, CancellationTokenSource cts)
        {
            try
            {
                Process parent = Process.GetProcessById(parentPid);
                Log.Information("Monitoring parent process PID={Pid}", parentPid);

                parent.WaitForExit();

                Log.Information("Parent process {Pid} exited, signalling shutdown", parentPid);
            }
            catch (ArgumentException)
            {
                // Процесс уже не существует - возможно, parent умер до того, как мы успели подписаться
                Log.Warning("Parent process {Pid} not found, shutting down", parentPid);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error monitoring parent process");
            }
            finally
            {
                cts.Cancel();
            }
        }

        // === ОСНОВНОЙ ЦИКЛ ===

        private static async Task RunWorkerLoopAsync(
            WatcherOptions options,
            string password,
            CancellationToken cancellationToken)
        {
            string connectionString =
                $"Host={options.DbHost};Port={options.DbPort};" +
                $"Database={options.DbName};" +
                $"Username={options.Username};Password={password}";

            // Сначала находим machine_id по hostname
            int machineId;
            try
            {
                machineId = await ResolveMachineIdAsync(connectionString, options.MachineName, cancellationToken);
                Log.Information("Resolved machine_id={Id} for {Name}", machineId, options.MachineName);
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Failed to resolve machine_id for {Name}", options.MachineName);
                return;
            }

            int iterationCount = 0;

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    MetricsSnapshot snapshot = CollectMetrics(machineId);
                    await SaveMetricsAsync(connectionString, snapshot, cancellationToken);

                    iterationCount++;

                    // Логируем не каждое сохранение, а каждое десятое — чтобы не засорять лог
                    if (iterationCount % 10 == 1)
                    {
                        Log.Information(
                            "Metrics saved (iter #{Count}): CPU={Cpu:F1}%, RAM={Ram}/{RamTotal} MB, Disk={Disk:F1}/{DiskTotal:F1} GB",
                            iterationCount,
                            snapshot.CpuPercent,
                            snapshot.RamUsedMb, snapshot.RamTotalMb,
                            snapshot.DiskUsedGb, snapshot.DiskTotalGb);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed iteration #{Count}", iterationCount + 1);
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(options.IntervalSeconds), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            Log.Information("Worker loop stopped after {Count} iterations", iterationCount);
        }

        // === РЕЗОЛВ machine_id ===

        private static async Task<int> ResolveMachineIdAsync(
            string connectionString,
            string machineName,
            CancellationToken cancellationToken)
        {
            await using NpgsqlConnection connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            MachineRepository repo = new MachineRepository(connection);
            Machine? machine = await repo.GetByHostnameAsync(machineName);

            if (machine == null)
            {
                throw new InvalidOperationException(
                    $"Machine '{machineName}' is not registered in the database. " +
                    $"Insert into 'machines' table first.");
            }

            return machine.Id;
        }

        // === СБОР МЕТРИК ===

        private static MetricsSnapshot CollectMetrics(int machineId)
        {
            float cpu = CpuCollector.Read();
            (int ramUsed, int ramTotal) = MemoryCollector.Read();
            (float diskUsed, float diskTotal) = DiskCollector.Read("C:\\");

            MetricsSnapshot snapshot = new MetricsSnapshot
            {
                MachineId = machineId,
                Timestamp = DateTime.UtcNow,
                CpuPercent = cpu,
                RamUsedMb = ramUsed,
                RamTotalMb = ramTotal,
                DiskUsedGb = diskUsed,
                DiskTotalGb = diskTotal
            };

            return snapshot;
        }

        // === СОХРАНЕНИЕ В БД (метрики + heartbeat в одной транзакции) ===

        private static async Task SaveMetricsAsync(
            string connectionString,
            MetricsSnapshot snapshot,
            CancellationToken cancellationToken)
        {
            await using NpgsqlConnection connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

            try
            {
                MetricsRepository metricsRepo = new MetricsRepository(connection);
                await metricsRepo.SaveSnapshotAsync(snapshot, transaction);

                MachineRepository machineRepo = new MachineRepository(connection);
                await machineRepo.UpdateLastSeenAsync(snapshot.MachineId, transaction);

                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }
    }
}
