using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using System.Reflection;
using System.Xml.Linq;

namespace VpnNotes.Tests.Database
{
    [TestClass]
    public class DatabaseConnectionTests
    {
        /// <summary>
        /// Источник тестовых данных — читает XML и возвращает массивы параметров.
        /// </summary>
        public static IEnumerable<object[]> ConnectionTestData
        {
            get
            {
                string xmlPath = Path.Combine(
                    AppContext.BaseDirectory,
                    "TestData",
                    "db-connection-test-data.xml");

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
                    string host = row.Element("Host")?.Value ?? string.Empty;
                    int port = int.Parse(row.Element("Port")?.Value ?? "5432");
                    string database = row.Element("Database")?.Value ?? string.Empty;
                    string username = row.Element("Username")?.Value ?? string.Empty;
                    string password = row.Element("Password")?.Value ?? string.Empty;
                    int timeout = int.Parse(row.Element("Timeout")?.Value ?? "5");
                    string expectedResult = row.Element("ExpectedResult")?.Value ?? string.Empty;
                    string description = row.Element("Description")?.Value ?? string.Empty;

                    yield return new object[]
                    {
                        host, port, database, username, password,
                        timeout, expectedResult, description
                    };
                }
            }
        }

        /// <summary>
        /// Генерирует читаемое имя теста на основе описания.
        /// </summary>
        public static string GetTestDisplayName(MethodInfo methodInfo, object[] data)
        {
            string description = data[7].ToString() ?? "unnamed";
            return $"{methodInfo.Name} ({description})";
        }

        [TestMethod]
        [DynamicData(nameof(ConnectionTestData),
                     DynamicDataDisplayName = nameof(GetTestDisplayName))]
        public async Task Connection_DataDriven_BehavesAsExpected(
            string host,
            int port,
            string database,
            string username,
            string password,
            int timeout,
            string expectedResult,
            string description)
        {
            // Arrange
            Console.WriteLine($"Testing: {description}");
            Console.WriteLine($"Target: {username}@{host}:{port}/{database}");
            Console.WriteLine($"Expected: {expectedResult}");

            string connectionString =
                $"Host={host};Port={port};Database={database};" +
                $"Username={username};Password={password};" +
                $"Timeout={timeout}";

            // Act & Assert
            if (expectedResult == "success")
            {
                await using NpgsqlConnection connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync();

                Assert.AreEqual(
                    System.Data.ConnectionState.Open,
                    connection.State,
                    $"Connection should be open for '{description}'");
            }
            else
            {
                bool exceptionThrown = false;
                string? actualSqlState = null;
                Type? actualExceptionType = null;

                try
                {
                    await using NpgsqlConnection connection = new NpgsqlConnection(connectionString);
                    await connection.OpenAsync();
                }
                catch (PostgresException ex)
                {
                    exceptionThrown = true;
                    actualSqlState = ex.SqlState;
                    actualExceptionType = ex.GetType();
                }
                catch (NpgsqlException ex)
                {
                    exceptionThrown = true;
                    actualExceptionType = ex.GetType();
                }
                catch (TimeoutException ex)
                {
                    exceptionThrown = true;
                    actualExceptionType = ex.GetType();
                }

                Assert.IsTrue(exceptionThrown,
                    $"Expected exception for '{description}', but none was thrown");

                // Проверяем тип ошибки в зависимости от ожидаемого результата
                switch (expectedResult)
                {
                    case "fail_auth":
                        // 28P01 = invalid_password
                        Assert.AreEqual("28P01", actualSqlState,
                            $"Expected SQL state 28P01 (invalid_password) for '{description}'");
                        break;

                    case "fail_database":
                        // 3D000 = invalid_catalog_name (несуществующая БД)
                        Assert.AreEqual("3D000", actualSqlState,
                            $"Expected SQL state 3D000 (invalid_catalog_name) for '{description}'");
                        break;

                    case "fail_network":
                        // Сетевые ошибки не имеют SqlState — проверяем что это сетевое исключение
                        Assert.IsNull(actualSqlState,
                            $"Network error should not have SqlState for '{description}'");
                        Assert.IsNotNull(actualExceptionType,
                            $"Should have caught some exception for '{description}'");
                        break;

                    default:
                        Assert.Fail($"Unknown expected result: '{expectedResult}'");
                        break;
                }
            }
        }
    }
}