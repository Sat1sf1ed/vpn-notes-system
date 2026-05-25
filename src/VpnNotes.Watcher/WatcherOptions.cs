namespace VpnNotes.Watcher
{
    public class WatcherOptions
    {
        public string DbHost { get; set; } = string.Empty;
        public int DbPort { get; set; } = 5432;
        public string DbName { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string MachineName { get; set; } = string.Empty;
        public int ParentPid { get; set; }
        public int IntervalSeconds { get; set; } = 60;

        public static WatcherOptions Parse(string[] args)
        {
            WatcherOptions options = new WatcherOptions();

            for (int i = 0; i < args.Length - 1; i++)
            {
                switch (args[i])
                {
                    case "--db-host":
                        options.DbHost = args[i + 1];
                        break;
                    case "--db-port":
                        options.DbPort = int.Parse(args[i + 1]);
                        break;
                    case "--db-name":
                        options.DbName = args[i + 1];
                        break;
                    case "--username":
                        options.Username = args[i + 1];
                        break;
                    case "--machine-name":
                        options.MachineName = args[i + 1];
                        break;
                    case "--parent-pid":
                        options.ParentPid = int.Parse(args[i + 1]);
                        break;
                    case "--interval":
                        options.IntervalSeconds = int.Parse(args[i + 1]);
                        break;
                }
            }

            Validate(options);
            return options;
        }

        private static void Validate(WatcherOptions options)
        {
            if (string.IsNullOrEmpty(options.DbHost))
            {
                throw new ArgumentException("--db-host is required");
            }
            if (string.IsNullOrEmpty(options.DbName))
            {
                throw new ArgumentException("--db-name is required");
            }
            if (string.IsNullOrEmpty(options.Username))
            {
                throw new ArgumentException("--username is required");
            }
            if (string.IsNullOrEmpty(options.MachineName))
            {
                throw new ArgumentException("--machine-name is required");
            }
            if (options.ParentPid == 0)
            {
                throw new ArgumentException("--parent-pid is required");
            }
            if (options.IntervalSeconds <= 0)
            {
                throw new ArgumentException("--interval must be positive");
            }
        }
    }
}
