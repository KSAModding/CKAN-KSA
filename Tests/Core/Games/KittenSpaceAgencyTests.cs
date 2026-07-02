using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Newtonsoft.Json.Linq;
using NUnit.Framework;

using CKAN;
using CKAN.IO;
using CKAN.Versioning;
using CKAN.Games.KittenSpaceAgency;
using CKAN.Games.KerbalSpaceProgram;
using ManifestModEntry = CKAN.Games.KittenSpaceAgency.KittenSpaceAgency.ManifestModEntry;

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
        public void ParseManifest_EntryWithUnknownKey_KeepsItAsExtraLine()
        {
            // The game may add fields we do not understand yet. Anything besides
            // id and enabled must survive so SerializeManifest can write it back.
            var text = "[[mods]]\n"
                     + "id = \"Core\"\n"
                     + "enabled = true\n"
                     + "version = \"1.0\"\n";

            var entries = KittenSpaceAgency.ParseManifest(text);

            Assert.AreEqual(1, entries.Count);
            Assert.AreEqual("Core", entries[0].Id);
            Assert.IsTrue(entries[0].Enabled);
            CollectionAssert.Contains(entries[0].ExtraLines, "version = \"1.0\"");
        }

        [Test]
        public void SerializeManifest_ParseManifest_RoundTrips()
        {
            var original = new List<ManifestModEntry>
            {
                new ManifestModEntry { Id = "Core", Enabled = true },
                new ManifestModEntry { Id = "KSP-Redux", Enabled = false },
            };

            var reparsed = KittenSpaceAgency.ParseManifest(
                               KittenSpaceAgency.SerializeManifest(original));

            Assert.AreEqual(original.Select(e => e.Id), reparsed.Select(e => e.Id));
            Assert.AreEqual(original.Select(e => e.Enabled), reparsed.Select(e => e.Enabled));
        }

        [Test]
        public void ReconcileManifest_NewlyInstalledMod_AddedAsEnabled()
        {
            // A mod CKAN just installed has no entry yet, so one must be added
            // and enabled, or the game leaves it inactive until the user notices.
            var entries = new List<ManifestModEntry>
            {
                new ManifestModEntry { Id = "Core", Enabled = true },
            };

            var result = KittenSpaceAgency.ReconcileManifest(
                             entries,
                             previouslyManaged:  new HashSet<string>(),
                             currentIdentifiers: new HashSet<string> { "KSP-Redux" });

            var added = result.Single(e => e.Id == "KSP-Redux");
            Assert.IsTrue(added.Enabled);
            // The entry CKAN does not manage is untouched.
            Assert.IsTrue(result.Single(e => e.Id == "Core").Enabled);
        }

        [Test]
        public void ReconcileManifest_DisabledMod_IsFlippedToEnabled()
        {
            // The game auto discovers a dropped mod folder as disabled. Once CKAN
            // is the one managing that mod, it needs to be active on next launch.
            var entries = new List<ManifestModEntry>
            {
                new ManifestModEntry { Id = "KSP-Redux", Enabled = false },
            };

            var result = KittenSpaceAgency.ReconcileManifest(
                             entries,
                             previouslyManaged:  new HashSet<string> { "KSP-Redux" },
                             currentIdentifiers: new HashSet<string> { "KSP-Redux" });

            Assert.IsTrue(result.Single(e => e.Id == "KSP-Redux").Enabled);
        }

        [Test]
        public void ReconcileManifest_UninstalledMod_EntryRemoved()
        {
            // KSP-Redux was previously installed by CKAN and is now gone. Its
            // manifest entry must be removed, not left enabled pointing at nothing.
            var entries = new List<ManifestModEntry>
            {
                new ManifestModEntry { Id = "KSP-Redux", Enabled = true },
            };

            var result = KittenSpaceAgency.ReconcileManifest(
                             entries,
                             previouslyManaged:  new HashSet<string> { "KSP-Redux" },
                             currentIdentifiers: new HashSet<string>());

            CollectionAssert.IsEmpty(result);
        }

        [Test]
        public void ReconcileManifest_UnmanagedEntry_NeverRemoved()
        {
            // Core was never installed by CKAN, so even though it is not in
            // currentIdentifiers, it must not be treated as removable.
            var entries = new List<ManifestModEntry>
            {
                new ManifestModEntry { Id = "Core", Enabled = true },
            };

            var result = KittenSpaceAgency.ReconcileManifest(
                             entries,
                             previouslyManaged:  new HashSet<string>(),
                             currentIdentifiers: new HashSet<string>());

            CollectionAssert.Contains(result.Select(e => e.Id), "Core");
        }

        [Test]
        public void ReadManagedMods_CorruptFile_ReturnsEmptyInsteadOfThrowing()
        {
            // A malformed managed-mods file must not permanently block manifest
            // updates until someone deletes it by hand.
            var path = Path.Combine(tempDir, "managed-mods.json");
            File.WriteAllText(path, "{ not valid json");

            var result = KittenSpaceAgency.ReadManagedMods(path);

            CollectionAssert.IsEmpty(result);
        }

        [Test]
        public void ReadManagedMods_MissingFile_ReturnsEmpty()
        {
            var result = KittenSpaceAgency.ReadManagedMods(
                             Path.Combine(tempDir, "does-not-exist.json"));

            CollectionAssert.IsEmpty(result);
        }

        [Test]
        public void UpdateManifestFile_ParentDirMissing_CreatesItAndWrites()
        {
            // The game may never have run yet, so <Documents>/My Games/Kitten
            // Space Agency/ (the manifest's parent) might not exist at all.
            var manifestPath    = Path.Combine(tempDir, "not-yet-created", "manifest.toml");
            var managedModsPath = Path.Combine(tempDir, "managed-mods.json");

            KittenSpaceAgency.UpdateManifestFile(new HashSet<string> { "KSP-Redux" },
                                                 manifestPath, managedModsPath);

            var entries = KittenSpaceAgency.ParseManifest(File.ReadAllText(manifestPath));
            Assert.IsTrue(entries.Single(e => e.Id == "KSP-Redux").Enabled);
        }

        [Test]
        public void UpdateManifestFile_CorruptManagedModsFile_StillUpdatesManifest()
        {
            // A corrupt managed-mods file must not stop the manifest itself from
            // being kept in sync, it should just fall back to an empty "previously
            // managed" set for this run.
            var manifestPath    = Path.Combine(tempDir, "manifest.toml");
            var managedModsPath = Path.Combine(tempDir, "managed-mods.json");
            File.WriteAllText(managedModsPath, "{ not valid json");

            KittenSpaceAgency.UpdateManifestFile(new HashSet<string> { "KSP-Redux" },
                                                 manifestPath, managedModsPath);

            var entries = KittenSpaceAgency.ParseManifest(File.ReadAllText(manifestPath));
            Assert.IsTrue(entries.Single(e => e.Id == "KSP-Redux").Enabled);
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
    }
}
