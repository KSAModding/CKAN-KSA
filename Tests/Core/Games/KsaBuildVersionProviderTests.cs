using System;
using System.IO;

using NUnit.Framework;

using CKAN.Versioning;
using CKAN.Games.KittenSpaceAgency;

namespace Tests.Core.Games
{
    [TestFixture]
    public class KsaBuildVersionProviderTests
    {
        private string tempDir = "";

        [SetUp]
        public void SetUp()
        {
            tempDir = Path.Combine(Path.GetTempPath(),
                                   "KsaBuildVersionProviderTests-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }

        [Test]
        public void TryGetVersion_NoContentVersionsDir_ReturnsFalse()
        {
            // Act
            var found = new KsaBuildVersionProvider().TryGetVersion(tempDir, out var version);

            // Assert
            Assert.IsFalse(found);
            Assert.IsNull(version);
        }

        [Test]
        public void TryGetVersion_ReadsBuildFromNewestVersionsFile()
        {
            // Arrange
            var versionsDir = Path.Combine(tempDir, "Content", "Versions");
            Directory.CreateDirectory(versionsDir);
            WriteVersionFile(versionsDir, "v2026.6.X.4680.json", "2026.6.8.4680",
                             new DateTime(2026, 6, 20, 0, 0, 0, DateTimeKind.Utc));
            WriteVersionFile(versionsDir, "v2026.6.X.4750.json", "2026.6.9.4750",
                             new DateTime(2026, 6, 27, 0, 0, 0, DateTimeKind.Utc));

            // Act
            var found = new KsaBuildVersionProvider().TryGetVersion(tempDir, out var version);

            // Assert
            Assert.IsTrue(found);
            Assert.AreEqual(GameVersion.Parse("2026.6.9.4750"), version);
        }

        private static void WriteVersionFile(string dir, string name, string build,
                                             DateTime writeTimeUtc)
        {
            var path = Path.Combine(dir, name);
            File.WriteAllText(path, "{ \"build\": \"" + build + "\" }");
            File.SetLastWriteTimeUtc(path, writeTimeUtc);
        }
    }
}
