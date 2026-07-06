using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

using Moq;
using NUnit.Framework;

using CKAN;
using CKAN.IO;
using CKAN.Games;
using CKAN.Games.KerbalSpaceProgram;
using CKAN.Versioning;

using Tests.Data;

namespace Tests.Core
{
    [TestFixture]
    public class GameInstanceTests
    {
        private GameInstance? ksp;
        private string?       ksp_dir;
        private IUser?        nullUser;

        [SetUp]
        public void Setup()
        {
            ksp_dir = TestData.NewTempDir();
            nullUser = new NullUser();
            Utilities.CopyDirectory(TestData.good_ksp_dir(), ksp_dir, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());
            ksp = new GameInstance(new KerbalSpaceProgram(), ksp_dir, "test");
        }

        [TearDown]
        public void TearDown()
        {
            if (ksp != null)
            {
                // Manually dispose of RegistryManager
                // For some reason the KSP instance doesn't do this itself causing test failures because the registry
                // lock file is still in use. So just dispose of it ourselves.
                RegistryManager.DisposeInstance(ksp);
            }

            if (ksp_dir != null)
            {
                Directory.Delete(ksp_dir, true);
            }
        }

        [Test]
        public void NormalizeGameDir_FilesystemRoot_CombinesRooted()
        {
            // A pure drive letter must keep its trailing separator, otherwise
            // Path.Combine produces drive-relative paths like K:CKAN
            var root    = Path.GetPathRoot(Path.GetTempPath())!;
            var ckanDir = Path.Combine(GameInstance.NormalizeGameDir(root), "CKAN");
            Assert.AreEqual(CKANPathUtils.NormalizePath(Path.Combine(root, "CKAN")),
                            CKANPathUtils.NormalizePath(ckanDir));
        }

        [Test]
        public void IsGameDir()
        {
            var game = new KerbalSpaceProgram();

            // Our test data directory should be good.
            Assert.IsTrue(game.GameInFolder(new DirectoryInfo(TestData.good_ksp_dir())));

            // As should our copied folder.
            Assert.IsTrue(game.GameInFolder(new DirectoryInfo(ksp_dir!)));

            // And the one from our KSP instance.
            Assert.IsTrue(game.GameInFolder(new DirectoryInfo(ksp?.GameDir!)));

            // All these ones should be bad.
            foreach (string dir in TestData.bad_ksp_dirs())
            {
                Assert.IsFalse(game.GameInFolder(new DirectoryInfo(dir)));
            }
        }

        [Test]
        public void ToAbsoluteToRelative_KSP_Unchanged()
        {
            // KSP1's mod directory is inside GameDir, so the external-mod-root
            // remapping is a no-op and paths resolve and relativize against
            // GameDir exactly as before.
            Assert.IsFalse(ksp!.Game.ModDirectoryIsExternal);

            var abs = ksp.ToAbsoluteGameDir("GameData/ExampleMod/part.cfg");
            Assert.AreEqual(
                CKANPathUtils.NormalizePath(
                    Path.Combine(ksp.GameDir, "GameData", "ExampleMod", "part.cfg")),
                abs);
            Assert.AreEqual("GameData/ExampleMod/part.cfg", ksp.ToRelativeGameDir(abs));
        }

        [Test]
        public void Tutorial()
        {
            //Use Uri to avoid issues with windows vs linux line separators.
            var canonicalPath = new Uri(Path.Combine(ksp_dir!, "saves", "training")).LocalPath;
            var game = new KerbalSpaceProgram();
            Assert.IsTrue(game.AllowInstallationIn("Tutorial", out string? dest));
            Assert.AreEqual(
                new DirectoryInfo(ksp?.ToAbsoluteGameDir(dest ?? "")!),
                new DirectoryInfo(canonicalPath));
        }

        [Test]
        public void ToAbsolute()
        {
            Assert.AreEqual(
                CKANPathUtils.NormalizePath(
                    Path.Combine(ksp_dir!, "GameData/HydrazinePrincess")),
                ksp?.ToAbsoluteGameDir("GameData/HydrazinePrincess"));
        }

        [Test]
        public void ToRelative()
        {
            string absolute = Path.Combine(ksp_dir!, "GameData/HydrazinePrincess");

            Assert.AreEqual(
                "GameData/HydrazinePrincess",
                ksp?.ToRelativeGameDir(absolute));
        }

        [Test]
        public void Valid_MissingVersionData_False()
        {
            // Arrange
            string gamedir  = TestData.NewTempDir();
            string ckandir  = Path.Combine(gamedir, "CKAN");
            string buildid  = Path.Combine(gamedir, "buildID.txt");
            string readme   = Path.Combine(gamedir, "readme.txt");
            string jsonpath = Path.Combine(ckandir, "compatible_ksp_versions.json");
            const string compatible_ksp_versions_json = @"{
                ""VersionOfKspWhenWritten"": ""1.4.3"",
                ""CompatibleGameVersions"":   [""1.4""]
            }";

            // Generate a valid game dir except for missing buildID.txt and readme.txt
            Utilities.CopyDirectory(TestData.good_ksp_dir(), gamedir, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());
            File.Delete(buildid);
            File.Delete(readme);

            // Save GameDir/CKAN/compatible_ksp_versions.json
            Directory.CreateDirectory(ckandir);
            File.WriteAllText(jsonpath, compatible_ksp_versions_json);

            // Act
            GameInstance my_ksp = new GameInstance(new KerbalSpaceProgram(), gamedir, "missing-ver-test");

            // Assert
            Assert.IsFalse(my_ksp.Valid);

            Directory.Delete(gamedir, true);
        }

        [Test]
        public void Constructor_NullMainCompatVer_NoCrash()
        {
            // Arrange
            string gamedir  = TestData.NewTempDir();
            string ckandir  = Path.Combine(gamedir, "CKAN");
            string buildid  = Path.Combine(gamedir, "buildID.txt");
            string readme   = Path.Combine(gamedir, "readme.txt");
            string jsonpath = Path.Combine(ckandir, "compatible_ksp_versions.json");
            const string compatible_ksp_versions_json = @"{
                ""VersionOfKspWhenWritten"": null,
                ""CompatibleGameVersions"":   [""1.4""]
            }";

            // Generate a valid game dir except for missing buildID.txt and readme.txt
            Utilities.CopyDirectory(TestData.good_ksp_dir(), gamedir, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());
            File.Delete(buildid);
            File.Delete(readme);

            // Save GameDir/CKAN/compatible_ksp_versions.json
            Directory.CreateDirectory(ckandir);
            File.WriteAllText(jsonpath, compatible_ksp_versions_json);

            // Act & Assert
            Assert.DoesNotThrow(() =>
            {
                GameInstance my_ksp = new GameInstance(new KerbalSpaceProgram(), gamedir, "null-compat-ver-test");
            });

            Directory.Delete(gamedir, true);
        }

        [Test]
        public void AddSuppressedCompatWarningIdentifiers_WithIdentifiers_Works()
        {
            // Arrange
            using (var inst = new DisposableKSP())
            {
                var path = inst.KSP.ToAbsoluteGameDir("CKAN/suppressed_compat_warning_identifiers.json");

                // Act
                FileAssert.DoesNotExist(path);
                inst.KSP.AddSuppressedCompatWarningIdentifiers(new HashSet<string>
                                                               {
                                                                   "Mod1", "Mod2", "Mod3"
                                                               });

                // Assert
                FileAssert.Exists(path);
                Assert.AreEqual(@"{""GameVersionWhenWritten"":""0.25.0.642"",""Identifiers"":[""Mod1"",""Mod2"",""Mod3""]}",
                                File.ReadAllText(path));
                CollectionAssert.AreEquivalent(new string[] { "Mod1", "Mod2", "Mod3" },
                                               inst.KSP.GetSuppressedCompatWarningIdentifiers);
            }
        }

        [Test]
        public void UnmanagedFiles_ExternalModRoot_SurfacesDroppedFiles()
        {
            // Arrange: a game (like KSA) whose mod root lives outside GameDir.
            var gameDir = TestData.NewTempDir();
            var extRoot = TestData.NewTempDir();
            try
            {
                var inst     = new GameInstance(ExternalModGame(extRoot).Object, gameDir, "ext-test");
                var registry = CKAN.Registry.Empty(new RepositoryDataManager());

                // A plain file in GameDir, and a mod folder a user dropped into the
                // external mod root by hand.
                File.WriteAllText(Path.Combine(gameDir, "readme.txt"), "");
                var dropped = Path.Combine(extRoot, "SomeMod", "plugin.dll");
                Directory.CreateDirectory(Path.GetDirectoryName(dropped)!);
                File.WriteAllText(dropped, "");

                // Act
                var unmanaged = inst.UnmanagedFiles(registry).ToArray();

                // Assert: exactly the GameDir file and the external file (under the
                // "mods" prefix) are surfaced, with no duplicates or extras.
                CollectionAssert.AreEquivalent(
                    new[] { "readme.txt", "mods/SomeMod/plugin.dll" },
                    unmanaged);
            }
            finally
            {
                Directory.Delete(gameDir, true);
                Directory.Delete(extRoot, true);
            }
        }

        [Test]
        public void UnmanagedFiles_ExternalModRoot_ExcludesManagedFiles()
        {
            // Arrange: a CKAN-managed mod and a hand-dropped file, both under the
            // external mod root.
            var gameDir = TestData.NewTempDir();
            var extRoot = TestData.NewTempDir();
            try
            {
                var inst     = new GameInstance(ExternalModGame(extRoot).Object, gameDir, "ext-managed-test");
                var registry = CKAN.Registry.Empty(new RepositoryDataManager());

                var managed = Path.Combine(extRoot, "ManagedMod", "managed.dll");
                Directory.CreateDirectory(Path.GetDirectoryName(managed)!);
                File.WriteAllText(managed, "");
                var dropped = Path.Combine(extRoot, "DroppedMod", "dropped.dll");
                Directory.CreateDirectory(Path.GetDirectoryName(dropped)!);
                File.WriteAllText(dropped, "");

                // Tell the registry CKAN installed the managed file. Its registry key
                // goes through the same "mods/" mapping that UnmanagedFiles uses to
                // look owners up, so this exercises that round-trip.
                registry.RegisterModule(
                    CkanModule.FromJson(@"{
                        ""spec_version"": 1,
                        ""identifier"":   ""ManagedMod"",
                        ""author"":       ""tester"",
                        ""version"":      ""1.0"",
                        ""download"":     ""https://github.com/""
                    }"),
                    new[] { managed }, inst, false);

                // Act
                var unmanaged = inst.UnmanagedFiles(registry).ToArray();

                // Assert: the managed mod's file is filtered out, the hand-dropped one
                // is still surfaced.
                CollectionAssert.DoesNotContain(unmanaged, "mods/ManagedMod/managed.dll");
                CollectionAssert.Contains(unmanaged, "mods/DroppedMod/dropped.dll");
            }
            finally
            {
                Directory.Delete(gameDir, true);
                Directory.Delete(extRoot, true);
            }
        }

        [Test]
        public void UnmanagedFiles_ExternalModRootMissing_DoesNotThrow()
        {
            // Arrange: the external mod root does not exist yet (no mods installed).
            var gameDir = TestData.NewTempDir();
            var extRoot = Path.Combine(TestData.NewTempDir(), "does-not-exist");
            try
            {
                var inst     = new GameInstance(ExternalModGame(extRoot).Object, gameDir, "ext-missing-test");
                var registry = CKAN.Registry.Empty(new RepositoryDataManager());
                File.WriteAllText(Path.Combine(gameDir, "readme.txt"), "");

                // Act & Assert: the missing external root is skipped, GameDir still scanned.
                var unmanaged = Array.Empty<string>();
                Assert.DoesNotThrow(() => unmanaged = inst.UnmanagedFiles(registry).ToArray());
                CollectionAssert.Contains(unmanaged, "readme.txt");
            }
            finally
            {
                Directory.Delete(Path.GetDirectoryName(extRoot)!, true);
                Directory.Delete(gameDir, true);
            }
        }

        [Test]
        public void UnmanagedFiles_ExternalModRootSharedWithAnotherInstance_NotScanned()
        {
            // Two instances of an external-mod game share one external mods folder (like
            // KSA). The shared root must not be scanned, because the other instance's
            // CKAN-managed mods are unowned in this registry and would surface as
            // unmanaged (and be deletable) from the wrong instance's window.
            var gameDirA = TestData.NewTempDir();
            var gameDirB = TestData.NewTempDir();
            var extRoot  = TestData.NewTempDir();
            try
            {
                var instA    = new GameInstance(ExternalModGame(extRoot).Object, gameDirA, "A");
                var instB    = new GameInstance(ExternalModGame(extRoot).Object, gameDirB, "B");
                var registry = CKAN.Registry.Empty(new RepositoryDataManager());

                File.WriteAllText(Path.Combine(gameDirA, "readme.txt"), "");
                var shared = Path.Combine(extRoot, "SomeMod", "plugin.dll");
                Directory.CreateDirectory(Path.GetDirectoryName(shared)!);
                File.WriteAllText(shared, "");

                // Act: pass both instances so the shared root is detected.
                var unmanaged = instA.UnmanagedFiles(registry, new[] { instA, instB }).ToArray();

                // Assert: only A's own GameDir file, the shared external file is skipped.
                CollectionAssert.AreEquivalent(new[] { "readme.txt" }, unmanaged);

                // And with only this instance passed, the external root is scanned again.
                var soleUser = instA.UnmanagedFiles(registry, new[] { instA }).ToArray();
                CollectionAssert.Contains(soleUser, "mods/SomeMod/plugin.dll");

                // The gate must not depend on this instance being in the list: if A is
                // omitted but sibling B (which shares the root) is present, the shared
                // root is still skipped, so the gate cannot fail open.
                var selfOmitted = instA.UnmanagedFiles(registry, new[] { instB }).ToArray();
                CollectionAssert.DoesNotContain(selfOmitted, "mods/SomeMod/plugin.dll");
            }
            finally
            {
                Directory.Delete(gameDirA, true);
                Directory.Delete(gameDirB, true);
                Directory.Delete(extRoot, true);
            }
        }

        [Test]
        public void UnmanagedFiles_LiteralModsFolderInGameDir_ExcludedFromExternalMapping()
        {
            // A real <GameDir>/mods folder maps to the same "mods/..." key as the external
            // root, and ToAbsoluteGameDir always resolves that key back to the external
            // root, so surfacing it would let a delete or show-in-folder hit the wrong
            // file (and a same-named pair would collide under Distinct). The whole subtree
            // is excluded; only the external file keeps the "mods/..." key.
            var gameDir = TestData.NewTempDir();
            var extRoot = TestData.NewTempDir();
            try
            {
                var inst     = new GameInstance(ExternalModGame(extRoot).Object, gameDir, "collide-test");
                var registry = CKAN.Registry.Empty(new RepositoryDataManager());

                // A GameDir/mods file whose key would collide with an external file of the
                // same name, plus a GameDir/mods file with a unique name.
                var gameDirCollide = Path.Combine(gameDir, "mods", "SomeMod", "plugin.dll");
                Directory.CreateDirectory(Path.GetDirectoryName(gameDirCollide)!);
                File.WriteAllText(gameDirCollide, "");
                var gameDirUnique = Path.Combine(gameDir, "mods", "OnlyHere", "note.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(gameDirUnique)!);
                File.WriteAllText(gameDirUnique, "");
                var external = Path.Combine(extRoot, "SomeMod", "plugin.dll");
                Directory.CreateDirectory(Path.GetDirectoryName(external)!);
                File.WriteAllText(external, "");

                // Act
                var unmanaged = inst.UnmanagedFiles(registry).ToArray();

                // Assert: the external file surfaces exactly once (no collision), and no
                // <GameDir>/mods file is surfaced (neither the colliding nor the unique
                // one), so nothing can mis-resolve to the external root.
                Assert.AreEqual(1, unmanaged.Count(p => p == "mods/SomeMod/plugin.dll"));
                CollectionAssert.DoesNotContain(unmanaged, "mods/OnlyHere/note.txt");
            }
            finally
            {
                Directory.Delete(gameDir, true);
                Directory.Delete(extRoot, true);
            }
        }

        [Test]
        public void UnmanagedFiles_BareModsFileInGameDir_Excluded()
        {
            // An extensionless file literally named "mods" at the game root maps to the
            // bare "mods" key, which ToAbsoluteGameDir resolves to the external root
            // directory, so it must be excluded too, not just the mods/ subtree.
            var gameDir = TestData.NewTempDir();
            var extRoot = TestData.NewTempDir();
            try
            {
                var inst     = new GameInstance(ExternalModGame(extRoot).Object, gameDir, "bare-mods-test");
                var registry = CKAN.Registry.Empty(new RepositoryDataManager());

                File.WriteAllText(Path.Combine(gameDir, "mods"), "");
                File.WriteAllText(Path.Combine(gameDir, "readme.txt"), "");

                // Act
                var unmanaged = inst.UnmanagedFiles(registry).ToArray();

                // Assert: the bare "mods" file is excluded, an ordinary GameDir file shows.
                CollectionAssert.DoesNotContain(unmanaged, "mods");
                CollectionAssert.Contains(unmanaged, "readme.txt");
            }
            finally
            {
                Directory.Delete(gameDir, true);
                Directory.Delete(extRoot, true);
            }
        }

        // A minimal fake game whose mod directory lives outside GameDir (like KSA),
        // pointed at a caller-controlled folder so the external-root scan can be
        // exercised without touching the real Documents mods folder. DetectVersion
        // returns null so construction takes the empty-compatible-versions path and
        // needs no build map.
        private static Mock<IGame> ExternalModGame(string externalModRoot)
        {
            var game = new Mock<IGame>();
            game.Setup(g => g.ShortName).Returns("FakeExternal");
            game.Setup(g => g.CompatibleVersionsFile).Returns("compatible_versions.json");
            game.Setup(g => g.DetectVersion(It.IsAny<DirectoryInfo>())).Returns((GameVersion?)null);
            game.Setup(g => g.StockFolders).Returns(Array.Empty<string>());
            game.Setup(g => g.PrimaryModDirectoryRelative).Returns("mods");
            game.Setup(g => g.ModDirectoryIsExternal).Returns(true);
            game.Setup(g => g.PrimaryModDirectory(It.IsAny<GameInstance>())).Returns(externalModRoot);
            return game;
        }
    }
}
