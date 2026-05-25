using Npgsql;
using VpnNotes.Core.Models;

namespace VpnNotes.Core.Repositories
{
    public class MetricsRepository
    {
        private readonly NpgsqlConnection _connection;

        public MetricsRepository(NpgsqlConnection connection)
        {
            _connection = connection;
        }

        public async Task SaveSnapshotAsync(MetricsSnapshot snapshot, NpgsqlTransaction? transaction = null)
        {
            string sql = @"
    INSERT INTO metrics 
        (machine_id, timestamp, cpu_pct, ram_used_mb, ram_total_mb, disk_used_gb, disk_total_gb)
    VALUES 
        (@machine_id, (NOW() AT TIME ZONE 'UTC'), @cpu_pct, @ram_used_mb, @ram_total_mb, @disk_used_gb, @disk_total_gb)";

            await using NpgsqlCommand command = new NpgsqlCommand(sql, _connection, transaction);
            command.Parameters.AddWithValue("machine_id", snapshot.MachineId);
            command.Parameters.AddWithValue("cpu_pct", snapshot.CpuPercent);
            command.Parameters.AddWithValue("ram_used_mb", snapshot.RamUsedMb);
            command.Parameters.AddWithValue("ram_total_mb", snapshot.RamTotalMb);
            command.Parameters.AddWithValue("disk_used_gb", snapshot.DiskUsedGb);
            command.Parameters.AddWithValue("disk_total_gb", snapshot.DiskTotalGb);

            await command.ExecuteNonQueryAsync();
        }
        public async Task<MetricsSnapshot?> GetLatestAsync(int machineId)
        {
            string sql = @"
        SELECT machine_id, timestamp, cpu_pct, ram_used_mb, ram_total_mb, disk_used_gb, disk_total_gb
        FROM metrics
        WHERE machine_id = @machine_id
        ORDER BY timestamp DESC
        LIMIT 1";

            await using NpgsqlCommand command = new NpgsqlCommand(sql, _connection);
            command.Parameters.AddWithValue("machine_id", machineId);

            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                return null;
            }

            MetricsSnapshot snapshot = new MetricsSnapshot
            {
                MachineId = reader.GetInt32(0),
                Timestamp = reader.GetDateTime(1),
                CpuPercent = reader.IsDBNull(2) ? 0f : reader.GetFloat(2),
                RamUsedMb = reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                RamTotalMb = reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
                DiskUsedGb = reader.IsDBNull(5) ? 0f : reader.GetFloat(5),
                DiskTotalGb = reader.IsDBNull(6) ? 0f : reader.GetFloat(6)
            };

            return snapshot;
        }
    }
}