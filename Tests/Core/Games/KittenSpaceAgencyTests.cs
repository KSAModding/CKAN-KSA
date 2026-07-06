using System;
using System.Collections;
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
using CKAN.Games.KerbalSpaceProgram2;
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
        public void SerializeManifest_ParseManifest_SpecialCharactersRoundTrip()
        {
            // A mod folder name may contain characters that break a naive TOML basic
            // string (a double quote closes the string early; a backslash starts an
            // escape). These are legal in file names on case-sensitive systems, so they
            // must be escaped on write and decoded on read or the id is silently
            // corrupted and the game's own parser can choke on the manifest.
            var original = new List<ManifestModEntry>
            {
                new ManifestModEntry { Id = "quote\"mod",  Enabled = true },
                new ManifestModEntry { Id = "back\\slash", Enabled = false },
            };

            var reparsed = KittenSpaceAgency.ParseManifest(
                               KittenSpaceAgency.SerializeManifest(original));

            Assert.AreEqual(original.Select(e => e.Id), reparsed.Select(e => e.Id));
            Assert.AreEqual(original.Select(e => e.Enabled), reparsed.Select(e => e.Enabled));
        }

        [Test]
        public void ParseValue_EscapedDoubleQuote_DecodedNotTruncated()
        {
            // Reading a game-written id that escapes a quote must yield the real value,
            // not a value truncated at the inner quote.
            Assert.AreEqual("a\"b", KittenSpaceAgency.ParseValue("\"a\\\"b\""));
        }

        [Test]
        public void SerializeManifest_ControlCharacterInId_EscapedNotEmittedRaw()
        {
            // The game's Tomlet reader rejects a raw control character in a basic string,
            // so DEL (U+007F) and the C0 controls must be written as \uXXXX escapes or the
            // whole manifest fails to load.
            var text = KittenSpaceAgency.SerializeManifest(new List<ManifestModEntry>
            {
                new ManifestModEntry { Id = "del\u007Fhere", Enabled = true },
            });

            StringAssert.Contains("\\u007F", text);
            Assert.IsFalse(text.Contains('\u007F'), "a raw control char must not be emitted");
            // And it still round-trips back to the original id.
            var reparsed = KittenSpaceAgency.ParseManifest(text);
            Assert.AreEqual("del\u007Fhere", reparsed[0].Id);
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
        public void ReconcileManifest_ExistingDisabledEntry_LeftDisabled()
        {
            // A mod already in the manifest keeps its enabled state on every sync, so a
            // deliberate in-game disable survives an unrelated CKAN operation. This holds
            // even when CKAN has no record of the mod (empty previouslyManaged), because
            // the enabled decision never consults previouslyManaged, only whether the mod
            // is already present.
            var entries = new List<ManifestModEntry>
            {
                new ManifestModEntry { Id = "KSP-Redux", Enabled = false },
            };

            var result = KittenSpaceAgency.ReconcileManifest(
                             entries,
                             previouslyManaged:  new HashSet<string>(),
                             currentIdentifiers: new HashSet<string> { "KSP-Redux" });

            Assert.IsFalse(result.Single(e => e.Id == "KSP-Redux").Enabled);
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
            Assert.AreEqual("0.2",
                            new KerbalSpaceProgram2().FormatVersion(GameVersion.Parse("0.2")));
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

            // StarMap loads and runs the game itself and its solo mode takes no
            // arguments, so the launch line becomes just the StarMap exe: KSA's
            // own args are dropped (a forwarded arg would be read as a pipe name).
            CollectionAssert.AreEqual(new[] { starMapExe }, adjusted);
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

        [Test]
        public void AdjustCommandLine_MultipleStarMapFolders_PicksDeterministically()
        {
            // A leftover older StarMap folder next to the current one must not
            // make the choice depend on filesystem enumeration order.
            var older = Path.Combine(tempDir, "StarMapLauncher-0.4.4");
            var newer = Path.Combine(tempDir, "StarMapLauncher-0.4.5");
            Directory.CreateDirectory(older);
            Directory.CreateDirectory(newer);
            File.WriteAllText(Path.Combine(older, "StarMap.exe"), "");
            File.WriteAllText(Path.Combine(newer, "StarMap.exe"), "");

            var adjusted = new KittenSpaceAgency().AdjustCommandLine(
                               new[] { "KSA.exe", "-single-instance" }, tempDir, fakeGameDir);

            Assert.AreEqual(Path.Combine(newer, "StarMap.exe"), adjusted[0]);
        }

        [Test]
        public void AdjustCommandLine_CorruptExistingConfig_DoesNotThrowAndRewrites()
        {
            // A blank or corrupt StarMapConfig.json (e.g. a partial write from a
            // prior StarMap crash) must not crash the launch. AdjustCommandLine
            // runs before PlayGame's own error handling, so it has to stay
            // throw-free; the config is repaired and StarMap still launches.
            var starMapExe = WriteFakeStarMapExe();
            var configPath = Path.Combine(tempDir, StarMapTestFolder, "StarMapConfig.json");
            File.WriteAllText(configPath, "{ this is not valid json");

            string[] adjusted = Array.Empty<string>();
            Assert.DoesNotThrow(() =>
                adjusted = new KittenSpaceAgency().AdjustCommandLine(
                    new[] { "KSA.exe", "-single-instance" }, tempDir, fakeGameDir));

            Assert.AreEqual(starMapExe, adjusted[0]);
            var config = JObject.Parse(File.ReadAllText(configPath));
            Assert.AreEqual(fakeGameDir, (string?)config["GameLocation"]);
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

        [Test]
        public void ModFolderNames_DerivesTopLevelFoldersUnderModDir()
        {
            var files = new[]
            {
                "mods/AdvancedFlightComputer/AdvancedFlightComputer.dll",
                "mods/AdvancedFlightComputer/mod.toml",
                "mods/StageInfo/StageInfo.dll",
                "mods/StageInfo/mod.toml",
                "readme.txt",              // GameRoot install, ignored
                "Content/Core/thing.xml",  // stock content, ignored
            };

            var folders = KittenSpaceAgency.ModFolderNames(files, "mods");

            CollectionAssert.AreEquivalent(
                new[] { "AdvancedFlightComputer", "StageInfo" }, folders);
        }

        [Test]
        public void ModFolderNames_UsesFolderNameNotIdentifier()
        {
            // The manifest id must be the on-disk folder the game sees, even if a mod
            // installs into a folder whose name differs from its CKAN identifier.
            var files = new[]
            {
                "mods/Real-Folder-Name/plugin.dll",
                "mods/Real-Folder-Name/mod.toml",
            };

            CollectionAssert.AreEqual(new[] { "Real-Folder-Name" },
                                      KittenSpaceAgency.ModFolderNames(files, "mods"));
        }

        [Test]
        public void ModFolderNames_IgnoresFolderWithoutModToml()
        {
            // The game only treats a folder as a mod if it contains a mod.toml, so a
            // bundled data-only folder must not produce a phantom manifest entry.
            var files = new[]
            {
                "mods/TheMod/TheMod.dll",
                "mods/TheMod/mod.toml",
                "mods/SharedAssets/texture.png",   // no mod.toml -> not a mod
            };

            CollectionAssert.AreEqual(new[] { "TheMod" },
                                      KittenSpaceAgency.ModFolderNames(files, "mods"));
        }

        [Test]
        public void ParseValue_StripsQuotesAndInlineComments()
        {
            Assert.AreEqual("Core",  KittenSpaceAgency.ParseValue("\"Core\""));
            Assert.AreEqual("Core",  KittenSpaceAgency.ParseValue("'Core'"));
            Assert.AreEqual("Core",  KittenSpaceAgency.ParseValue("\"Core\" # keep"));
            Assert.AreEqual("true",  KittenSpaceAgency.ParseValue("true # on"));
            Assert.AreEqual("true",  KittenSpaceAgency.ParseValue("  true  "));
        }

        [Test]
        public void ParseManifest_InlineCommentOnEnabled_NotMisreadAsDisabled()
        {
            // A trailing comment on enabled must not flip a mod off (the old parser
            // compared the whole remainder against "true").
            var entries = KittenSpaceAgency.ParseManifest(
                "[[mods]]\nid = \"Core\"\nenabled = true # stock\n");

            Assert.IsTrue(entries.Single(e => e.Id == "Core").Enabled);
        }

        [Test]
        public void ParseManifest_SingleQuotedId_Parsed()
        {
            var entries = KittenSpaceAgency.ParseManifest(
                "[[mods]]\nid = 'MyMod'\nenabled = true\n");

            Assert.AreEqual("MyMod", entries.Single().Id);
        }

        [Test]
        public void ParseManifest_MissingEnabledKey_DefaultsToEnabled()
        {
            // The game's ModEntry defaults a missing enabled key to true, so an entry
            // without one must not be silently rewritten as disabled.
            var entries = KittenSpaceAgency.ParseManifest("[[mods]]\nid = \"Foo\"\n");

            Assert.IsTrue(entries.Single().Enabled);
        }

        [Test]
        public void ModFolderNames_IgnoresLooseFilesDirectlyUnderModDir()
        {
            // A file installed straight into mods/ (no subfolder) is not a mod folder,
            // so it must not produce a phantom manifest entry the game would prune.
            var files = new[]
            {
                "mods/loose.cfg",
                "mods/RealMod/plugin.dll",
                "mods/RealMod/mod.toml",
            };

            CollectionAssert.AreEqual(new[] { "RealMod" },
                                      KittenSpaceAgency.ModFolderNames(files, "mods"));
        }

        [Test]
        public void ReconcileManifest_MatchesExistingEntryByFileSystemCasing()
        {
            // Folder names are matched the way the file system does. On Windows a
            // casing difference must not add a duplicate entry for the same folder.
            var entries = new List<ManifestModEntry>
            {
                new ManifestModEntry { Id = "MyMod", Enabled = true },
            };

            var result = KittenSpaceAgency.ReconcileManifest(
                             entries,
                             previouslyManaged:  new HashSet<string>(),
                             currentIdentifiers: new HashSet<string> { "mymod" });

            Assert.AreEqual(Platform.IsWindows ? 1 : 2, result.Count);
        }

        [Test]
        public void GameSurface_StaticProperties_MatchGameConventions()
        {
            var game = new KittenSpaceAgency();

            Assert.AreEqual(new DateTime(2025, 11, 14), game.FirstReleaseDate);
            CollectionAssert.IsEmpty(game.AlternateModDirectoriesRelative);
            // The stock content the game ships with, which CKAN must not manage.
            CollectionAssert.Contains(game.StockFolders, "Content");
            CollectionAssert.Contains(game.StockFolders, "Content/Core");
            CollectionAssert.IsEmpty(game.LeaveEmptyInClones);
            CollectionAssert.IsEmpty(game.ReservedPaths);
            CollectionAssert.IsEmpty(game.CreateableInstallTos);
            // The installer may create the per-mod folders under the mod root.
            CollectionAssert.AreEqual(new[] { "mods" }, game.CreateableDirs);
            CollectionAssert.IsEmpty(game.AutoRemovableDirs);
            // KSA has no paid DLC and no named install filter presets.
            CollectionAssert.IsEmpty(game.DlcDetectors);
            CollectionAssert.IsEmpty(game.InstallFilterPresets);
            Assert.DoesNotThrow(() => game.RebuildSubdirectories(tempDir));
        }

        [Test]
        public void AllowInstallationIn_NamedTargets_NoneBesidesImplicitModRoot()
        {
            // The mod root is allowed implicitly via PrimaryModDirectoryRelative;
            // KSA defines no other named install_to targets.
            Assert.IsFalse(new KittenSpaceAgency().AllowInstallationIn("GameData", out _));
            Assert.IsFalse(new KittenSpaceAgency().AllowInstallationIn("BepInEx/plugins", out _));
        }

        [Test]
        public void MetadataAndCommunityUrls_PointAtKsaResources()
        {
            var game = new KittenSpaceAgency();

            // Assert the repo names, not the owning org, so these hold across a
            // transfer of the metadata repos to another org.
            StringAssert.Contains("KSA-CKAN-meta", game.DefaultRepositoryURL.ToString());
            StringAssert.Contains("repositories.json", game.RepositoryListURL.ToString());
            StringAssert.Contains("KSA-NetKAN", game.MetadataBugtrackerURL.ToString());
            Assert.IsNotNull(game.DiscordURL);
            Assert.IsNotNull(game.ModSupportURL);
        }

        [Test]
        public void IsReservedDirectory_GameCkanAndModRoot_True()
        {
            var inst = MakeInstance();
            var game = inst.Game;

            Assert.IsTrue(game.IsReservedDirectory(inst, inst.GameDir));
            Assert.IsTrue(game.IsReservedDirectory(inst, inst.CkanDir));
            Assert.IsTrue(game.IsReservedDirectory(inst, game.PrimaryModDirectory(inst)));
            Assert.IsFalse(game.IsReservedDirectory(
                inst, CKANPathUtils.NormalizePath(Path.Combine(inst.GameDir, "Content"))));
        }

        [Test]
        public void DefaultCommandLines_AnchorPresent_LaunchesAnchorExe()
        {
            var gameDir = new DirectoryInfo(tempDir);
            File.WriteAllText(Path.Combine(tempDir, "KSA.exe"), "");

            var lines = new KittenSpaceAgency().DefaultCommandLines(new SteamLibrary(null),
                                                                    gameDir);

            // No Steam library, so the only launch line is the game exe itself.
            Assert.AreEqual(1, lines.Length);
            if (Platform.IsMac)
            {
                StringAssert.Contains("KSA.app", lines[0]);
            }
            else
            {
                StringAssert.Contains("KSA.exe", lines[0]);
                StringAssert.EndsWith("-single-instance", lines[0]);
            }
        }

        [Test]
        public void DefaultCommandLines_AnchorMissing_FallsBackToDefaultName()
        {
            // An unusual install without the anchor file still gets a launch line
            // built from the default anchor name.
            var lines = new KittenSpaceAgency().DefaultCommandLines(new SteamLibrary(null),
                                                                    new DirectoryInfo(tempDir));

            Assert.AreEqual(1, lines.Length);
            if (!Platform.IsMac)
            {
                StringAssert.Contains("KSA.exe", lines[0]);
            }
        }

        [Test]
        public void AdjustCommandLine_InstanceOverload_NonAnchorArgs_Unchanged()
        {
            // The GameInstance overload resolves the user's real mod root; with a
            // launch line that does not start with KSA.exe nothing is swapped and
            // nothing may be written.
            var inst = MakeInstance();
            var args = new[] { "steam://rungameid/12345" };

            CollectionAssert.AreEqual(args, inst.Game.AdjustCommandLine(args, null, inst));
        }

        [Test]
        public void AdjustCommandLine_ConfigPathBlocked_FallsBackToPlainLaunch()
        {
            // A directory squatting on the StarMapConfig.json path makes the config
            // write throw. AdjustCommandLine runs before PlayGame's own error
            // handling, so it must swallow that and launch KSA.exe unchanged.
            WriteFakeStarMapExe();
            Directory.CreateDirectory(Path.Combine(tempDir, StarMapTestFolder, "StarMapConfig.json"));
            var args = new[] { "KSA.exe", "-single-instance" };

            string[] adjusted = Array.Empty<string>();
            Assert.DoesNotThrow(() =>
                adjusted = new KittenSpaceAgency().AdjustCommandLine(args, tempDir, fakeGameDir));

            CollectionAssert.AreEqual(args, adjusted);
        }

        [Test]
        public void ParseValue_ControlCharacterEscapes_Decoded()
        {
            Assert.AreEqual("a\tb", KittenSpaceAgency.ParseValue("\"a\\tb\""));
            Assert.AreEqual("a\nb", KittenSpaceAgency.ParseValue("\"a\\nb\""));
            Assert.AreEqual("a\rb", KittenSpaceAgency.ParseValue("\"a\\rb\""));
            Assert.AreEqual("a\bb", KittenSpaceAgency.ParseValue("\"a\\bb\""));
            Assert.AreEqual("a\fb", KittenSpaceAgency.ParseValue("\"a\\fb\""));
        }

        [Test]
        public void ParseValue_InvalidUnicodeEscape_KeptLiterally()
        {
            // \u not followed by four hex digits: the u is kept and parsing
            // continues, instead of corrupting or truncating the value.
            Assert.AreEqual("auZZZZb", KittenSpaceAgency.ParseValue("\"a\\uZZZZb\""));
        }

        [Test]
        public void ParseValue_UnknownEscape_KeepsFollowingCharacter()
        {
            Assert.AreEqual("aqb", KittenSpaceAgency.ParseValue("\"a\\qb\""));
        }

        [Test]
        public void SerializeManifest_ParseManifest_ControlCharactersRoundTrip()
        {
            // Control characters in an id must be escaped on write (a raw newline
            // would break the line-based format) and decoded on read.
            var original = new List<ManifestModEntry>
            {
                new ManifestModEntry { Id = "tab\there\nnewline\rreturn\bbell\ffeed",
                                       Enabled = true },
            };

            var reparsed = KittenSpaceAgency.ParseManifest(
                               KittenSpaceAgency.SerializeManifest(original));

            Assert.AreEqual(original[0].Id, reparsed.Single().Id);
        }

        [Test]
        public void SerializeManifest_ExtraLines_WrittenBack()
        {
            // Unknown keys parsed from the game's manifest must survive a rewrite.
            var entries = KittenSpaceAgency.ParseManifest(
                "[[mods]]\nid = \"Core\"\nenabled = true\nversion = \"1.0\"\n");

            var written = KittenSpaceAgency.SerializeManifest(entries);

            StringAssert.Contains("version = \"1.0\"", written);
        }

        [Test]
        public void UpdateManifestFile_UnwritableManifestPath_DoesNotThrow()
        {
            // A file squatting on the manifest's parent folder path makes the
            // directory creation fail; the sync is best-effort and must swallow it.
            var blocker = Path.Combine(tempDir, "blocked");
            File.WriteAllText(blocker, "");

            Assert.DoesNotThrow(() =>
                KittenSpaceAgency.UpdateManifestFile(
                    new HashSet<string> { "SomeMod" },
                    Path.Combine(blocker, "manifest.toml"),
                    Path.Combine(tempDir, "managed-mods.json")));
        }

        [Test]
        public void ProcessLoadedModsBeforeGameStart_InstalledModule_WritesManifest()
        {
            // The end-to-end path from installed modules to the manifest: the mod
            // folder name is derived from the installed files and enabled.
            var inst   = MakeInstance();
            var module = CkanModule.FromJson(
                @"{
                    ""spec_version"": 1,
                    ""identifier"":   ""TestMod"",
                    ""name"":         ""TestMod"",
                    ""abstract"":     ""A test module"",
                    ""author"":       ""tester"",
                    ""license"":      ""MIT"",
                    ""version"":      ""1.0"",
                    ""download"":     ""https://example.com/TestMod.zip""
                }");
            var installed = new InstalledModule(inst, module,
                                                new[] { "mods/TestMod/mod.toml",
                                                        "mods/TestMod/TestMod.dll" },
                                                false);
            var manifestFile    = Path.Combine(tempDir, "manifest.toml");
            var managedModsFile = Path.Combine(tempDir, "managed-mods.json");

            new KittenSpaceAgency().ProcessLoadedModsBeforeGameStart(
                new[] { installed }, manifestFile, managedModsFile);

            var entries = KittenSpaceAgency.ParseManifest(File.ReadAllText(manifestFile));
            Assert.IsTrue(entries.Single(e => e.Id == "TestMod").Enabled);
        }

        [Test]
        public void ProcessLoadedModsBeforeGameStart_EnumerationThrows_DoesNotThrow()
        {
            // Keeping the manifest in sync is a best-effort side effect of an
            // otherwise successful operation, so a failure while deriving the mod
            // folders must never surface to the caller.
            var manifestFile = Path.Combine(tempDir, "manifest.toml");

            Assert.DoesNotThrow(() =>
                new KittenSpaceAgency().ProcessLoadedModsBeforeGameStart(
                    new ThrowsOnSecondEnumeration(),
                    manifestFile,
                    Path.Combine(tempDir, "managed-mods.json")));

            Assert.IsFalse(File.Exists(manifestFile));
        }

        // Enumerates fine once (the logging loop) and throws on the second pass
        // (deriving the mod folders), simulating a failure mid-sync.
        private sealed class ThrowsOnSecondEnumeration : IReadOnlyCollection<InstalledModule>
        {
            private int enumerations = 0;

            public int Count => 0;

            public IEnumerator<InstalledModule> GetEnumerator()
                => ++enumerations > 1
                    ? throw new IOException("simulated failure")
                    : Enumerable.Empty<InstalledModule>().GetEnumerator();

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }
    }
}
