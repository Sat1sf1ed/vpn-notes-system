using Npgsql;
using VpnNotes.Core.Models;

namespace VpnNotes.Core.Repositories
{
    public class MachineRepository
    {
        private readonly NpgsqlConnection _connection;

        public MachineRepository(NpgsqlConnection connection)
        {
            _connection = connection;
        }

        public async Task<List<Machine>> GetAllAsync()
        {
            List<Machine> machines = new List<Machine>();

            string sql = "SELECT id, hostname, last_seen, created_at FROM machines ORDER BY hostname";
            await using NpgsqlCommand command = new NpgsqlCommand(sql, _connection);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                Machine machine = new Machine
                {
                    Id = reader.GetInt32(0),
                    Hostname = reader.GetString(1),
                    LastSeen = reader.IsDBNull(2) ? null : reader.GetDateTime(2),
                    CreatedAt = reader.GetDateTime(3)
                };
                machines.Add(machine);
            }

            return machines;
        }

        public async Task<Machine?> GetByHostnameAsync(string hostname)
        {
            string sql = "SELECT id, hostname, last_seen, created_at FROM machines WHERE hostname = @hostname";
            await using NpgsqlCommand command = new NpgsqlCommand(sql, _connection);
            command.Parameters.AddWithValue("hostname", hostname);

            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                return null;
            }

            return new Machine
            {
                Id = reader.GetInt32(0),
                Hostname = reader.GetString(1),
                LastSeen = reader.IsDBNull(2) ? null : reader.GetDateTime(2),
                CreatedAt = reader.GetDateTime(3)
            };
        }
        public async Task UpdateLastSeenAsync(int machineId, NpgsqlTransaction? transaction = null)
        {
            string sql = "UPDATE machines SET last_seen = (NOW() AT TIME ZONE 'UTC') WHERE id = @id";

            await using NpgsqlCommand command = new NpgsqlCommand(sql, _connection, transaction);
            command.Parameters.AddWithValue("id", machineId);

            await command.ExecuteNonQueryAsync();
        }
        public async Task<int> AddAsync(string hostname)
        {
            string sql = @"
        INSERT INTO machines (hostname) 
        VALUES (@hostname)
        RETURNING id";

            await using NpgsqlCommand command = new NpgsqlCommand(sql, _connection);
            command.Parameters.AddWithValue("hostname", hostname);

            object? result = await command.ExecuteScalarAsync();
            if (result == null)
            {
                throw new InvalidOperationException("INSERT did not return an ID");
            }

            return (int)result;
        }
        public async Task<Machine?> GetByIdAsync(int id)
        {
            string sql = "SELECT id, hostname, last_seen, created_at FROM machines WHERE id = @id";

            await using NpgsqlCommand command = new NpgsqlCommand(sql, _connection);
            command.Parameters.AddWithValue("id", id);

            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                return null;
            }

            return new Machine
            {
                Id = reader.GetInt32(0),
                Hostname = reader.GetString(1),
                LastSeen = reader.IsDBNull(2) ? null : reader.GetDateTime(2),
                CreatedAt = reader.GetDateTime(3)
            };
        }
    }
}
