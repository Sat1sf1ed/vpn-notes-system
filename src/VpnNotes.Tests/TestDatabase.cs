using Npgsql;

namespace VpnNotes.Tests
{
    /// <summary>
    /// Утилиты для работы с тестовой базой данных.
    /// Все интеграционные тесты используют эти методы.
    /// </summary>
    public static class TestDatabase
    {
        public const string Host = "localhost";
        public const int Port = 5432;
        public const string DatabaseName = "vpnnotes_test";

        public const string AdminUsername = "postgres";
        public const string AdminPassword = "1234";

        public const string TestUsername = "test";
        public const string TestPassword = "test123";

        public static string AdminConnectionString =>
            $"Host={Host};Port={Port};Database={DatabaseName};" +
            $"Username={AdminUsername};Password={AdminPassword}";

        public static string UserConnectionString =>
            $"Host={Host};Port={Port};Database={DatabaseName};" +
            $"Username={TestUsername};Password={TestPassword}";

        public static async Task<NpgsqlConnection> OpenAdminAsync()
        {
            NpgsqlConnection conn = new NpgsqlConnection(AdminConnectionString);
            await conn.OpenAsync();
            return conn;
        }

        public static async Task<NpgsqlConnection> OpenUserAsync()
        {
            NpgsqlConnection conn = new NpgsqlConnection(UserConnectionString);
            await conn.OpenAsync();
            return conn;
        }

        /// <summary>
        /// Очищает все таблицы и оставляет одну тестовую машину.
        /// Вызывается перед каждым тестом, чтобы тесты не влияли друг на друга.
        /// </summary>
        public static async Task ResetAsync()
        {
            await using NpgsqlConnection conn = await OpenAdminAsync();

            // TRUNCATE с RESTART IDENTITY сбрасывает SERIAL счётчики
            // CASCADE автоматически очищает связанные таблицы
            await new NpgsqlCommand(
                "TRUNCATE TABLE notes, metrics, machines RESTART IDENTITY CASCADE",
                conn).ExecuteNonQueryAsync();

            await new NpgsqlCommand(
                "INSERT INTO machines (hostname) VALUES ('test-machine')",
                conn).ExecuteNonQueryAsync();
        }
    }
}
