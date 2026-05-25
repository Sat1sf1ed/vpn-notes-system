using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using System.Reflection;
using System.Xml.Linq;

namespace VpnNotes.Tests.Auth
{
    [TestClass]
    public class AuthTests
    {
        /// <summary>
        /// Источник данных для теста — читает XML и возвращает массивы параметров.
        /// Каждый массив — это один запуск теста.
        /// </summary>
        public static IEnumerable<object[]> AuthTestData
        {
            get
            {
                string xmlPath = Path.Combine(
                    AppContext.BaseDirectory,
                    "TestData",
                    "auth-test-data.xml");

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
                    string username = row.Element("Username")?.Value ?? string.Empty;
                    string password = row.Element("Password")?.Value ?? string.Empty;
                    string expectedResult = row.Element("ExpectedResult")?.Value ?? string.Empty;
                    string description = row.Element("Description")?.Value ?? string.Empty;

                    yield return new object[] { username, password, expectedResult, description };
                }
            }
        }

        /// <summary>
        /// Метод для генерации читаемого имени каждого запуска теста.
        /// </summary>
        public static string GetTestDisplayName(MethodInfo methodInfo, object[] data)
        {
            string description = data[3].ToString() ?? "unnamed";
            return $"{methodInfo.Name} ({description})";
        }

        [TestMethod]
        [DynamicData(nameof(AuthTestData),
                     DynamicDataDisplayName = nameof(GetTestDisplayName))]
        public async Task Login_DataDriven_BehavesAsExpected(
            string username,
            string password,
            string expectedResult,
            string description)
        {
            // Arrange
            Console.WriteLine($"Testing: {description}");
            Console.WriteLine($"Username: {username}, Expected: {expectedResult}");

            string connectionString =
                $"Host={TestDatabase.Host};Port={TestDatabase.Port};" +
                $"Database={TestDatabase.DatabaseName};" +
                $"Username={username};Password={password}";

            // Act & Assert — поведение зависит от ожидаемого результата
            if (expectedResult == "success")
            {
                await using NpgsqlConnection connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync();

                Assert.AreEqual(
                    System.Data.ConnectionState.Open,
                    connection.State,
                    $"Connection should be open for '{description}'");
            }
            else if (expectedResult == "fail_invalid_password")
            {
                bool exceptionThrown = false;
                string? actualSqlState = null;

                try
                {
                    await using NpgsqlConnection connection = new NpgsqlConnection(connectionString);
                    await connection.OpenAsync();
                }
                catch (PostgresException ex)
                {
                    exceptionThrown = true;
                    actualSqlState = ex.SqlState;
                }
                catch (NpgsqlException)
                {
                    exceptionThrown = true;
                }

                Assert.IsTrue(exceptionThrown,
                    $"Expected exception for '{description}', but none was thrown");

                if (actualSqlState != null)
                {
                    Assert.AreEqual("28P01", actualSqlState,
                        $"Expected SQL state 28P01 for '{description}'");
                }
            }
            else
            {
                Assert.Fail($"Unknown expected result: '{expectedResult}' in test data");
            }
        }
    }
}