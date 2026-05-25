using System.Diagnostics;
using System.Text;

namespace VpnNotes.Updater
{
    internal class Program
    {
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            try
            {
                string? sourceDir = ExtractArg(args, "--source");
                string? targetDir = ExtractArg(args, "--target");
                string? parentPidStr = ExtractArg(args, "--parent-pid");
                bool launchAfter = args.Contains("--launch-after");

                if (sourceDir == null || targetDir == null || parentPidStr == null)
                {
                    Console.WriteLine("Usage: VpnNotes.Updater.exe --source <dir> --target <dir> --parent-pid <pid> [--launch-after]");
                    return 1;
                }

                int parentPid = int.Parse(parentPidStr);

                Console.WriteLine("=== VPN Notes Updater ===");
                Console.WriteLine($"Source:     {sourceDir}");
                Console.WriteLine($"Target:     {targetDir}");
                Console.WriteLine($"Parent PID: {parentPid}");
                Console.WriteLine();

                WaitForProcessesToExit(parentPid);

                Console.WriteLine("Copying files...");
                CopyDirectory(sourceDir, targetDir);
                Console.WriteLine("Files copied successfully.");

                Console.WriteLine("Updating config version...");
                UpdateConfigVersion(targetDir, sourceDir);
                Console.WriteLine("Config updated.");

                CleanupTempFolder(sourceDir);

                if (launchAfter)
                {
                    Console.WriteLine("Launching notes.exe...");
                    LaunchMainApp(targetDir);
                }

                Console.WriteLine();
                Console.WriteLine("Update complete. This window will close in 2 seconds.");
                Thread.Sleep(2000);

                SelfDelete();
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine($"ERROR: {ex.Message}");
                Console.WriteLine();
                Console.WriteLine("Press any key to close...");
                Console.ReadKey();
                return 1;
            }
        }

        // === ОЖИДАНИЕ ЗАВЕРШЕНИЯ ПРОЦЕССОВ ===

        private static void WaitForProcessesToExit(int parentPid)
        {
            Console.WriteLine($"Waiting for parent process (PID {parentPid}) to exit...");
            try
            {
                Process parent = Process.GetProcessById(parentPid);
                parent.WaitForExit(30000);
                Console.WriteLine("Parent process exited.");
            }
            catch (ArgumentException)
            {
                Console.WriteLine("Parent process already gone.");
            }

            Console.WriteLine("Waiting for related processes to exit...");
            string[] processNames = new string[]
            {
                "VpnNotes.Cli",
                "notes",
                "VpnNotes.Watcher",
                "vpnnotes-watcher"
            };

            foreach (string name in processNames)
            {
                Process[] processes = Process.GetProcessesByName(name);
                foreach (Process p in processes)
                {
                    try
                    {
                        Console.WriteLine($"  Waiting for {p.ProcessName} (PID {p.Id})...");
                        p.WaitForExit(15000);
                    }
                    catch
                    {
                        // Игнорируем — процесс мог сам завершиться
                    }
                }
            }

            // Дополнительная задержка, чтобы ОС освободила файловые хэндлы
            Thread.Sleep(1500);
            Console.WriteLine("All processes exited.");
            Console.WriteLine();
        }

        // === КОПИРОВАНИЕ ФАЙЛОВ ===

        private static void CopyDirectory(string source, string target)
        {
            Directory.CreateDirectory(target);

            string[] sourceFiles = Directory.GetFiles(source, "*", SearchOption.AllDirectories);

            foreach (string sourceFile in sourceFiles)
            {
                string relativePath = Path.GetRelativePath(source, sourceFile);
                string destPath = Path.Combine(target, relativePath);

                string fileName = Path.GetFileName(destPath).ToLower();

                // Не перезаписываем сам updater, пока он работает
                if (fileName.Contains("updater"))
                {
                    Console.WriteLine($"  Skipping (in use): {relativePath}");
                    continue;
                }

                // Не перезаписываем app-config.yml — там настройки пользователя
                if (fileName == "app-config.yml")
                {
                    Console.WriteLine($"  Skipping (user config): {relativePath}");
                    continue;
                }

                string? destDir = Path.GetDirectoryName(destPath);
                if (destDir != null)
                {
                    Directory.CreateDirectory(destDir);
                }

                try
                {
                    File.Copy(sourceFile, destPath, overwrite: true);
                    Console.WriteLine($"  Copied: {relativePath}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  Failed to copy {relativePath}: {ex.Message}");
                }
            }
        }

        // === ОБНОВЛЕНИЕ current_version В YML ===

        private static void UpdateConfigVersion(string targetDir, string sourceDir)
        {
            string configPath = Path.Combine(targetDir, "app-config.yml");
            if (!File.Exists(configPath))
            {
                Console.WriteLine("  Config file not found, skipping version update.");
                return;
            }

            // Узнаём новую версию из exe-шника
            string newVersion = "1.0.0";
            string notesExePath = Path.Combine(sourceDir, "VpnNotes.Cli.exe");
            if (!File.Exists(notesExePath))
            {
                notesExePath = Path.Combine(sourceDir, "notes.exe");
            }

            if (File.Exists(notesExePath))
            {
                FileVersionInfo info = FileVersionInfo.GetVersionInfo(notesExePath);
                if (!string.IsNullOrEmpty(info.ProductVersion))
                {
                    // ProductVersion может быть вида "1.2.0+build123" - берём только часть до '+'
                    string version = info.ProductVersion.Split('+')[0];
                    newVersion = version;
                }
                else if (!string.IsNullOrEmpty(info.FileVersion))
                {
                    newVersion = info.FileVersion;
                }
            }

            // Простая замена в yml без YAML-парсера, чтобы не тащить зависимости
            string[] lines = File.ReadAllLines(configPath);
            for (int i = 0; i < lines.Length; i++)
            {
                string trimmed = lines[i].TrimStart();
                if (trimmed.StartsWith("current_version:"))
                {
                    int indent = lines[i].Length - trimmed.Length;
                    lines[i] = new string(' ', indent) + $"current_version: \"{newVersion}\"";
                    break;
                }
            }
            File.WriteAllLines(configPath, lines);
            Console.WriteLine($"  Set current_version: \"{newVersion}\"");
        }

        // === ОЧИСТКА ВРЕМЕННЫХ ФАЙЛОВ ===

        private static void CleanupTempFolder(string sourceDir)
        {
            try
            {
                Directory.Delete(sourceDir, recursive: true);
                Console.WriteLine("Temporary files cleaned up.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: failed to clean temp folder: {ex.Message}");
            }
        }

        // === ЗАПУСК ОСНОВНОГО ПРИЛОЖЕНИЯ ===

        private static void LaunchMainApp(string targetDir)
        {
            // Пробуем разные варианты имени exe
            string[] candidates = new string[]
            {
                "notes.exe",
                "VpnNotes.Cli.exe"
            };

            foreach (string candidate in candidates)
            {
                string fullPath = Path.Combine(targetDir, candidate);
                if (File.Exists(fullPath))
                {
                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = fullPath,
                        UseShellExecute = true,
                        WorkingDirectory = targetDir
                    };
                    Process.Start(psi);
                    Console.WriteLine($"Launched: {candidate}");
                    return;
                }
            }

            Console.WriteLine("Warning: could not find notes.exe to launch.");
        }

        // === САМОУНИЧТОЖЕНИЕ ===

        private static void SelfDelete()
        {
            try
            {
                string tempBat = Path.Combine(
                    Path.GetTempPath(),
                    $"vpnnotes-cleanup-{Guid.NewGuid()}.bat");

                string? currentExe = Environment.ProcessPath;
                if (currentExe == null)
                {
                    return;
                }

                string batContent =
                    "@echo off\r\n" +
                    "timeout /t 2 /nobreak >nul\r\n" +
                    $"del \"{currentExe}\" >nul 2>&1\r\n" +
                    $"del \"%~f0\" >nul 2>&1\r\n";

                File.WriteAllText(tempBat, batContent);

                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = tempBat,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                Process.Start(psi);
            }
            catch
            {
                // Не критично, если не получилось
            }
        }

        // === ХЕЛПЕРЫ ===

        private static string? ExtractArg(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name)
                {
                    return args[i + 1];
                }
            }
            return null;
        }
    }
}