using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Reflection;
using System.Xml.Linq;

namespace VpnNotes.Tests.Updates
{
    [TestClass]
    public class UpdateTests
    {
        /// <summary>
        /// Источник тестовых данных — читает XML и возвращает массивы параметров.
        /// </summary>
        public static IEnumerable<object[]> UpdateTestData
        {
            get
            {
                string xmlPath = Path.Combine(
                    AppContext.BaseDirectory,
                    "TestData",
                    "update-test-data.xml");

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
                    string currentVersion = row.Element("CurrentVersion")?.Value ?? string.Empty;
                    string remoteTag = row.Element("RemoteTag")?.Value ?? string.Empty;
                    string expectedDecision = row.Element("ExpectedDecision")?.Value ?? string.Empty;
                    string expectedParsedVersion = row.Element("ExpectedParsedVersion")?.Value ?? string.Empty;
                    string description = row.Element("Description")?.Value ?? string.Empty;

                    yield return new object[]
                    {
                        currentVersion,
                        remoteTag,
                        expectedDecision,
                        expectedParsedVersion,
                        description
                    };
                }
            }
        }

        public static string GetTestDisplayName(MethodInfo methodInfo, object[] data)
        {
            string description = data[4].ToString() ?? "unnamed";
            return $"{methodInfo.Name} ({description})";
        }

        [TestMethod]
        [DynamicData(nameof(UpdateTestData),
                     DynamicDataDisplayName = nameof(GetTestDisplayName))]
        public void Update_DataDriven_DecisionMatchesExpected(
            string currentVersionString,
            string remoteTag,
            string expectedDecision,
            string expectedParsedVersion,
            string description)
        {
            // Arrange
            Console.WriteLine($"Testing: {description}");
            Console.WriteLine($"Current: '{currentVersionString}'");
            Console.WriteLine($"Remote tag: '{remoteTag}'");
            Console.WriteLine($"Expected: {expectedDecision}");

            // Act: парсим текущую версию
            bool currentParsed = Version.TryParse(currentVersionString, out Version? currentVersion);
            Assert.IsTrue(currentParsed,
                $"Current version should always be parseable. Got: '{currentVersionString}'");

            // Act: парсим remote-тег (имитируем то, что делает GitHubUpdater)
            Version? remoteVersion = ParseRemoteTag(remoteTag);

            // Act: принимаем решение об обновлении
            string actualDecision = MakeUpdateDecision(currentVersion!, remoteVersion);

            // Assert
            Assert.AreEqual(expectedDecision, actualDecision,
                $"Decision mismatch for '{description}'");

            // Дополнительно: если ожидался валидный парсинг, проверяем версию
            if (!string.IsNullOrEmpty(expectedParsedVersion) && remoteVersion != null)
            {
                Version expected = new Version(expectedParsedVersion);
                Assert.AreEqual(expected, remoteVersion,
                    $"Parsed version mismatch for '{description}'");
            }
        }

        // =================================================
        // Хелперы, которые имитируют логику GitHubUpdater
        // =================================================

        /// <summary>
        /// Парсит remote-тег. Возвращает null если тег невалидный.
        /// Логика та же, что в GitHubUpdater.CheckAsync.
        /// </summary>
        private static Version? ParseRemoteTag(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag))
            {
                return null;
            }

            string tagWithoutV = tag.TrimStart('v', 'V');
            if (string.IsNullOrWhiteSpace(tagWithoutV))
            {
                return null;
            }

            if (!Version.TryParse(tagWithoutV, out Version? version))
            {
                return null;
            }

            return version;
        }

        /// <summary>
        /// Принимает решение об обновлении на основе текущей и удалённой версий.
        /// Возвращает: "update", "skip" или "invalid_tag".
        /// </summary>
        private static string MakeUpdateDecision(Version current, Version? remote)
        {
            if (remote == null)
            {
                return "invalid_tag";
            }

            if (remote > current)
            {
                return "update";
            }

            return "skip";
        }
    }
}