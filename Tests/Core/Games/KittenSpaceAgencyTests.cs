using System;
using System.IO;
using System.Linq;

using NUnit.Framework;

using CKAN.Versioning;
using CKAN.Games.KittenSpaceAgency;

namespace Tests.Core.Games
{
    [TestFixture]
    public class KittenSpaceAgencyTests
    {
        private string tempDir = "";

        [SetUp]
        public void SetUp()
        {
            tempDir = Path.Combine(Path.GetTempPath(),
                                   "KittenSpaceAgencyTests-" + Guid.NewGuid());
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
        public void EmbeddedGameVersions_Called_ContainsKnownBuild()
        {
            // Arrange
            var game = new KittenSpaceAgency();

            // Act
            var versions = game.EmbeddedGameVersions;

            // Assert: the build counter (3rd component) is normalized to 0, so the
            // known build 2025.11.6.2829 is stored as 2025.11.0.2829.
            CollectionAssert.IsNotEmpty(versions);
            CollectionAssert.Contains(versions, GameVersion.Parse("2025.11.0.2829"));
        }

        [Test]
        public void EmbeddedGameVersions_BuildCounterNormalizedToZero()
        {
            // Arrange
            var game = new KittenSpaceAgency();

            // Act
            var versions = game.EmbeddedGameVersions;

            // Assert: KSA's non-monotonic build counter must never survive into a
            // version CKAN sorts or compares, so every 4-part build has patch == 0.
            Assert.That(versions.Where(v => v.IsBuildDefined),
                        Has.All.Matches<GameVersion>(v => v.Patch == 0));
        }

        [Test]
        public void EmbeddedGameVersions_OrderByRevisionNotBuildCounter()
        {
            // 2026.2.10.3538 was released before 2026.2.3.3549: the newer build has the
            // higher revision (3549) but the LOWER build counter (3), the exact case
            // that breaks positional sorting. After normalization the build counter no
            // longer flips the order.
            var game = new KittenSpaceAgency();
            var versions = game.EmbeddedGameVersions;

            var older = versions.First(v => v.Build == 3538);
            var newer = versions.First(v => v.Build == 3549);

            Assert.That(older.Patch, Is.EqualTo(0));
            Assert.That(newer.Patch, Is.EqualTo(0));
            Assert.That(newer, Is.GreaterThan(older));
        }

        [Test]
        public void DetectVersion_NormalizesBuildCounterToZero()
        {
            // Arrange: a Content/Versions file whose build counter (3rd component) is 9.
            var versionsDir = Path.Combine(tempDir, "Content", "Versions");
            Directory.CreateDirectory(versionsDir);
            File.WriteAllText(Path.Combine(versionsDir, "v2026.6.X.4750.json"),
                              "{ \"build\": \"2026.6.9.4750\" }");

            // Act
            var version = new KittenSpaceAgency().DetectVersion(new DirectoryInfo(tempDir));

            // Assert: the build counter is pinned to 0, the revision is preserved.
            Assert.AreEqual(GameVersion.Parse("2026.6.0.4750"), version);
        }
    }
}
