using Npgsql;
using VpnNotes.Core.Models;

namespace VpnNotes.Core.Repositories
{
    public class NoteRepository
    {
        private readonly NpgsqlConnection _connection;

        public NoteRepository(NpgsqlConnection connection)
        {
            _connection = connection;
        }

        public async Task<int> AddAsync(int? machineId, string text, string[]? tags)
        {
            string sql = @"
        INSERT INTO notes (machine_id, text, tags) 
        VALUES (@machine_id, @text, @tags)
        RETURNING id";

            await using NpgsqlCommand command = new NpgsqlCommand(sql, _connection);
            command.Parameters.AddWithValue("machine_id", (object?)machineId ?? DBNull.Value);
            command.Parameters.AddWithValue("text", text);
            command.Parameters.AddWithValue("tags", (object?)tags ?? DBNull.Value);

            object? result = await command.ExecuteScalarAsync();
            if (result == null)
            {
                throw new InvalidOperationException("INSERT did not return an ID");
            }

            return (int)result;
        }

        public async Task<List<Note>> ListAsync(int limit)
        {
            List<Note> notes = new List<Note>();

            string sql = @"
        SELECT id, machine_id, text, tags, created_at, created_by
        FROM notes
        ORDER BY created_at DESC 
        LIMIT @limit";

            await using NpgsqlCommand command = new NpgsqlCommand(sql, _connection);
            command.Parameters.AddWithValue("limit", limit);

            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                notes.Add(ReadNote(reader));
            }

            return notes;
        }

        public async Task<Note?> GetByIdAsync(int id)
        {
            string sql = @"
        SELECT id, machine_id, text, tags, created_at, created_by
        FROM notes
        WHERE id = @id";

            await using NpgsqlCommand command = new NpgsqlCommand(sql, _connection);
            command.Parameters.AddWithValue("id", id);

            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                return null;
            }

            return ReadNote(reader);
        }
        public async Task<List<Note>> SearchAsync(string query, int limit)
        {
            List<Note> notes = new List<Note>();

            string sql = @"
        SELECT id, machine_id, text, tags, created_at, created_by
        FROM notes
        WHERE text ILIKE '%' || @query || '%'
        ORDER BY created_at DESC
        LIMIT @limit";

            await using NpgsqlCommand command = new NpgsqlCommand(sql, _connection);
            command.Parameters.AddWithValue("query", query);
            command.Parameters.AddWithValue("limit", limit);

            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                notes.Add(ReadNote(reader));
            }

            return notes;
        }
        public async Task<bool> DeleteAsync(int id)
        {
            string sql = "DELETE FROM notes WHERE id = @id";

            await using NpgsqlCommand command = new NpgsqlCommand(sql, _connection);
            command.Parameters.AddWithValue("id", id);

            int affected = await command.ExecuteNonQueryAsync();
            return affected > 0;
        }

        private static Note ReadNote(NpgsqlDataReader reader)
        {
            return new Note
            {
                Id = reader.GetInt32(0),
                MachineId = reader.IsDBNull(1) ? null : reader.GetInt32(1),
                Text = reader.GetString(2),
                Tags = reader.IsDBNull(3) ? null : (string[])reader.GetValue(3),
                CreatedAt = reader.GetDateTime(4),
                CreatedBy = reader.GetString(5)
            };
        }
    }
}