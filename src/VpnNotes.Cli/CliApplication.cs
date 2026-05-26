using Npgsql;
using System;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using VpnNotes.Core.Configuration;
using VpnNotes.Core.Models;
using VpnNotes.Core.Repositories;
using VpnNotes.Updater.Lib;

namespace VpnNotes.Cli
{
    public class CliApplication
    {
        private readonly string _configPath;
        private NpgsqlConnection? _connection;
        private string? _currentUsername;
        private string? _password;
        private bool _running = true;
        private UserRole _currentRole;
        private Process? _watcherProcess;
        private UpdateInfo? _availableUpdate;

        public CliApplication(string configPath)
        {
            _configPath = configPath;

            Console.CancelKeyPress += (sender, eventArgs) =>
            {
                eventArgs.Cancel = true;
                _running = false;
            };
        }

        public async Task<int> RunAsync()
        {
            string version = GetVersion();
            Console.WriteLine($"VPN Notes System v{version}");
            Console.WriteLine();

            bool loginOk = await LoginAsync();
            if (!loginOk)
            {
                return 1;
            }
            StartWatcher();
            _ = Task.Run(CheckForUpdatesInBackgroundAsync);
            await RunReplAsync();
            Shutdown();
            return 0;
        }

        // === ЛОГИН ===

        private async Task<bool> LoginAsync()
        {
            Console.Write("Username: ");
            string? rawUsername = Console.ReadLine();
            string username = rawUsername?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(username))
            {
                Console.WriteLine("Username cannot be empty.");
                return false;
            }

            Console.Write("Password: ");
            string password = ReadPasswordMasked();
            if (string.IsNullOrEmpty(password))
            {
                Console.WriteLine("Password cannot be empty.");
                return false;
            }

            AppConfig config = AppConfig.Load(_configPath);
            string connectionString =
                $"Host={config.Database.Host};Port={config.Database.Port};" +
                $"Database={config.Database.Database};" +
                $"Username={username};Password={password}";

            try
            {
                _connection = new NpgsqlConnection(connectionString);
                await _connection.OpenAsync();
                _currentUsername = username;
                _password = password;

                // === НОВОЕ: получение роли пользователя ===
                UserRepository userRepo = new UserRepository(_connection);
                User? user = await userRepo.GetByUsernameAsync(username);
                if (user == null)
                {
                    Console.WriteLine();
                    Console.WriteLine($"Login failed: user '{username}' is not registered in the system.");
                    Console.WriteLine("Contact administrator.");
                    await _connection.CloseAsync();
                    return false;
                }
                _currentRole = user.Role;

                Console.WriteLine();
                Console.WriteLine($"Connected to database.");
                Console.WriteLine($"Logged in as: {user.Username} (role: {user.Role.ToDbString()})");
                return true;
            }
            catch (NpgsqlException ex)
            {
                Console.WriteLine($"Login failed: {ex.Message}");
                return false;
            }
        }

        private static string ReadPasswordMasked()
        {
            StringBuilder builder = new StringBuilder();

            while (true)
            {
                ConsoleKeyInfo key = Console.ReadKey(intercept: true);

                if (key.Key == ConsoleKey.Enter)
                {
                    Console.WriteLine();
                    return builder.ToString();
                }

                if (key.Key == ConsoleKey.Backspace)
                {
                    if (builder.Length > 0)
                    {
                        builder.Length--;
                        Console.Write("\b \b");
                    }
                    continue;
                }

                if (!char.IsControl(key.KeyChar))
                {
                    builder.Append(key.KeyChar);
                    Console.Write("*");
                }
            }
        }

        // === REPL ===

        private async Task RunReplAsync()
        {
            Console.WriteLine("Type 'help' for commands, 'exit' to quit.");
            Console.WriteLine();

            while (_running)
            {
                Console.Write("notes> ");
                string? input = Console.ReadLine();
                if (input == null)
                {
                    break;
                }
                input = input.Trim();
                if (string.IsNullOrEmpty(input))
                {
                    continue;
                }

                try
                {
                    await ExecuteCommandAsync(input);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: {ex.Message}");
                }
            }
        }

        private async Task ExecuteCommandAsync(string input)
        {
            string[] args = SplitArgs(input);
            if (args.Length == 0)
            {
                return;
            }

            string command = args[0].ToLower();
            string[] rest = args.Skip(1).ToArray();

            // Проверка прав на команду
            if (!IsCommandAllowed(command))
            {
                Console.WriteLine($"Command '{command}' is not allowed for role '{_currentRole.ToDbString()}'.");
                return;
            }

            switch (command)
            {
                case "add":
                    await AddNoteAsync(rest);
                    break;
                case "list":
                    await ListNotesAsync(rest);
                    break;
                case "show":
                    await ShowNoteAsync(rest);
                    break;
                case "delete":
                    await DeleteNoteAsync(rest);
                    break;
                case "search":
                    await SearchNotesAsync(rest);
                    break;
                case "machines":
                    await ShowMachinesAsync();
                    break;
                case "addmachine":          // ← новое
                    await AddMachineAsync(rest);
                    break;
                case "stats":
                    await ShowStatsAsync(rest);
                    break;
                case "update":
                    await RunUpdateAsync();
                    break;
                case "users":           // ← новое
                    await ShowUsersAsync();
                    break;
                case "adduser":         // ← новое
                    await AddUserAsync(rest);
                    break;
                case "deluser":         // ← новое
                    await DeleteUserAsync(rest);
                    break;
                case "logs": 
                    await ShowLogsAsync(args); 
                    break;
                case "help":
                    ShowHelp();
                    break;
                case "exit":
                case "quit":
                    _running = false;
                    break;
                default:
                    Console.WriteLine($"Unknown command: {command}. Type 'help' for available commands.");
                    break;
            }
        }

        private bool IsCommandAllowed(string command)
        {
            // Команды доступные всем
            string[] commonCommands = new string[]
            {
        "update", "help", "exit", "quit"
            };
            if (commonCommands.Contains(command))
            {
                return true;
            }

            // === Команды работы с заметками ===
            // Доступны всем ролям (но видны только свои заметки благодаря RLS)
            string[] notesCommands = new string[]
            {
        "add", "list", "show", "delete", "search"
            };
            if (notesCommands.Contains(command))
            {
                return _currentRole == UserRole.User
                    || _currentRole == UserRole.Stats
                    || _currentRole == UserRole.Admin;
            }

            // === Команды работы с метриками и машинами ===
            string[] metricsCommands = new string[]
            {
        "machines", "stats", "addmachine"
            };
            if (metricsCommands.Contains(command))
            {
                return _currentRole == UserRole.Stats || _currentRole == UserRole.Admin;
            }

            // === Админские команды ===
            string[] adminCommands = new string[]
            {
        "users", "adduser", "deluser", "logs"
            };
            if (adminCommands.Contains(command))
            {
                return _currentRole == UserRole.Admin;
            }

            return false;
        }
        private async Task SearchNotesAsync(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Usage: search \"<query>\"");
                return;
            }

            string query = args[0];
            if (string.IsNullOrWhiteSpace(query))
            {
                Console.WriteLine("Search query cannot be empty");
                return;
            }

            NoteRepository repo = new NoteRepository(_connection!);
            List<Note> notes = await repo.SearchAsync(query, limit: 50);

            if (notes.Count == 0)
            {
                Console.WriteLine("No notes found.");
                return;
            }

            Console.WriteLine();
            Console.WriteLine($"Found {notes.Count} notes:");
            Console.WriteLine();
            Console.WriteLine($"{"ID",-4} {"Created",-20} {"Created by",-12} Text");
            Console.WriteLine(new string('-', 80));

            foreach (Note note in notes)
            {
                string preview = note.Text.Length > 40
                    ? note.Text.Substring(0, 40) + "..."
                    : note.Text;
                string created = note.CreatedAt.ToString("yyyy-MM-dd HH:mm");
                Console.WriteLine($"{note.Id,-4} {created,-20} {note.CreatedBy,-12} {preview}");
            }
            Console.WriteLine();
        }
        private async Task ShowStatsAsync(string[] args)
        {
            string? machineName = ExtractOption(args, "--machine");
            if (machineName == null)
            {
                throw new ArgumentException("--machine is required. Usage: stats --machine <hostname>");
            }

            MachineRepository machineRepo = new MachineRepository(_connection!);
            Machine? machine = await machineRepo.GetByHostnameAsync(machineName);

            if (machine == null)
            {
                throw new InvalidOperationException($"Machine not found: {machineName}");
            }

            MetricsRepository metricsRepo = new MetricsRepository(_connection!);
            MetricsSnapshot? latest = await metricsRepo.GetLatestAsync(machine.Id);

            if (latest == null)
            {
                Console.WriteLine($"No metrics available for machine '{machineName}'.");
                Console.WriteLine("Watcher may not have collected any data yet.");
                return;
            }

            float ramPercent = latest.RamTotalMb > 0
                ? 100f * latest.RamUsedMb / latest.RamTotalMb
                : 0f;

            float diskPercent = latest.DiskTotalGb > 0
                ? 100f * latest.DiskUsedGb / latest.DiskTotalGb
                : 0f;

            TimeSpan age = DateTime.UtcNow - latest.Timestamp;

            Console.WriteLine();
            Console.WriteLine($"Latest metrics for '{machineName}':");
            Console.WriteLine();
            Console.WriteLine($"  CPU:  {latest.CpuPercent:F1}%");
            Console.WriteLine($"  RAM:  {latest.RamUsedMb} / {latest.RamTotalMb} MB ({ramPercent:F1}%)");
            Console.WriteLine($"  Disk: {latest.DiskUsedGb:F1} / {latest.DiskTotalGb:F1} GB ({diskPercent:F1}%)");
            Console.WriteLine();
            Console.WriteLine($"  Recorded: {latest.Timestamp:yyyy-MM-dd HH:mm:ss} UTC ({FormatDuration(age)} ago)");
            Console.WriteLine();
        }
        private async Task ShowMachinesAsync()
        {
            MachineRepository repo = new MachineRepository(_connection!);
            List<Machine> machines = await repo.GetAllAsync();

            if (machines.Count == 0)
            {
                Console.WriteLine("No machines registered.");
                return;
            }

            DateTime now = DateTime.UtcNow;

            Console.WriteLine();
            Console.WriteLine($"Registered machines: {machines.Count}");
            Console.WriteLine();

            foreach (Machine machine in machines)
            {
                string status;
                string lastSeenStr;

                if (!machine.LastSeen.HasValue)
                {
                    status = "never seen";
                    lastSeenStr = "never";
                }
                else
                {
                    TimeSpan inactiveFor = now - machine.LastSeen.Value;

                    if (inactiveFor.TotalMinutes < 2)
                    {
                        status = "online";
                    }
                    else if (inactiveFor.TotalMinutes < 10)
                    {
                        status = "stale";
                    }
                    else
                    {
                        status = "offline";
                    }

                    lastSeenStr = $"{machine.LastSeen.Value:yyyy-MM-dd HH:mm:ss} " +
                                  $"({FormatDuration(inactiveFor)} ago)";
                }

                Console.WriteLine($"  {machine.Hostname}");
                Console.WriteLine($"    status:    {status}");
                Console.WriteLine($"    last seen: {lastSeenStr}");
                Console.WriteLine();
            }
        }

        private static string FormatDuration(TimeSpan ts)
        {
            if (ts.TotalSeconds < 60)
            {
                return $"{(int)ts.TotalSeconds}s";
            }
            if (ts.TotalMinutes < 60)
            {
                return $"{(int)ts.TotalMinutes}m";
            }
            if (ts.TotalHours < 24)
            {
                return $"{(int)ts.TotalHours}h";
            }
            return $"{(int)ts.TotalDays}d";
        }
        // === ПАРСЕР АРГУМЕНТОВ С УЧЁТОМ КАВЫЧЕК ===

        private static string[] SplitArgs(string input)
        {
            List<string> result = new List<string>();
            StringBuilder current = new StringBuilder();
            bool inQuotes = false;

            foreach (char ch in input)
            {
                if (ch == '"')
                {
                    inQuotes = !inQuotes;
                    continue;
                }

                if (char.IsWhiteSpace(ch) && !inQuotes)
                {
                    if (current.Length > 0)
                    {
                        result.Add(current.ToString());
                        current.Clear();
                    }
                }
                else
                {
                    current.Append(ch);
                }
            }

            if (current.Length > 0)
            {
                result.Add(current.ToString());
            }

            return result.ToArray();
        }

        private static string? ExtractOption(string[] args, string optionName)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == optionName)
                {
                    return args[i + 1];
                }
            }
            return null;
        }

        // === КОМАНДЫ ===

        private async Task AddNoteAsync(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Usage: add <text> [--tag X] [--machine Y]");
                return;
            }

            string text = args[0];
            if (string.IsNullOrWhiteSpace(text))
            {
                Console.WriteLine("Note text cannot be empty");
                return;
            }

            // Парсим теги (может быть несколько --tag)
            List<string> tagsList = new List<string>();
            for (int i = 1; i < args.Length - 1; i++)
            {
                if (args[i] == "--tag")
                {
                    tagsList.Add(args[i + 1]);
                }
            }
            string[]? tags = tagsList.Count > 0 ? tagsList.ToArray() : null;

            // Парсим машину (опционально)
            int? machineId = null;
            string? machineName = ExtractOption(args, "--machine");
            if (!string.IsNullOrEmpty(machineName))
            {
                MachineRepository machineRepo = new MachineRepository(_connection!);
                Machine? machine = await machineRepo.GetByHostnameAsync(machineName);
                if (machine == null)
                {
                    Console.WriteLine($"Machine '{machineName}' not found. Use 'addmachine' first.");
                    return;
                }
                machineId = machine.Id;
            }

            // Создание — БД сама подставит created_by = current_user
            NoteRepository repo = new NoteRepository(_connection!);
            int newId = await repo.AddAsync(machineId, text, tags);

            Console.WriteLine($"Note created with ID {newId}.");
        }

        private async Task ListNotesAsync(string[] args)
        {
            int limit = 20;
            string? lastStr = ExtractOption(args, "--last");
            if (lastStr != null)
            {
                if (!int.TryParse(lastStr, out limit) || limit < 1 || limit > 1000)
                {
                    Console.WriteLine("--last must be a number between 1 and 1000");
                    return;
                }
            }

            NoteRepository repo = new NoteRepository(_connection!);
            List<Note> notes = await repo.ListAsync(limit);

            if (notes.Count == 0)
            {
                Console.WriteLine("No notes found.");
                return;
            }

            Console.WriteLine();
            Console.WriteLine($"{"ID",-4} {"Created",-20} {"Created by",-12} Text");
            Console.WriteLine(new string('-', 80));

            foreach (Note note in notes)
            {
                string preview = note.Text.Length > 40
                    ? note.Text.Substring(0, 40) + "..."
                    : note.Text;
                string created = note.CreatedAt.ToString("yyyy-MM-dd HH:mm");
                Console.WriteLine($"{note.Id,-4} {created,-20} {note.CreatedBy,-12} {preview}");
            }
            Console.WriteLine();
        }
        private async Task ShowNoteAsync(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Usage: show <id>");
                return;
            }

            if (!int.TryParse(args[0], out int id))
            {
                Console.WriteLine("Invalid ID");
                return;
            }

            NoteRepository repo = new NoteRepository(_connection!);
            Note? note = await repo.GetByIdAsync(id);

            if (note == null)
            {
                Console.WriteLine($"Note {id} not found.");
                return;
            }

            Console.WriteLine();
            Console.WriteLine($"Note #{note.Id}");
            Console.WriteLine($"  Created:    {note.CreatedAt:yyyy-MM-dd HH:mm:ss} UTC");
            Console.WriteLine($"  Created by: {note.CreatedBy}");

            if (note.MachineId.HasValue)
            {
                MachineRepository machineRepo = new MachineRepository(_connection!);
                Machine? machine = await machineRepo.GetByIdAsync(note.MachineId.Value);
                if (machine != null)
                {
                    Console.WriteLine($"  Machine:    {machine.Hostname}");
                }
            }

            if (note.Tags != null && note.Tags.Length > 0)
            {
                Console.WriteLine($"  Tags:       {string.Join(", ", note.Tags)}");
            }

            Console.WriteLine();
            Console.WriteLine("  Text:");
            Console.WriteLine($"  {note.Text}");
            Console.WriteLine();
        }
        private async Task DeleteNoteAsync(string[] args)
        {
            if (args.Length == 0)
            {
                throw new ArgumentException("Note ID is required. Usage: delete <id>");
            }

            if (!int.TryParse(args[0], out int id))
            {
                throw new ArgumentException($"Invalid ID: {args[0]}");
            }

            Console.Write($"Delete note {id}? (y/N): ");
            string? answer = Console.ReadLine()?.Trim().ToLower();
            if (answer != "y" && answer != "yes")
            {
                Console.WriteLine("Cancelled.");
                return;
            }

            NoteRepository noteRepo = new NoteRepository(_connection!);
            bool deleted = await noteRepo.DeleteAsync(id);

            if (deleted)
            {
                Console.WriteLine($"Note {id} deleted.");
            }
            else
            {
                Console.WriteLine($"Note {id} not found.");
            }
        }

        private void ShowHelp()
        {
            Console.WriteLine();
            Console.WriteLine("Available commands:");
            Console.WriteLine();

            // Команды работы с заметками — теперь доступны всем ролям
            Console.WriteLine("  --- Notes ---");
            Console.WriteLine("  add <text> [--tag X] [--machine Y]    Create a new note");
            Console.WriteLine("  list [--last N]                       Show notes (default last 20)");
            Console.WriteLine("  show <id>                             Show note details");
            Console.WriteLine("  delete <id>                           Delete a note");
            Console.WriteLine("  search \"<query>\"                      Search notes by text");
            Console.WriteLine();

            // Команды мониторинга (stats и admin)
            if (_currentRole == UserRole.Stats || _currentRole == UserRole.Admin)
            {
                Console.WriteLine("  --- Monitoring ---");
                Console.WriteLine("  machines                              Show all machines with status");
                Console.WriteLine("  stats --machine <hostname>            Show latest metrics for a machine");
                Console.WriteLine("  addmachine <hostname>                 Register a new machine");
                Console.WriteLine();
            }

            // Админские команды
            if (_currentRole == UserRole.Admin)
            {
                Console.WriteLine("  --- Administration ---");
                Console.WriteLine("  users                                 Show all users in the system");
                Console.WriteLine("  adduser <username> <role> [--name X]  Create a new user");
                Console.WriteLine("                                        Roles: user, stats, admin");
                Console.WriteLine("  deluser <username>                    Delete a user");
                Console.WriteLine("  logs [--last N] [--level X] [--date]  Show Watcher logs");
                Console.WriteLine();
            }

            // Общие команды для всех ролей
            Console.WriteLine("  --- Common ---");
            Console.WriteLine("  update                                Check and install update from GitHub");
            Console.WriteLine("  help                                  Show this help");
            Console.WriteLine("  exit                                  Exit the application");
            Console.WriteLine();
            Console.WriteLine("Wrap text with spaces in double quotes: add \"Hello world\"");
            Console.WriteLine();
        }

        // === SHUTDOWN ===

        private void Shutdown()
        {
            if (_watcherProcess != null && !_watcherProcess.HasExited)
            {
                try
                {
                    _watcherProcess.Kill(entireProcessTree: true);
                    _watcherProcess.WaitForExit(5000);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: failed to stop watcher cleanly: {ex.Message}");
                }
            }

            _connection?.Dispose();
            Console.WriteLine("Goodbye.");
        }

        private static string GetVersion()
        {
            Version? version = Assembly.GetExecutingAssembly().GetName().Version;
            return version?.ToString(3) ?? "0.0.0";
        }
        private void StartWatcher()
        {
            string baseDir = AppContext.BaseDirectory;
            string watcherExe = Path.Combine(baseDir, "VpnNotes.Watcher.exe");

            if (!File.Exists(watcherExe))
            {
                Console.WriteLine("Warning: watcher executable not found. Metrics will not be collected.");
                Console.WriteLine($"Expected location: {watcherExe}");
                return;
            }

            AppConfig config = AppConfig.Load(_configPath);

            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = watcherExe,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            psi.ArgumentList.Add("--db-host");
            psi.ArgumentList.Add(config.Database.Host);
            psi.ArgumentList.Add("--db-port");
            psi.ArgumentList.Add(config.Database.Port.ToString());
            psi.ArgumentList.Add("--db-name");
            psi.ArgumentList.Add(config.Database.Database);
            psi.ArgumentList.Add("--username");
            psi.ArgumentList.Add(_currentUsername!);
            psi.ArgumentList.Add("--machine-name");
            psi.ArgumentList.Add(Environment.MachineName);
            psi.ArgumentList.Add("--parent-pid");
            psi.ArgumentList.Add(Environment.ProcessId.ToString());
            psi.ArgumentList.Add("--interval");
            psi.ArgumentList.Add(config.Watcher.MetricsIntervalSeconds.ToString());

            psi.EnvironmentVariables["VPNNOTES_DB_PASSWORD"] = _password!;

            try
            {
                _watcherProcess = Process.Start(psi);
                if (_watcherProcess == null)
                {
                    Console.WriteLine("Warning: failed to start watcher (Process.Start returned null).");
                    return;
                }
                Console.WriteLine($"Watcher started (PID {_watcherProcess.Id}).");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: failed to start watcher: {ex.Message}");
                _watcherProcess = null;
            }
        }
        private async Task CheckForUpdatesInBackgroundAsync()
        {
            try
            {
                AppConfig config = AppConfig.Load(_configPath);

                if (string.IsNullOrEmpty(config.Update.GitHub.Owner) ||
                    string.IsNullOrEmpty(config.Update.GitHub.Repo))
                {
                    return;
                }

                if (!Version.TryParse(config.Update.CurrentVersion, out Version? currentVersion))
                {
                    return;
                }

                GitHubUpdater updater = new GitHubUpdater(
                    config.Update.GitHub.Owner,
                    config.Update.GitHub.Repo);

                _availableUpdate = await updater.CheckAsync(currentVersion);

                if (_availableUpdate != null)
                {
                    Console.WriteLine();
                    Console.WriteLine($"*** New version {_availableUpdate.Version} available. Run 'update' to install. ***");
                    Console.Write("notes> ");
                }
            }
            catch
            {
                // Фоновая проверка не должна валить основную работу
            }
        }
        private async Task RunUpdateAsync()
        {
            AppConfig config = AppConfig.Load(_configPath);

            if (string.IsNullOrEmpty(config.Update.GitHub.Owner) ||
                string.IsNullOrEmpty(config.Update.GitHub.Repo))
            {
                Console.WriteLine("GitHub repository is not configured in app-config.yml");
                return;
            }

            GitHubUpdater updater = new GitHubUpdater(
                config.Update.GitHub.Owner,
                config.Update.GitHub.Repo);

            UpdateInfo? info = _availableUpdate;
            if (info == null)
            {
                Console.WriteLine("Checking for updates...");

                if (!Version.TryParse(config.Update.CurrentVersion, out Version? currentVersion))
                {
                    Console.WriteLine($"Invalid current_version in config: {config.Update.CurrentVersion}");
                    return;
                }

                info = await updater.CheckAsync(currentVersion);
                if (info == null)
                {
                    Console.WriteLine("You are on the latest version.");
                    return;
                }
            }

            Console.WriteLine($"New version available: {info.Version}");
            Console.WriteLine($"Download size: {info.Size / 1024 / 1024} MB");
            Console.Write("Proceed with installation? (y/N): ");
            string? answer = Console.ReadLine()?.Trim().ToLower();
            if (answer != "y" && answer != "yes")
            {
                Console.WriteLine("Update cancelled.");
                return;
            }

            Console.WriteLine();
            Console.WriteLine("Downloading...");
            string zipPath;
            try
            {
                zipPath = await updater.DownloadAsync(info);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Download failed: {ex.Message}");
                return;
            }

            Console.WriteLine("Extracting archive...");
            string extractedDir;
            try
            {
                extractedDir = updater.ExtractArchive(zipPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to extract archive: {ex.Message}");
                return;
            }

            Console.WriteLine("Launching updater. The application will restart automatically.");
            Console.WriteLine();

            string baseDir = AppContext.BaseDirectory;
            string updaterExe = Path.Combine(baseDir, "VpnNotes.Updater.exe");
            if (!File.Exists(updaterExe))
            {
                Console.WriteLine($"Updater not found at: {updaterExe}");
                return;
            }

            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = updaterExe,
                UseShellExecute = true,
                WorkingDirectory = baseDir
            };
            psi.ArgumentList.Add("--source");
            psi.ArgumentList.Add(extractedDir);
            psi.ArgumentList.Add("--target");
            psi.ArgumentList.Add(baseDir);
            psi.ArgumentList.Add("--parent-pid");
            psi.ArgumentList.Add(Environment.ProcessId.ToString());
            psi.ArgumentList.Add("--launch-after");

            Process.Start(psi);

            // CLI завершается, Watcher завершится за ним, Updater дождётся обоих
            Environment.Exit(0);
        }
        private async Task ShowUsersAsync()
        {
            UserRepository repo = new UserRepository(_connection!);
            List<User> users = await repo.GetAllAsync();

            if (users.Count == 0)
            {
                Console.WriteLine("No users found.");
                return;
            }

            Console.WriteLine();
            Console.WriteLine($"Total users: {users.Count}");
            Console.WriteLine();

            foreach (User user in users)
            {
                string fullName = user.FullName ?? "-";
                string createdBy = user.CreatedBy ?? "-";

                Console.WriteLine($"  {user.Username}");
                Console.WriteLine($"    role:       {user.Role.ToDbString()}");
                Console.WriteLine($"    full name:  {fullName}");
                Console.WriteLine($"    created:    {user.CreatedAt:yyyy-MM-dd HH:mm:ss}");
                Console.WriteLine($"    created by: {createdBy}");
                Console.WriteLine();
            }
        }
        private async Task AddUserAsync(string[] args)
        {
            if (args.Length < 2)
            {
                throw new ArgumentException(
                    "Usage: adduser <username> <role> [--name \"Full Name\"]\n" +
                    "  role: user | stats | admin");
            }

            string newUsername = args[0];
            string roleString = args[1].ToLower();

            // Валидация username
            if (newUsername.Length < 3 || newUsername.Length > 63)
            {
                throw new ArgumentException("Username must be 3-63 characters long");
            }

            foreach (char c in newUsername)
            {
                if (!char.IsLetterOrDigit(c) && c != '_')
                {
                    throw new ArgumentException(
                        "Username can only contain letters, digits and underscores");
                }
            }

            // Парсинг роли
            UserRole role;
            switch (roleString)
            {
                case "user": role = UserRole.User; break;
                case "stats": role = UserRole.Stats; break;
                case "admin": role = UserRole.Admin; break;
                default:
                    throw new ArgumentException(
                        $"Invalid role: '{roleString}'. Must be user, stats or admin.");
            }

            string? fullName = ExtractOption(args, "--name");

            // Проверка существования
            UserRepository repo = new UserRepository(_connection!);
            User? existing = await repo.GetByUsernameAsync(newUsername);
            if (existing != null)
            {
                throw new InvalidOperationException($"User '{newUsername}' already exists");
            }

            // Запрос пароля для нового пользователя
            Console.Write($"Enter password for {newUsername}: ");
            string password = ReadPasswordMasked();
            if (password.Length < 6)
            {
                Console.WriteLine("Password must be at least 6 characters long.");
                return;
            }

            Console.Write("Confirm password: ");
            string confirmPassword = ReadPasswordMasked();
            if (password != confirmPassword)
            {
                Console.WriteLine("Passwords do not match.");
                return;
            }

            // Создание
            try
            {
                await repo.CreateAsync(newUsername, password, role, fullName, _currentUsername!);
                Console.WriteLine($"User '{newUsername}' created with role '{role.ToDbString()}'.");
            }
            catch (PostgresException ex)
            {
                Console.WriteLine($"Failed to create user: {ex.Message}");
            }
        }
        private async Task DeleteUserAsync(string[] args)
        {
            if (args.Length == 0)
            {
                throw new ArgumentException("Usage: deluser <username>");
            }

            string targetUsername = args[0];

            // Защита от удаления самого себя
            if (targetUsername == _currentUsername)
            {
                Console.WriteLine("You cannot delete your own account.");
                return;
            }

            UserRepository repo = new UserRepository(_connection!);
            User? target = await repo.GetByUsernameAsync(targetUsername);
            if (target == null)
            {
                Console.WriteLine($"User '{targetUsername}' not found.");
                return;
            }

            Console.WriteLine($"About to delete user:");
            Console.WriteLine($"  username: {target.Username}");
            Console.WriteLine($"  role:     {target.Role.ToDbString()}");
            Console.WriteLine($"  name:     {target.FullName ?? "-"}");
            Console.Write("Confirm deletion? (y/N): ");
            string? answer = Console.ReadLine()?.Trim().ToLower();
            if (answer != "y" && answer != "yes")
            {
                Console.WriteLine("Cancelled.");
                return;
            }

            try
            {
                await repo.DeleteAsync(targetUsername);
                Console.WriteLine($"User '{targetUsername}' deleted.");
            }
            catch (PostgresException ex)
            {
                Console.WriteLine($"Failed to delete user: {ex.Message}");
            }
        }
        private async Task AddMachineAsync(string[] args)
        {
            if (args.Length == 0)
            {
                throw new ArgumentException("Usage: addmachine <hostname>");
            }

            string hostname = args[0];

            // Валидация hostname
            if (hostname.Length < 1 || hostname.Length > 255)
            {
                throw new ArgumentException("Hostname must be 1-255 characters long");
            }

            // Разрешаем буквы, цифры, точки, дефисы — стандартный формат hostname
            foreach (char c in hostname)
            {
                if (!char.IsLetterOrDigit(c) && c != '.' && c != '-' && c != '_')
                {
                    throw new ArgumentException(
                        "Hostname can only contain letters, digits, dots, hyphens and underscores");
                }
            }

            // Проверка существования
            MachineRepository repo = new MachineRepository(_connection!);
            Machine? existing = await repo.GetByHostnameAsync(hostname);
            if (existing != null)
            {
                Console.WriteLine($"Machine '{hostname}' is already registered with ID {existing.Id}.");
                return;
            }

            // Добавление
            try
            {
                int newId = await repo.AddAsync(hostname);
                Console.WriteLine($"Machine '{hostname}' registered with ID {newId}.");
            }
            catch (PostgresException ex) when (ex.SqlState == "23505")
            {
                // На случай race condition — кто-то успел добавить между нашими проверками
                Console.WriteLine($"Machine '{hostname}' already exists.");
            }
            catch (PostgresException ex)
            {
                Console.WriteLine($"Failed to register machine: {ex.Message}");
            }
        }
        private Task ShowLogsAsync(string[] args)
        {
            // Парсим параметры
            int limit = 20;
            string? levelFilter = null;
            string? dateFilter = null;

            string? lastStr = ExtractOption(args, "--last");
            if (lastStr != null)
            {
                if (!int.TryParse(lastStr, out limit) || limit < 1 || limit > 1000)
                {
                    Console.WriteLine("--last must be a number between 1 and 1000");
                    return Task.CompletedTask;
                }
            }

            levelFilter = ExtractOption(args, "--level");
            if (levelFilter != null)
            {
                levelFilter = levelFilter.ToUpper();
                string[] validLevels = { "INFO", "WARN", "ERROR", "DEBUG", "FATAL" };
                if (!validLevels.Contains(levelFilter))
                {
                    Console.WriteLine($"Invalid level. Allowed: {string.Join(", ", validLevels)}");
                    return Task.CompletedTask;
                }
            }

            dateFilter = ExtractOption(args, "--date");

            // Определяем какой файл читать
            string logsDir = Path.Combine(AppContext.BaseDirectory, "logs");

            if (!Directory.Exists(logsDir))
            {
                Console.WriteLine("Logs directory not found.");
                Console.WriteLine($"Expected location: {logsDir}");
                return Task.CompletedTask;
            }

            string logFileName;
            if (dateFilter != null)
            {
                // Пользователь указал конкретную дату
                if (!DateTime.TryParse(dateFilter, out DateTime parsedDate))
                {
                    Console.WriteLine("Invalid date format. Use YYYY-MM-DD (example: 2026-05-23)");
                    return Task.CompletedTask;
                }
                logFileName = $"watcher-{parsedDate:yyyyMMdd}.log";
            }
            else
            {
                // По умолчанию — сегодняшний файл
                logFileName = $"watcher-{DateTime.Now:yyyyMMdd}.log";
            }

            string logFilePath = Path.Combine(logsDir, logFileName);

            if (!File.Exists(logFilePath))
            {
                Console.WriteLine($"Log file not found: {logFileName}");

                // Покажем какие файлы есть в наличии
                string[] availableFiles = Directory.GetFiles(logsDir, "watcher-*.log")
                    .Select(Path.GetFileName)
                    .OrderByDescending(f => f)
                    .Take(5)
                    .ToArray()!;

                if (availableFiles.Length > 0)
                {
                    Console.WriteLine("Available log files (latest 5):");
                    foreach (string file in availableFiles)
                    {
                        Console.WriteLine($"  {file}");
                    }
                }
                return Task.CompletedTask;
            }

            // Читаем файл
            string[] allLines;
            try
            {
                // FileShare.ReadWrite — чтобы можно было читать файл
                // даже когда Serilog в него пишет
                using FileStream fs = new FileStream(
                    logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using StreamReader reader = new StreamReader(fs);
                string content = reader.ReadToEnd();
                allLines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            }
            catch (IOException ex)
            {
                Console.WriteLine($"Failed to read log file: {ex.Message}");
                return Task.CompletedTask;
            }

            // Фильтруем по уровню если указан
            IEnumerable<string> filteredLines = allLines;
            if (levelFilter != null)
            {
                filteredLines = allLines.Where(line => LineMatchesLevel(line, levelFilter));
            }

            // Берём последние N строк
            string[] resultLines = filteredLines
                .Reverse()        // чтобы свежие шли первыми
                .Take(limit)
                .Reverse()        // возвращаем хронологический порядок
                .ToArray();

            if (resultLines.Length == 0)
            {
                Console.WriteLine("No matching log entries found.");
                return Task.CompletedTask;
            }

            // Выводим результат
            Console.WriteLine();
            Console.WriteLine($"=== {logFileName} ===");
            if (levelFilter != null)
            {
                Console.WriteLine($"Filter: level >= {levelFilter}");
            }
            Console.WriteLine($"Showing last {resultLines.Length} entries:");
            Console.WriteLine(new string('-', 80));

            foreach (string line in resultLines)
            {
                // Подсветка уровня цветом
                PrintLogLine(line);
            }

            Console.WriteLine();
            return Task.CompletedTask;
        }

        /// <summary>
        /// Проверяет, соответствует ли строка лога заданному уровню.
        /// Уровни иерархичны: FATAL > ERROR > WARN > INFO > DEBUG.
        /// Если filter = ERROR, показываем ERROR и FATAL.
        /// </summary>
        private static bool LineMatchesLevel(string line, string filterLevel)
        {
            // Serilog формат: 2026-05-23 10:45:22.123 +03:00 [INF] Message
            // Нас интересует [INF], [WRN], [ERR], [FTL], [DBG]

            if (!line.Contains('[') || !line.Contains(']'))
                return false;

            int start = line.IndexOf('[');
            int end = line.IndexOf(']', start);
            if (end <= start) return false;

            string levelTag = line.Substring(start + 1, end - start - 1).Trim();

            // Приоритеты уровней (выше = критичнее)
            int linePriority = GetLevelPriority(levelTag);
            int filterPriority = GetLevelPriority(filterLevel);

            return linePriority >= filterPriority;
        }

        private static int GetLevelPriority(string level)
        {
            return level.ToUpper() switch
            {
                "DBG" or "DEBUG" => 1,
                "INF" or "INFO" => 2,
                "WRN" or "WARN" or "WARNING" => 3,
                "ERR" or "ERROR" => 4,
                "FTL" or "FATAL" => 5,
                _ => 0  // неизвестный уровень — самый низкий приоритет
            };
        }

        /// <summary>
        /// Выводит строку лога с цветовой подсветкой уровня.
        /// </summary>
        private static void PrintLogLine(string line)
        {
            ConsoleColor originalColor = Console.ForegroundColor;

            if (line.Contains("[ERR]") || line.Contains("[FTL]"))
                Console.ForegroundColor = ConsoleColor.Red;
            else if (line.Contains("[WRN]"))
                Console.ForegroundColor = ConsoleColor.Yellow;
            else if (line.Contains("[INF]"))
                Console.ForegroundColor = ConsoleColor.Gray;
            else if (line.Contains("[DBG]"))
                Console.ForegroundColor = ConsoleColor.DarkGray;

            Console.WriteLine(line);
            Console.ForegroundColor = originalColor;
        }
    }
}
