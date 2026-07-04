using System;
using System.Diagnostics;
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

        [Test]
        public void TryGetVersion_ReadsFileVersionFromKsaDll()
        {
            // Arrange
            var expected = CopyAssemblyAsKsaDll(fullFourPart: true);

            // Act
            var found = new KsaBuildVersionProvider().TryGetVersion(tempDir, out var version);

            // Assert
            Assert.IsTrue(found);
            Assert.AreEqual(expected, version);
        }

        [Test]
        public void TryGetVersion_DllTakesPrecedenceOverVersionsFiles()
        {
            // Arrange
            var expected = CopyAssemblyAsKsaDll(fullFourPart: true);
            var versionsDir = Path.Combine(tempDir, "Content", "Versions");
            Directory.CreateDirectory(versionsDir);
            WriteVersionFile(versionsDir, "v2026.6.X.4750.json", "2026.6.9.4750",
                             new DateTime(2026, 6, 27, 0, 0, 0, DateTimeKind.Utc));

            // Act
            var found = new KsaBuildVersionProvider().TryGetVersion(tempDir, out var version);

            // Assert: the dll's version wins over the build info file
            Assert.IsTrue(found);
            Assert.AreEqual(expected, version);
        }

        [Test]
        public void TryGetVersion_CoarseDllVersionFallsBackToVersionsFiles()
        {
            // Arrange: a dll whose stamp has fewer than 4 parts is not a full
            // KSA version, so the provider must not trust it
            CopyAssemblyAsKsaDll(fullFourPart: false);
            var versionsDir = Path.Combine(tempDir, "Content", "Versions");
            Directory.CreateDirectory(versionsDir);
            WriteVersionFile(versionsDir, "v2026.6.X.4750.json", "2026.6.9.4750",
                             new DateTime(2026, 6, 27, 0, 0, 0, DateTimeKind.Utc));

            // Act
            var found = new KsaBuildVersionProvider().TryGetVersion(tempDir, out var version);

            // Assert
            Assert.IsTrue(found);
            Assert.AreEqual(GameVersion.Parse("2026.6.9.4750"), version);
        }

        [Test]
        public void TryGetVersion_NonPeDllFallsBackToVersionsFiles()
        {
            // Arrange: a KSA.dll that is not a PE file yields a null FileVersion
            // (FileVersionInfo.GetVersionInfo does not throw on existing files)
            File.WriteAllText(Path.Combine(tempDir, "KSA.dll"), "not a PE file");
            var versionsDir = Path.Combine(tempDir, "Content", "Versions");
            Directory.CreateDirectory(versionsDir);
            WriteVersionFile(versionsDir, "v2026.6.X.4750.json", "2026.6.9.4750",
                             new DateTime(2026, 6, 27, 0, 0, 0, DateTimeKind.Utc));

            // Act
            var found = new KsaBuildVersionProvider().TryGetVersion(tempDir, out var version);

            // Assert
            Assert.IsTrue(found);
            Assert.AreEqual(GameVersion.Parse("2026.6.9.4750"), version);
        }

        [Test]
        public void TryGetVersion_NonPeDllAndNoVersionsDir_ReturnsFalse()
        {
            // Arrange
            File.WriteAllText(Path.Combine(tempDir, "KSA.dll"), "not a PE file");

            // Act
            var found = new KsaBuildVersionProvider().TryGetVersion(tempDir, out var version);

            // Assert
            Assert.IsFalse(found);
            Assert.IsNull(version);
        }

        // Stand in for a real KSA.dll with a managed assembly from the test bin
        // directory, since the provider only reads the PE file version resource.
        // fullFourPart selects an assembly stamped with a full 4-part version
        // (the shape the provider trusts) or a coarser one (which it must not).
        // Returns the version stamped into the chosen assembly.
        private GameVersion CopyAssemblyAsKsaDll(bool fullFourPart)
        {
            var binDir = Path.GetDirectoryName(typeof(KsaBuildVersionProviderTests).Assembly.Location)!;
            foreach (var dll in Directory.EnumerateFiles(binDir, "*.dll"))
            {
                if (FileVersionInfo.GetVersionInfo(dll).FileVersion is string fileVersion
                    && GameVersion.TryParse(fileVersion, out var v)
                    && v.IsBuildDefined == fullFourPart)
                {
                    File.Copy(dll, Path.Combine(tempDir, "KSA.dll"));
                    return v;
                }
            }
            Assert.Fail("Test prerequisite: no assembly with a "
                        + (fullFourPart ? "4-part" : "less than 4-part")
                        + " file version found in " + binDir);
            throw new InvalidOperationException("unreachable");
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
