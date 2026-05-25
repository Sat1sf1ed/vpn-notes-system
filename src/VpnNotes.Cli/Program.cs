using System.Text;

namespace VpnNotes.Cli
{
    internal class Program
    {
        static async Task<int> Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.InputEncoding = Encoding.UTF8;

            string baseDir = AppContext.BaseDirectory;
            string configPath = Path.Combine(baseDir, "app-config.yml");

            CliApplication app = new CliApplication(configPath);
            int exitCode = await app.RunAsync();
            return exitCode;
        }
    }
}
