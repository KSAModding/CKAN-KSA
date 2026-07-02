using System;
using System.IO;
using System.Linq;

using Newtonsoft.Json.Linq;
using NUnit.Framework;

using CKAN;
using CKAN.IO;
using CKAN.Versioning;
using CKAN.Games.KittenSpaceAgency;
using CKAN.Games.KerbalSpaceProgram;

namespace Tests.Core.Games
{
    [TestFixture]
    public class KittenSpaceAgencyTests
    {
        private string tempDir = "";
        private GameInstance? instance;

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
            if (instance != null)
            {
                // Release the registry lock file so the temp dir can be deleted.
                RegistryManager.DisposeInstance(instance);
                instance = null;
            }
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }

        // A KSA instance rooted at a throwaway game dir. Constructing it only
        // creates the CKAN subfolder; the external mod root is never written to,
        // the tests below only exercise path math against it.
        private GameInstance MakeInstance()
        {
            var gameDir = Path.Combine(tempDir, "game");
            Directory.CreateDirectory(gameDir);
            return instance = new GameInstance(new KittenSpaceAgency(), gameDir, "test");
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

        [Test]
        public void ParseBuildsJson_NormalizesBuildCounter()
        {
            // A build map as it arrives from RefreshVersions, with raw build counters.
            var json = JArray.Parse("[ \"2026.6.9.4750\", \"2026.2.10.3538\" ]");

            var versions = new KittenSpaceAgency().ParseBuildsJson(json);

            // Build counter pinned to 0, revision preserved.
            Assert.That(versions, Has.All.Matches<GameVersion>(v => v.Patch == 0));
            CollectionAssert.Contains(versions, GameVersion.Parse("2026.6.0.4750"));
        }

        [Test]
        public void ModDirectory_IsExternalUserModsFolder()
        {
            // The mod root is the game's own Documents mods folder, outside GameDir,
            // and CKAN records installed files under the "mods" prefix. These are
            // independent structural checks, not a recomputation of the production
            // path expression, so a broken (e.g. non-Windows) mod root would fail here.
            var inst = MakeInstance();

            Assert.IsTrue(inst.Game.ModDirectoryIsExternal);
            Assert.AreEqual("mods", inst.Game.PrimaryModDirectoryRelative);

            var modRoot = inst.Game.PrimaryModDirectory(inst);
            Assert.IsTrue(Path.IsPathRooted(modRoot),
                          "mod root must be absolute");
            Assert.IsFalse(modRoot.StartsWith(inst.GameDir, StringComparison.Ordinal),
                           "mod root must be outside GameDir");
            Assert.IsTrue(modRoot.Replace('\\', '/').EndsWith(
                              "My Games/Kitten Space Agency/mods", StringComparison.Ordinal),
                          "mod root must be the game's Documents mods folder");
        }

        [Test]
        public void ToAbsoluteGameDir_ModPrefix_ResolvesToExternalRoot()
        {
            var inst    = MakeInstance();
            var modRoot = inst.Game.PrimaryModDirectory(inst);

            // A registry-relative mod path maps under the external mod root,
            // not <GameDir>/mods.
            Assert.AreEqual(
                CKANPathUtils.NormalizePath(
                    Path.Combine(modRoot, "AdvancedFlightComputer", "mod.dll")),
                inst.ToAbsoluteGameDir("mods/AdvancedFlightComputer/mod.dll"));

            // The bare prefix maps to the mod root itself.
            Assert.AreEqual(modRoot, inst.ToAbsoluteGameDir("mods"));
        }

        [Test]
        public void ToRelativeGameDir_ExternalPath_MapsBackToModPrefix()
        {
            var inst    = MakeInstance();
            var modRoot = inst.Game.PrimaryModDirectory(inst);
            var abs     = Path.Combine(modRoot, "AdvancedFlightComputer", "mod.dll");

            Assert.AreEqual("mods/AdvancedFlightComputer/mod.dll",
                            inst.ToRelativeGameDir(abs));
            Assert.AreEqual("mods", inst.ToRelativeGameDir(modRoot));
        }

        [Test]
        public void ToAbsolute_ToRelative_ModPath_RoundTrips()
        {
            var inst = MakeInstance();
            const string rel = "mods/AdvancedFlightComputer/Plugins/mod.dll";

            Assert.AreEqual(rel, inst.ToRelativeGameDir(inst.ToAbsoluteGameDir(rel)));
        }

        [Test]
        public void ToAbsoluteGameDir_NonModPath_StaysUnderGameDir()
        {
            var inst = MakeInstance();

            // A GameRoot-style install (or anything not under the "mods" prefix)
            // still resolves relative to GameDir, not the external mod root.
            Assert.AreEqual(
                CKANPathUtils.NormalizePath(Path.Combine(inst.GameDir, "readme.txt")),
                inst.ToAbsoluteGameDir("readme.txt"));

            // A path that merely starts with the prefix letters but is not the
            // prefix folder must not be treated as an external mod path.
            Assert.AreEqual(
                CKANPathUtils.NormalizePath(Path.Combine(inst.GameDir, "modsomething", "x")),
                inst.ToAbsoluteGameDir("modsomething/x"));
        }

        [Test]
        public void FormatVersion_MonthGranular_AppendsWildcard()
        {
            // A year.month compatibility value matches any revision that month, so it
            // is shown as year.month.* rather than the bare, incomplete-looking 2026.6.
            Assert.AreEqual("2026.6.*",
                            new KittenSpaceAgency().FormatVersion(GameVersion.Parse("2026.6")));
        }

        [Test]
        public void FormatVersion_FullOrYearOnly_Unchanged()
        {
            var game = new KittenSpaceAgency();

            // A fully specified build is shown as-is.
            Assert.AreEqual("2026.6.0.4750",
                            game.FormatVersion(GameVersion.Parse("2026.6.0.4750")));
            // Year-only is not month-granular, so it is left alone.
            Assert.AreEqual("2026", game.FormatVersion(GameVersion.Parse("2026")));
        }

        [Test]
        public void FormatVersion_OtherGames_Unchanged()
        {
            // The wildcard rendering is KSA-specific; other games show the plain string.
            Assert.AreEqual("1.12",
                            new KerbalSpaceProgram().FormatVersion(GameVersion.Parse("1.12")));
        }

        [Test]
        public void AdjustCommandLine_NoStarMap_LaunchesKSAUnchanged()
        {
            // no StarMap.exe under the mod root, launch line stays as-is
            var args = new[] { "KSA.exe", "-single-instance" };

            var adjusted = new KittenSpaceAgency().AdjustCommandLine(args, tempDir, fakeGameDir);

            CollectionAssert.AreEqual(args, adjusted);
        }

        [Test]
        public void AdjustCommandLine_StarMapPresent_SwapsInStarMapExe()
        {
            // release folder is versioned, not fixed, so it has to be found
            // by scanning rather than a hardcoded path
            var starMapExe = WriteFakeStarMapExe();

            var adjusted = new KittenSpaceAgency().AdjustCommandLine(
                               new[] { "KSA.exe", "-single-instance" }, tempDir, fakeGameDir);

            Assert.AreEqual(starMapExe, adjusted[0]);
            Assert.AreEqual("-single-instance", adjusted[1]);
        }

        [Test]
        public void AdjustCommandLine_StarMapAlongsideOtherMods_StillFound()
        {
            // Real mods folders have other stuff in them (AutoStage, DeltaVMap,
            // etc), StarMap has to be picked out of the mix.
            Directory.CreateDirectory(Path.Combine(tempDir, "AutoStage"));
            Directory.CreateDirectory(Path.Combine(tempDir, "DeltaVMap"));
            var starMapExe = WriteFakeStarMapExe();

            var adjusted = new KittenSpaceAgency().AdjustCommandLine(
                               new[] { "KSA.exe", "-single-instance" }, tempDir, fakeGameDir);

            Assert.AreEqual(starMapExe, adjusted[0]);
        }

        [Test]
        public void AdjustCommandLine_SteamLaunch_NeverSwapped()
        {
            // steam:// url doesn't match the KSA.exe anchor, leave it alone
            WriteFakeStarMapExe();

            var args = new[] { "steam://rungameid/12345" };
            var adjusted = new KittenSpaceAgency().AdjustCommandLine(args, tempDir, fakeGameDir);

            CollectionAssert.AreEqual(args, adjusted);
        }

        [Test]
        public void AdjustCommandLine_NoStarMap_NoConfigWritten()
        {
            // StarMap isn't there at all, so there's nothing to configure
            new KittenSpaceAgency().AdjustCommandLine(
                new[] { "KSA.exe", "-single-instance" }, tempDir, fakeGameDir);

            Assert.IsFalse(File.Exists(Path.Combine(tempDir, StarMapTestFolder, "StarMapConfig.json")));
        }

        [Test]
        public void AdjustCommandLine_StarMapPresent_WritesFreshConfig()
        {
            // First time StarMap shows up, no config yet, so one gets created
            // pointing at this instance's game folder.
            WriteFakeStarMapExe();

            new KittenSpaceAgency().AdjustCommandLine(
                new[] { "KSA.exe", "-single-instance" }, tempDir, fakeGameDir);

            var config = JObject.Parse(File.ReadAllText(
                             Path.Combine(tempDir, StarMapTestFolder, "StarMapConfig.json")));
            Assert.AreEqual(fakeGameDir, (string?)config["GameLocation"]);
            Assert.AreEqual("", (string?)config["RepositoryLocation"]);
        }

        [Test]
        public void AdjustCommandLine_StarMapPresent_FixesGameLocationKeepsOtherFields()
        {
            // A config already exists (maybe from a stale instance, or fields
            // a newer StarMap added). GameLocation must get corrected, but
            // nothing else should be touched.
            WriteFakeStarMapExe();
            var configPath = Path.Combine(tempDir, StarMapTestFolder, "StarMapConfig.json");
            File.WriteAllText(configPath,
                              "{ \"GameLocation\": \"C:/somewhere/stale\", "
                              + "\"RepositoryLocation\": \"https://example.com/repo\", "
                              + "\"SomeFutureSetting\": true }");

            new KittenSpaceAgency().AdjustCommandLine(
                new[] { "KSA.exe", "-single-instance" }, tempDir, fakeGameDir);

            var config = JObject.Parse(File.ReadAllText(configPath));
            Assert.AreEqual(fakeGameDir, (string?)config["GameLocation"]);
            Assert.AreEqual("https://example.com/repo", (string?)config["RepositoryLocation"]);
            Assert.AreEqual(true, (bool?)config["SomeFutureSetting"]);
        }

        private readonly string fakeGameDir = "C:/fake/Kitten Space Agency";

        // matches the real versioned folder name, not a fixed "StarMap"
        private const string StarMapTestFolder = "StarMapLauncher-0.4.5";

        private string WriteFakeStarMapExe()
        {
            var starMapDir = Path.Combine(tempDir, StarMapTestFolder);
            Directory.CreateDirectory(starMapDir);
            var starMapExe = Path.Combine(starMapDir, "StarMap.exe");
            File.WriteAllText(starMapExe, "");
            return starMapExe;
        }
    }
}
