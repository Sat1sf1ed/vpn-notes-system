using Npgsql;
using VpnNotes.Core.Models;

namespace VpnNotes.Core.Repositories
{
    public class UserRepository
    {
        private readonly NpgsqlConnection _connection;

        public UserRepository(NpgsqlConnection connection)
        {
            _connection = connection;
        }

        public async Task<User?> GetByUsernameAsync(string username)
        {
            string sql = @"
                SELECT username, role, full_name, created_at, created_by
                FROM users
                WHERE username = @username";

            await using NpgsqlCommand command = new NpgsqlCommand(sql, _connection);
            command.Parameters.AddWithValue("username", username);

            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                return null;
            }

            return ReadUser(reader);
        }

        public async Task<List<User>> GetAllAsync()
        {
            List<User> users = new List<User>();

            string sql = @"
                SELECT username, role, full_name, created_at, created_by
                FROM users
                ORDER BY role DESC, username";

            await using NpgsqlCommand command = new NpgsqlCommand(sql, _connection);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                users.Add(ReadUser(reader));
            }

            return users;
        }

        public async Task CreateAsync(string username, string password, UserRole role,
    string? fullName, string createdBy)
        {
            await using NpgsqlTransaction tx = await _connection.BeginTransactionAsync();
            try
            {
                // 1. Создать PG-пользователя
                string safeUsername = EscapeIdentifier(username);
                string safePassword = EscapeStringLiteral(password);

                string createUserSql = $"CREATE USER {safeUsername} WITH PASSWORD '{safePassword}'";
                await using (NpgsqlCommand cmd = new NpgsqlCommand(createUserSql, _connection, tx))
                {
                    await cmd.ExecuteNonQueryAsync();
                }

                // 2. Выдать соответствующую групповую роль
                string roleName = role.GetPgGroupRole();
                string grantSql = $"GRANT {roleName} TO {safeUsername}";
                await using (NpgsqlCommand cmd = new NpgsqlCommand(grantSql, _connection, tx))
                {
                    await cmd.ExecuteNonQueryAsync();
                }

                // 3. Для админов — дополнительные права на администрирование
                if (role == UserRole.Admin)
                {
                    // 3a. Атрибут CREATEROLE — для CREATE USER в будущем
                    string createRoleSql = $"ALTER ROLE {safeUsername} CREATEROLE";
                    await using (NpgsqlCommand cmd = new NpgsqlCommand(createRoleSql, _connection, tx))
                    {
                        await cmd.ExecuteNonQueryAsync();
                    }

                    // 3b. WITH ADMIN OPTION на все три роли — чтобы мог назначать их другим
                    string[] allRoles = new string[]
                    {
                "vpnnotes_user",
                "vpnnotes_stats",
                "vpnnotes_admin"
                    };
                    foreach (string allRole in allRoles)
                    {
                        string adminGrantSql = $"GRANT {allRole} TO {safeUsername} WITH ADMIN OPTION";
                        await using (NpgsqlCommand cmd = new NpgsqlCommand(adminGrantSql, _connection, tx))
                        {
                            await cmd.ExecuteNonQueryAsync();
                        }
                    }
                }

                // 4. Запись в таблицу users
                string insertSql = @"
            INSERT INTO users (username, role, full_name, created_by)
            VALUES (@username, @role, @full_name, @created_by)";
                await using (NpgsqlCommand cmd = new NpgsqlCommand(insertSql, _connection, tx))
                {
                    cmd.Parameters.AddWithValue("username", username);
                    cmd.Parameters.AddWithValue("role", role.ToDbString());
                    cmd.Parameters.AddWithValue("full_name", (object?)fullName ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("created_by", createdBy);
                    await cmd.ExecuteNonQueryAsync();
                }

                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        public async Task DeleteAsync(string username)
        {
            await using NpgsqlTransaction tx = await _connection.BeginTransactionAsync();
            try
            {
                // 1. Удалить из таблицы users
                string deleteSql = "DELETE FROM users WHERE username = @username";
                await using (NpgsqlCommand cmd = new NpgsqlCommand(deleteSql, _connection, tx))
                {
                    cmd.Parameters.AddWithValue("username", username);
                    await cmd.ExecuteNonQueryAsync();
                }

                // 2. Отозвать GRANT'ы (REVOKE ALL FROM)
                // 3. Удалить PG-пользователя
                string safeUsername = EscapeIdentifier(username);
                string dropSql = $"DROP USER IF EXISTS {safeUsername}";
                await using (NpgsqlCommand cmd = new NpgsqlCommand(dropSql, _connection, tx))
                {
                    await cmd.ExecuteNonQueryAsync();
                }

                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        // === Безопасное экранирование для DDL команд ===

        /// <summary>
        /// Экранирует идентификатор (имя пользователя/таблицы/роли).
        /// Только буквы, цифры и подчёркивание разрешены.
        /// </summary>
        private static string EscapeIdentifier(string identifier)
        {
            if (string.IsNullOrEmpty(identifier))
            {
                throw new ArgumentException("Identifier cannot be empty");
            }

            // Строгая проверка: только [a-zA-Z0-9_], длина от 1 до 63 (лимит PG)
            if (identifier.Length > 63)
            {
                throw new ArgumentException("Identifier too long (max 63 characters)");
            }

            foreach (char c in identifier)
            {
                if (!char.IsLetterOrDigit(c) && c != '_')
                {
                    throw new ArgumentException(
                        $"Invalid character in identifier: '{c}'. " +
                        "Only letters, digits and underscores are allowed.");
                }
            }

            // Дополнительно оборачиваем в двойные кавычки для безопасности
            return $"\"{identifier}\"";
        }

        /// <summary>
        /// Экранирует строковый литерал для SQL (для CREATE USER ... PASSWORD '...').
        /// </summary>
        private static string EscapeStringLiteral(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                throw new ArgumentException("Value cannot be empty");
            }

            // Удваиваем одинарные кавычки
            return value.Replace("'", "''");
        }

        private static User ReadUser(NpgsqlDataReader reader)
        {
            return new User
            {
                Username = reader.GetString(0),
                Role = UserRoleExtensions.ParseDbString(reader.GetString(1)),
                FullName = reader.IsDBNull(2) ? null : reader.GetString(2),
                CreatedAt = reader.GetDateTime(3),
                CreatedBy = reader.IsDBNull(4) ? null : reader.GetString(4)
            };
        }
    }
}
