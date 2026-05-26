using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace VpnNotes.Tests.Auth
{
    [TestClass]
    public class RolePermissionTests
    {
        // Тест: user видит только свои заметки (RLS)
        [TestMethod]
        public async Task UserRole_SeeOnlyOwnNotes_RlsFiltersOthers()
        {
            await TestDatabase.ResetAsync();

            // Под админом создаём 2 заметки от разных авторов
            await using (NpgsqlConnection adminConn = await TestDatabase.OpenAdminAsync())
            {
                await new NpgsqlCommand(
                    "INSERT INTO notes (text, created_by) VALUES ('Note A', 'test_user')",
                    adminConn).ExecuteNonQueryAsync();
                await new NpgsqlCommand(
                    "INSERT INTO notes (text, created_by) VALUES ('Note B', 'test_admin')",
                    adminConn).ExecuteNonQueryAsync();
            }

            // Под user видим только одну
            await using NpgsqlConnection userConn = await TestDatabase.OpenUserAsync();
            NpgsqlCommand selectCmd = new NpgsqlCommand("SELECT COUNT(*) FROM notes", userConn);
            long count = (long)(await selectCmd.ExecuteScalarAsync() ?? 0L);

            Assert.AreEqual(1L, count, "User should see only own notes via RLS");
        }

        // Тест: admin видит все заметки
        [TestMethod]
        public async Task AdminRole_SeeAllNotes_NoRlsFilter()
        {
            await TestDatabase.ResetAsync();

            await using (NpgsqlConnection adminConn = await TestDatabase.OpenAdminAsync())
            {
                await new NpgsqlCommand(
                    "INSERT INTO notes (text, created_by) VALUES ('A', 'test_user')",
                    adminConn).ExecuteNonQueryAsync();
                await new NpgsqlCommand(
                    "INSERT INTO notes (text, created_by) VALUES ('B', 'test_stats')",
                    adminConn).ExecuteNonQueryAsync();
            }

            await using NpgsqlConnection adminConn2 = await TestDatabase.OpenAdminAsync();
            NpgsqlCommand cmd = new NpgsqlCommand("SELECT COUNT(*) FROM notes", adminConn2);
            long count = (long)(await cmd.ExecuteScalarAsync() ?? 0L);

            Assert.AreEqual(2L, count, "Admin should see all notes");
        }
    }
}