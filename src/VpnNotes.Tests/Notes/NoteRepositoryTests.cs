using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using System.Reflection;
using System.Xml.Linq;
using VpnNotes.Core.Models;
using VpnNotes.Core.Repositories;

namespace VpnNotes.Tests.Notes
{
    [TestClass]
    public class NoteRepositoryTests
    {
        /// <summary>
        /// Источник тестовых данных — читает XML и возвращает массивы параметров.
        /// </summary>
        public static IEnumerable<object[]> NotesTestData
        {
            get
            {
                string xmlPath = Path.Combine(
                    AppContext.BaseDirectory,
                    "TestData",
                    "notes-test-data.xml");

                if (!File.Exists(xmlPath))
                {
                    throw new FileNotFoundException(
                        $"Test data file not found: {xmlPath}");
                }

                XDocument doc = XDocument.Load(xmlPath);
                XElement? root = doc.Root;
                if (root == null)
                {
                    throw new InvalidOperationException("XML root not found");
                }

                foreach (XElement row in root.Elements("Row"))
                {
                    string operation = row.Element("Operation")?.Value ?? string.Empty;
                    string userName = row.Element("UserName")?.Value ?? string.Empty;
                    string text = row.Element("Text")?.Value ?? string.Empty;
                    string tagsString = row.Element("Tags")?.Value ?? string.Empty;
                    string searchQuery = row.Element("SearchQuery")?.Value ?? string.Empty;
                    string expectedResult = row.Element("ExpectedResult")?.Value ?? string.Empty;
                    int expectedCount = int.Parse(row.Element("ExpectedCount")?.Value ?? "0");
                    string description = row.Element("Description")?.Value ?? string.Empty;

                    yield return new object[]
                    {
                        operation, userName, text, tagsString,
                        searchQuery, expectedResult, expectedCount, description
                    };
                }
            }
        }

        public static string GetTestDisplayName(MethodInfo methodInfo, object[] data)
        {
            string description = data[7].ToString() ?? "unnamed";
            return $"{methodInfo.Name} ({description})";
        }

        [TestMethod]
        [DynamicData(nameof(NotesTestData),
                     DynamicDataDisplayName = nameof(GetTestDisplayName))]
        public async Task Notes_DataDriven_OperationProducesExpectedResult(
            string operation,
            string userName,
            string text,
            string tagsString,
            string searchQuery,
            string expectedResult,
            int expectedCount,
            string description)
        {
            // Arrange
            Console.WriteLine($"Testing: {description}");
            Console.WriteLine($"Operation: {operation}, User: {userName}");

            await TestDatabase.ResetAsync();
            await using NpgsqlConnection connection = await TestDatabase.OpenAdminAsync();
            NoteRepository repository = new NoteRepository(connection);

            // Парсим теги (если есть)
            string[]? tags = null;
            if (!string.IsNullOrWhiteSpace(tagsString))
            {
                tags = tagsString.Split(',');
            }

            // Act & Assert: ветвление по операции
            switch (operation)
            {
                case "add":
                    await TestAddOperation(repository, text, tags,
                        expectedResult, expectedCount, description);
                    break;

                case "search":
                    await TestSearchOperation(repository, text,
                        searchQuery, expectedCount, description);
                    break;

                case "delete":
                    await TestDeleteOperation(repository, text,
                        expectedCount, description);
                    break;

                case "delete_nonexistent":
                    await TestDeleteNonExistent(repository, expectedResult, description);
                    break;

                default:
                    Assert.Fail($"Unknown operation: '{operation}'");
                    break;
            }
        }

        // ===========================================
        // Реализация операций
        // ===========================================

        private async Task TestAddOperation(
            NoteRepository repository,
            string text,
            string[]? tags,
            string expectedResult,
            int expectedCount,
            string description)
        {
            // Act: БД сама подставит created_by = current_user (postgres под admin-коннектом)
            int newId = await repository.AddAsync(null, text, tags);

            // Assert
            Assert.IsTrue(newId > 0, $"AddAsync should return positive ID for '{description}'");

            // Проверяем что заметка действительно создана
            Note? loaded = await repository.GetByIdAsync(newId);
            Assert.IsNotNull(loaded, $"Note should be retrievable for '{description}'");
            Assert.AreEqual(text, loaded.Text);

            // Проверяем теги если были
            if (tags != null)
            {
                Assert.IsNotNull(loaded.Tags);
                Assert.AreEqual(tags.Length, loaded.Tags.Length);
                foreach (string tag in tags)
                {
                    CollectionAssert.Contains(loaded.Tags, tag);
                }
            }

            // Проверяем количество в БД
            List<Note> allNotes = await repository.ListAsync(limit: 100);
            Assert.AreEqual(expectedCount, allNotes.Count,
                $"Expected {expectedCount} notes in DB after add for '{description}'");
        }

        private async Task TestSearchOperation(
            NoteRepository repository,
            string textToSeed,
            string searchQuery,
            int expectedCount,
            string description)
        {
            // Arrange: засеваем тестовую заметку
            await repository.AddAsync(null, textToSeed, null);

            // Act
            List<Note> results = await repository.SearchAsync(searchQuery, limit: 50);

            // Assert
            Assert.AreEqual(expectedCount, results.Count,
                $"Expected {expectedCount} results for query '{searchQuery}' in '{description}'");
        }

        private async Task TestDeleteOperation(
            NoteRepository repository,
            string text,
            int expectedCount,
            string description)
        {
            // Arrange: создаём заметку для удаления
            int noteId = await repository.AddAsync(null, text, null);

            // Act
            bool deleted = await repository.DeleteAsync(noteId);

            // Assert
            Assert.IsTrue(deleted, $"Delete should return true for '{description}'");

            Note? loaded = await repository.GetByIdAsync(noteId);
            Assert.IsNull(loaded, $"Note should not exist after delete for '{description}'");

            List<Note> remaining = await repository.ListAsync(limit: 100);
            Assert.AreEqual(expectedCount, remaining.Count,
                $"Expected {expectedCount} notes after delete for '{description}'");
        }

        private async Task TestDeleteNonExistent(
            NoteRepository repository,
            string expectedResult,
            string description)
        {
            // Act
            bool deleted = await repository.DeleteAsync(99999);

            // Assert
            if (expectedResult == "not_found")
            {
                Assert.IsFalse(deleted,
                    $"Delete of non-existent note should return false for '{description}'");
            }
            else
            {
                Assert.Fail($"Unexpected expected result for delete_nonexistent: '{expectedResult}'");
            }
        }
    }
}