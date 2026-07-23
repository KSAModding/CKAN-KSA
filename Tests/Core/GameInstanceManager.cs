using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;

using Moq;
using NUnit.Framework;

using CKAN;
using CKAN.Configuration;
using CKAN.DLC;
using CKAN.IO;
using CKAN.Versioning;
using CKAN.Games;
using CKAN.Games.KerbalSpaceProgram;
using CKAN.Games.KerbalSpaceProgram.DLC;
using CKAN.Games.KittenSpaceAgency;

using Tests.Core.Configuration;
using Tests.Data;

namespace Tests.Core
{
    [TestFixture] public class GameInstanceManagerTests
    {
        private const string nameInReg = "testing";
        private DisposableKSP?       tidy;
        private FakeConfiguration?   cfg;
        private GameInstanceManager? manager;

        [SetUp]
        public void SetUp()
        {
            tidy = new DisposableKSP();
            cfg = GetTestCfg(nameInReg);
            manager = new GameInstanceManager(new NullUser(), cfg);
        }

        [TearDown]
        public void TearDown()
        {
            manager?.Dispose();
            tidy?.Dispose();
            cfg?.Dispose();
        }

        [Test]
        public void HasInstance_ReturnsFalseIfNoInstanceByThatName()
        {
            const string anyNameNotInReg = "Games";
            Assert.That(manager?.HasInstance(anyNameNotInReg), Is.EqualTo(false));
        }

        [Test]
        public void HasInstance_ReturnsTrueIfInstanceByThatName()
        {
            Assert.That(manager?.HasInstance(nameInReg), Is.EqualTo(true));
        }

        [Test]
        public void SetAutoStart_ValidName_SetsAutoStart()
        {
            Assert.That(manager?.Configuration.AutoStartInstance, Is.EqualTo(null));

            manager?.SetAutoStart(nameInReg);
            Assert.That(manager?.Configuration.AutoStartInstance, Is.EqualTo(nameInReg));
        }

        [Test]
        public void SetAutoStart_InvalidName_DoesNotChangeAutoStart()
        {
            manager?.SetAutoStart(nameInReg);
            Assert.Throws<InvalidGameInstanceKraken>(() => manager?.SetAutoStart("invalid"));
            Assert.That(manager?.Configuration.AutoStartInstance, Is.EqualTo(nameInReg));
        }

        [Test]
        public void RemoveInstance_HasInstance_ReturnsFalse()
        {
            manager?.RemoveInstance(nameInReg);
            Assert.False(manager?.HasInstance(nameInReg));
        }

        [Test]
        public void RenameInstance_NewName_Works()
        {
            const string newname = "newname";
            manager!.RenameInstance(nameInReg, newname);
            Assert.False(manager.HasInstance(nameInReg));
            Assert.True(manager.HasInstance(newname));
        }

        [Test]
        public void RenameInstance_SameName_Throws()
        {
            var fakeName = "fake";
            using (var tidy2 = new DisposableKSP(fakeName, tidy!.KSP.Game))
            {
                manager!.AddInstance(tidy2.KSP);
                Assert.Throws<InstanceNameTakenKraken>(() =>
                {
                    manager!.RenameInstance(nameInReg, fakeName);
                });
            }
        }

        [Test]
        public void ClearAutoStart_UpdatesValueInWin32Reg()
        {
            Assert.That(cfg?.AutoStartInstance, Is.Null.Or.Empty);
        }

        [Test]
        public void GetNextValidInstanceName_ManagerDoesNotHaveResult()
        {
            var name = manager?.GetNextValidInstanceName(nameInReg)!;
            Assert.That(manager?.HasInstance(name), Is.False);
        }

        [Test]
        public void AddInstance_ManagerHasInstance()
        {
            using (var tidy2 = new DisposableKSP())
            {
                const string newInstance = "tidy2";
                tidy2.KSP.Name = newInstance;
                Assert.IsFalse(manager?.HasInstance(newInstance));
                manager?.AddInstance(tidy2.KSP);
                Assert.IsTrue(manager?.HasInstance(newInstance));
            }
        }

        // CloneInstance

        [Test]
        public void CloneInstance_BadInstance_ThrowsNotKSPDirKraken()
        {
            string badName = "badInstance";
            using (var tempdir = new TemporaryDirectory())
            {
                var badKSP = new GameInstance(new KerbalSpaceProgram(),
                                              TestData.bad_ksp_dirs().First(),
                                              "badDir");

                Assert.Throws<NotGameDirKraken>(() =>
                    manager?.CloneInstance(badKSP, badName, tempdir));
                Assert.IsFalse(manager?.HasInstance(badName));
            }
        }

        [Test]
        public void CloneInstance_ToNotEmptyFolder_ThrowsPathErrorKraken()
        {
            string instanceName = "newInstance";
            using (var tempdir = new TemporaryDirectory())
            {
                File.Create(Path.Combine(tempdir, "shouldntbehere.txt")).Close();

                Assert.Throws<PathErrorKraken>(() =>
                    manager?.CloneInstance(tidy!.KSP, instanceName, tempdir));
                Assert.IsFalse(manager?.HasInstance(instanceName));
            }
        }

        [Test]
        public void CloneInstance_GoodInstance_ManagerHasValidInstance()
        {
            string instanceName = "newInstance";
            using (var tempdir = new TemporaryDirectory())
            {
                manager?.CloneInstance(tidy!.KSP, instanceName, tempdir);
                Assert.IsTrue(manager?.HasInstance(instanceName));
            }
        }

        [Test]
        public void CloneInstance_WithCKANDirFiles_OmittedAndCopied()
        {
            // Arrange
            var inst = tidy!.KSP;
            string instanceName = "newInstance";
            using (var tempdir = new TemporaryDirectory())
            {
                var origReg = Path.Combine(inst.CkanDir, "registry.json");
                var origFI  = new FileInfo(origReg);
                File.Copy(TestData.TestRegistry(), origReg);
                File.WriteAllText(Path.Combine(inst.CkanDir, "registry.locked"), "1234");
                File.WriteAllText(Path.Combine(inst.CkanDir, "playtime.json"),   "{}");

                // Act
                manager!.CloneInstance(inst, instanceName, tempdir);
                var clone    = manager.Instances[instanceName];
                var cloneReg = Path.Combine(clone.CkanDir, "registry.json");

                // Assert
                FileAssert.DoesNotExist(Path.Combine(clone.CkanDir, "registry.locked"));
                FileAssert.DoesNotExist(Path.Combine(clone.CkanDir, "playtime.json"));
                FileAssert.Exists(cloneReg);

                // Act
                using (var regMgr = RegistryManager.Instance(clone, new RepositoryDataManager()))
                {
                    regMgr.Save();

                    // Assert
                    FileAssert.AreEqual(new FileInfo(TestData.TestRegistry()), origFI,
                                        "Original registry should be unchanged");
                    FileAssert.AreNotEqual(origFI, new FileInfo(cloneReg),
                                           "Updating the cloned registry should not affect the original");
                }
            }
        }

        [Test]
        public void CloneInstance_TargetInsideSource_Throws()
        {
            // Arrange
            var inst = tidy!.KSP;
            string instanceName = "newInstance";
            var target = Path.Combine(inst.GameDir, "subclone");

            // Act / Assert
            var exc = Assert.Throws<PathErrorKraken>(() =>
            {
                manager!.CloneInstance(inst, instanceName, target);
            });
        }

        // FakeInstance

        [Test]
        public void FakeInstance_InvalidVersion_ThrowsBadGameVersionKraken()
        {
            // Arrange
            var name = "testname";
            var version = GameVersion.Parse("1.1.99");

            using (var tempdir = new TemporaryDirectory())
            {
                // Act / Assert
                Assert.Throws<BadGameVersionKraken>(() =>
                    manager?.FakeInstance(new KerbalSpaceProgram(), name,
                                          tempdir, version));
                Assert.IsFalse(manager?.HasInstance(name));
            }
        }

        [TestCase("1.4.0"),
         TestCase("1.6.1")]
        public void FakeInstance_DlcsWithWrongBaseVersion_ThrowsWrongGameVersionKraken(string baseVersion)
        {
            // Arrange
            var name = "testname";
            var mhVersion = GameVersion.Parse("1.1.0");
            var bgVersion = GameVersion.Parse("1.0.0");
            var version = GameVersion.Parse(baseVersion);
            var dlcs = new Dictionary<IDlcDetector, GameVersion>()
            {
                { new MakingHistoryDlcDetector(),  mhVersion },
                { new BreakingGroundDlcDetector(), bgVersion },
            };
            using (var tempdir = new TemporaryDirectory())
            {
                // Act / Assert
                Assert.Throws<WrongGameVersionKraken>(() =>
                    manager?.FakeInstance(new KerbalSpaceProgram(), name,
                                          tempdir, version, dlcs));
                Assert.IsFalse(manager?.HasInstance(name));
            }
        }

        [Test]
        public void FakeInstance_InNotEmptyFolder_ThrowsBadInstallLocationKraken()
        {
            // Arrange
            var name = "testname";
            var version = GameVersion.Parse("1.5.1");

            using (var tempdir = new TemporaryDirectory())
            {
                File.Create(Path.Combine(tempdir,
                                         "shouldntbehere.txt"))
                    .Close();

                // Act / Assert
                Assert.Throws<BadInstallLocationKraken>(() =>
                    manager?.FakeInstance(new KerbalSpaceProgram(), name,
                                          tempdir, version));
                Assert.IsFalse(manager?.HasInstance(name));
            }
        }

        [Test]
        public void FakeInstance_ValidArgumentsWithDLCs_ManagerHasValidInstance()
        {
            // Arrange
            string name = "testname";
            var mhVersion = GameVersion.Parse("1.1.0");
            var unmanagedMhVersion = new UnmanagedModuleVersion(mhVersion.ToString());
            var bgVersion = GameVersion.Parse("1.0.0");
            var unmanagedBgVersion = new UnmanagedModuleVersion(bgVersion.ToString());
            var version = GameVersion.Parse("1.7.1.2539");
            var mhDetector = new MakingHistoryDlcDetector();
            var bgDetector = new BreakingGroundDlcDetector();

            var dlcs = new Dictionary<IDlcDetector, GameVersion>()
            {
                { mhDetector, mhVersion },
                { bgDetector, bgVersion },
            };

            using (var tempdir = new TemporaryDirectory())
            {
                // Act
                var newKSP = manager!.FakeInstance(new KerbalSpaceProgram(), name,
                                                   tempdir, version, dlcs);

                Assert.IsTrue(manager?.HasInstance(name));
                Assert.IsTrue(mhDetector.IsInstalled(newKSP, out string? _, out UnmanagedModuleVersion? detectedMhVersion));
                Assert.AreEqual(unmanagedMhVersion, detectedMhVersion);
                Assert.IsTrue(bgDetector.IsInstalled(newKSP, out string? _, out UnmanagedModuleVersion? detectedBgVersion));
                Assert.AreEqual(unmanagedBgVersion, detectedBgVersion);
                FileAssert.Exists(Path.Combine(tempdir, "buildID.txt"));
                FileAssert.Exists(Path.Combine(tempdir, "buildID64.txt"));
            }
        }

        // The raw in-game string (with the real build counter) and the normalized
        // form CKAN stores must both produce a valid instance registered with the
        // normalized version. Like the 1.7.1.2539 KSP case above, the version must
        // be in the game's build map: 2026.6.9.4750 is in the embedded map, and the
        // remote map (which may be cached on the machine) is append-only, so it
        // stays known wherever the tests run.
        [TestCase("2026.6.9.4750"),
         TestCase("2026.6.0.4750")]
        public void FakeInstance_Ksa_RegistersValidInstanceWithNormalizedVersion(string versionString)
        {
            // Arrange
            var name    = "testname";
            var version = GameVersion.Parse(versionString);

            using (var tempdir = new TemporaryDirectory())
            {
                // Act
                var newKsa = manager!.FakeInstance(new KittenSpaceAgency(), name,
                                                   tempdir, version);

                // Assert
                Assert.IsTrue(manager?.HasInstance(name));
                Assert.IsTrue(newKsa.Valid);
                Assert.AreEqual(GameVersion.Parse("2026.6.0.4750"), newKsa.Version(),
                                "A fake KSA instance must detect the normalized version");
                // The version file the game-version provider reads, named like the
                // real game's v<year>.<month>.X.<revision>.json files
                FileAssert.Exists(Path.Combine(tempdir, "Content", "Versions",
                                               "v2026.6.X.4750.json"));
                DirectoryAssert.DoesNotExist(Path.Combine(tempdir, "mods"));
            }
        }

        // An unknown year.month, and an unknown revision within a known month: the
        // revision (4th component) is KSA's real version ordinal, so it must be
        // validated too, raw or normalized.
        [TestCase("2199.1.0.999999"),
         TestCase("2026.6.0.999999"),
         TestCase("2026.6.9.999999")]
        public void FakeInstance_KsaUnknownVersion_ThrowsBadGameVersionKraken(string versionString)
        {
            // Arrange
            var name = "testname";
            var version = GameVersion.Parse(versionString);

            using (var tempdir = new TemporaryDirectory())
            {
                // Act / Assert
                Assert.Throws<BadGameVersionKraken>(() =>
                    manager?.FakeInstance(new KittenSpaceAgency(), name,
                                          tempdir, version));
                Assert.IsFalse(manager?.HasInstance(name));
            }
        }

        // GetPreferredInstance

        [Test]
        public void GetPreferredInstance_WithAutoStart_ReturnsAutoStart()
        {
            Assert.That(manager?.GetPreferredInstance(),Is.EqualTo(tidy?.KSP));
        }

        [Test]
        public void GetPreferredInstance_WithEmptyAutoStartAndMultipleInstances_ReturnsNull()
        {
            using (var tidy2 = new DisposableKSP())
            {
                cfg!.Instances.Add(new Tuple<string, string, string>("tidy2", tidy2.KSP.GameDir, "KSP"));
                // Make a new manager with the updated config
                var multiMgr = new GameInstanceManager(new NullUser(), cfg);
                multiMgr.ClearAutoStart();
                Assert.That(multiMgr.GetPreferredInstance(), Is.Null);
                multiMgr.Dispose();
            }
        }

        [Test]
        public void GetPreferredInstance_OnlyOneAvailable_ReturnsAvailable()
        {
            manager?.ClearAutoStart();
            Assert.That(manager?.GetPreferredInstance(), Is.EqualTo(tidy?.KSP));
        }

        [Test]
        public void SetCurrentInstance_NameNotInRepo_Throws()
        {
            Assert.Throws<InvalidGameInstanceKraken>(() => manager?.SetCurrentInstance("invalid"));
        }

        [Test] //37a33
        public void Ctor_InvalidAutoStart_DoesNotThrow()
        {
            using (var config = new FakeConfiguration(tidy!.KSP, "invalid"))
            {
                Assert.DoesNotThrow(() =>
                {
                    using (var mgr = new GameInstanceManager(new NullUser(), config))
                    {
                    }
                });
            }
        }

        [Test]
        public void SetCurrentInstanceByPath_WithInstance_Works()
        {
            // Act
            manager!.SetCurrentInstanceByPath(tidy!.KSP.GameDir);

            // Assert
            Assert.AreEqual(tidy!.KSP, manager.CurrentInstance);
        }

        [Test]
        public void InstanceAt_WithInstance_Found()
        {
            // Arrange
            var instFromMgr = manager!.InstanceAt(tidy!.KSP.GameDir);

            // Assert
            Assert.IsNotNull(instFromMgr);
        }

        [Test]
        public void IsGameInstanceDir_WithInstance_True()
        {
            // Arrange
            var di = new DirectoryInfo(tidy!.KSP.GameDir);

            // Act / Assert
            Assert.IsTrue(GameInstanceManager.IsGameInstanceDir(di));
        }


        [Test]
        public void FindAndRegisterDefaultInstances_WithMockedSteam_FindsInstances()
        {
            // Arrange
            using (var config = new FakeConfiguration(new List<Tuple<string, string, string>>(), null, null))
            using (var nonSteamGameDir = TemporaryDirectory.CopiedFromDir(TestData.good_ksp_dir()))
            using (var dir = new TemporarySteamDirectory(
                                 new (string acfFileName, int appId, string appName)[]
                                 {
                                     (acfFileName: "appmanifest_220200.acf",
                                      appId:       220200,
                                      appName:     "Kerbal Space Program"),
                                     (acfFileName: "appmanifest_954850.acf",
                                      appId:       954850,
                                      appName:     "Kerbal Space Program 2"),
                                 },
                                 new (string name, string absPath)[]
                                 {
                                     (name:    "Test Instance",
                                      absPath: nonSteamGameDir),
                                 }))
            {
                var steamLib = new SteamLibrary(dir);
                using (var mgr = new GameInstanceManager(new NullUser(), config, steamLib))
                {
                    foreach (var g in steamLib.Games.OfType<SteamGame>())
                    {
                        Utilities.CopyDirectory(TestData.good_ksp_dir(),
                                                g.GameDir!.FullName,
                                                Array.Empty<string>(),
                                                Array.Empty<string>(),
                                                Array.Empty<string>(),
                                                Array.Empty<string>());
                    }

                    // Act
                    mgr.FindAndRegisterDefaultInstances();

                    // Assert
                    CollectionAssert.AreEquivalent(new string[]
                                                   {
                                                       "Kerbal Space Program",
                                                       "Kerbal Space Program 2",
                                                       "Test Instance",
                                                   },
                                                   mgr.Instances.Keys);
                }
            }
        }

        [Test]
        public void Constructor_WithCachePathDefined_Creates()
        {
            // Arrange
            using (var dir = new TemporaryDirectory())
            {
                var cachePath = Path.Combine(dir, "cachetest");
                DirectoryAssert.DoesNotExist(cachePath);

                var configPath = Path.Combine(dir, "config.json");
                File.WriteAllText(configPath, "{}");
                var config = new JsonConfiguration(configPath)
                {
                    DownloadCacheDir = cachePath,
                };

                // Act
                using (var mgr = new GameInstanceManager(new NullUser(), config))
                {
                    // Assert
                    DirectoryAssert.Exists(cachePath, $"{cachePath} should exist");
                }
            }
        }

        // Shared external mod root (KSA-style games)

        [Test]
        public void AddInstance_SecondInstanceSharingModRoot_WarnsAndRegistersBoth()
        {
            // Arrange
            using (var dirA    = new TemporaryDirectory())
            using (var dirB    = new TemporaryDirectory())
            using (var modRoot = new TemporaryDirectory())
            using (var config  = new FakeConfiguration(new List<Tuple<string, string, string>>(),
                                                       null, null))
            {
                var user = new CapturingUser(false, q => true, (msg, objs) => 0);
                using (var mgr = new GameInstanceManager(user, config))
                {
                    var game  = SharedModRootGame(modRoot);
                    var instA = new GameInstance(game.Object, dirA, "sharedA");
                    var instB = new GameInstance(game.Object, dirB, "sharedB");

                    // Act
                    mgr.AddInstance(instA);
                    var errorsAfterFirst = user.RaisedErrors.Count;
                    mgr.AddInstance(instB);

                    // Assert
                    Assert.AreEqual(0, errorsAfterFirst,
                                    "The first instance of a shared-mod-root game should not warn");
                    Assert.AreEqual(1, user.RaisedErrors.Count,
                                    "A second instance sharing the mod root should warn");
                    // Pin the placeholder mapping: {0} new instance, {1} sharers, {2} game
                    var warning = user.RaisedErrors.Single();
                    StringAssert.StartsWith("\"sharedB\" shares", warning);
                    StringAssert.Contains("with: sharedA", warning);
                    StringAssert.Contains("FakeExternal stores mods", warning);
                    Assert.IsTrue(mgr.HasInstance("sharedA"));
                    Assert.IsTrue(mgr.HasInstance("sharedB"),
                                  "The warning must not block registration");
                    CollectionAssert.AreEquivalent(new[] { instA },
                                                   mgr.InstancesSharingModRoot(instB));
                }
            }
        }

        [Test]
        public void AddInstance_WithExplicitUser_WarnsThatUser()
        {
            // Arrange: the ConsoleUI passes its screen as the IUser
            using (var dirA    = new TemporaryDirectory())
            using (var dirB    = new TemporaryDirectory())
            using (var modRoot = new TemporaryDirectory())
            using (var config  = new FakeConfiguration(new List<Tuple<string, string, string>>(),
                                                       null, null))
            {
                var managerUser  = new CapturingUser(false, q => true, (msg, objs) => 0);
                var explicitUser = new CapturingUser(false, q => true, (msg, objs) => 0);
                using (var mgr = new GameInstanceManager(managerUser, config))
                {
                    var game = SharedModRootGame(modRoot);

                    // Act
                    mgr.AddInstance(new GameInstance(game.Object, dirA, "expA"));
                    mgr.AddInstance(new GameInstance(game.Object, dirB, "expB"), explicitUser);

                    // Assert
                    Assert.IsEmpty(managerUser.RaisedErrors);
                    Assert.AreEqual(1, explicitUser.RaisedErrors.Count);
                }
            }
        }

        [Test]
        public void AddInstance_ExternalInstancesWithDifferentModRoots_DoesNotWarn()
        {
            // Arrange
            using (var dirA     = new TemporaryDirectory())
            using (var dirB     = new TemporaryDirectory())
            using (var modRootA = new TemporaryDirectory())
            using (var modRootB = new TemporaryDirectory())
            using (var config   = new FakeConfiguration(new List<Tuple<string, string, string>>(),
                                                        null, null))
            {
                var user = new CapturingUser(false, q => true, (msg, objs) => 0);
                using (var mgr = new GameInstanceManager(user, config))
                {
                    var instA = new GameInstance(SharedModRootGame(modRootA).Object, dirA, "extA");
                    var instB = new GameInstance(SharedModRootGame(modRootB).Object, dirB, "extB");

                    // Act
                    mgr.AddInstance(instA);
                    mgr.AddInstance(instB);

                    // Assert
                    Assert.IsEmpty(user.RaisedErrors);
                    Assert.IsEmpty(mgr.InstancesSharingModRoot(instB));
                }
            }
        }

        [Test]
        public void AddInstance_SecondNonExternalInstance_DoesNotWarn()
        {
            // Arrange: the manager already knows the SetUp KSP instance
            using (var tidy2 = new DisposableKSP())
            {
                var user = new CapturingUser(false, q => true, (msg, objs) => 0);
                using (var mgr = new GameInstanceManager(user, cfg!))
                {
                    // Act
                    mgr.AddInstance(tidy2.KSP);

                    // Assert
                    Assert.IsEmpty(user.RaisedErrors);
                    Assert.IsEmpty(mgr.InstancesSharingModRoot(tidy2.KSP));
                }
            }
        }

        [Test]
        public void AddInstance_SecondKsaInstance_WarnsAndFindsSharer()
        {
            // Arrange
            using (var dirA   = new TemporaryDirectory())
            using (var dirB   = new TemporaryDirectory())
            using (var config = new FakeConfiguration(new List<Tuple<string, string, string>>(),
                                                      null, null))
            {
                var user = new CapturingUser(false, q => true, (msg, objs) => 0);
                using (var mgr = new GameInstanceManager(user, config))
                {
                    var instA = new GameInstance(new KittenSpaceAgency(), FakeKsaGameDir(dirA), "ksaA");
                    var instB = new GameInstance(new KittenSpaceAgency(), FakeKsaGameDir(dirB), "ksaB");

                    // Act
                    mgr.AddInstance(instA);
                    mgr.AddInstance(instB);

                    // Assert: every KSA instance shares the user's Documents mods folder
                    Assert.AreEqual(1, user.RaisedErrors.Count);
                    // Pin the placeholder mapping: {0} new instance, {1} sharers, {2} game
                    var warning = user.RaisedErrors.Single();
                    StringAssert.StartsWith("\"ksaB\" shares", warning);
                    StringAssert.Contains("with: ksaA", warning);
                    StringAssert.Contains("KSA stores mods", warning);
                    CollectionAssert.AreEquivalent(new[] { instA },
                                                   mgr.InstancesSharingModRoot(instB));
                }
            }
        }

        [Test]
        public void AddInstance_PathOverloadSecondSharer_DeclineDoesNotRegister()
        {
            // Arrange
            using (var dirA   = new TemporaryDirectory())
            using (var dirB   = new TemporaryDirectory())
            using (var config = new FakeConfiguration(new List<Tuple<string, string, string>>(),
                                                      null, null))
            {
                // Decline the shared-mod-folder confirmation
                var user = new CapturingUser(false, q => false, (msg, objs) => 0);
                using (var mgr = new GameInstanceManager(user, config))
                {
                    mgr.AddInstance(new GameInstance(new KittenSpaceAgency(), FakeKsaGameDir(dirA), "ksaA"));

                    // Act
                    var result = mgr.AddInstance(FakeKsaGameDir(dirB), "ksaB", user);

                    // Assert
                    Assert.IsNull(result);
                    Assert.IsFalse(mgr.HasInstance("ksaB"));
                    Assert.AreEqual(1, user.RaisedYesNoDialogQuestions.Count);
                    Assert.IsEmpty(user.RaisedErrors,
                                   "Declining should not also raise the warning");
                }
            }
        }

        [Test]
        public void AddInstance_PathOverloadSecondSharer_ConfirmRegistersWithoutExtraWarning()
        {
            // Arrange
            using (var dirA   = new TemporaryDirectory())
            using (var dirB   = new TemporaryDirectory())
            using (var config = new FakeConfiguration(new List<Tuple<string, string, string>>(),
                                                      null, null))
            {
                // Confirm the shared-mod-folder confirmation
                var user = new CapturingUser(false, q => true, (msg, objs) => 0);
                using (var mgr = new GameInstanceManager(user, config))
                {
                    mgr.AddInstance(new GameInstance(new KittenSpaceAgency(), FakeKsaGameDir(dirA), "ksaA"));

                    // Act
                    var result = mgr.AddInstance(FakeKsaGameDir(dirB), "ksaB", user);

                    // Assert
                    Assert.IsNotNull(result);
                    Assert.IsTrue(mgr.HasInstance("ksaB"));
                    var question = user.RaisedYesNoDialogQuestions.Single();
                    StringAssert.StartsWith("\"ksaB\" shares", question);
                    StringAssert.Contains("Add it anyway?", question);
                    Assert.IsEmpty(user.RaisedErrors,
                                   "Confirming should not warn a second time");
                }
            }
        }

        [Test]
        public void AddInstance_PathOverloadFirstInstance_DoesNotPrompt()
        {
            // Arrange
            using (var dirA   = new TemporaryDirectory())
            using (var config = new FakeConfiguration(new List<Tuple<string, string, string>>(),
                                                      null, null))
            {
                var user = new CapturingUser(false, q => false, (msg, objs) => 0);
                using (var mgr = new GameInstanceManager(user, config))
                {
                    // Act
                    var result = mgr.AddInstance(FakeKsaGameDir(dirA), "ksaA", user);

                    // Assert
                    Assert.IsNotNull(result);
                    Assert.IsTrue(mgr.HasInstance("ksaA"));
                    Assert.IsEmpty(user.RaisedYesNoDialogQuestions);
                    Assert.IsEmpty(user.RaisedErrors);
                }
            }
        }

        [Test]
        public void AddInstance_PathOverloadMultiGameFolderCancelled_ReturnsNull()
        {
            // Arrange: a folder that looks like both a KSP1 and a KSA install, so
            // DetermineGame has to ask which game it is; cancelling that dialog
            // must abort the add without registering anything.
            using (var dirA   = new TemporaryDirectory())
            using (var config = new FakeConfiguration(new List<Tuple<string, string, string>>(),
                                                      null, null))
            {
                var gameDir = FakeKsaGameDir(dirA);
                // KSP1 anchor files for every platform plus its GameData marker
                File.WriteAllText(Path.Combine(gameDir, "KSP_x64.exe"), "");
                File.WriteAllText(Path.Combine(gameDir, "buildID64.txt"), "");
                File.WriteAllText(Path.Combine(gameDir, "KSP.x86_64"), "");
                Directory.CreateDirectory(Path.Combine(gameDir, "GameData"));

                // Cancel the game selection dialog
                var user = new CapturingUser(false, q => true, (msg, objs) => -1);
                using (var mgr = new GameInstanceManager(user, config))
                {
                    // Act
                    var result = mgr.AddInstance(gameDir, "which", user);

                    // Assert
                    Assert.IsNull(result);
                    Assert.IsFalse(mgr.HasInstance("which"));
                    Assert.AreEqual(1, user.RaisedSelectionDialogs.Count);
                    Assert.IsEmpty(user.RaisedErrors);
                }
            }
        }

        [Test]
        public void AddInstance_PathOverloadStateDirNotCreatable_RaisesErrorAndDoesNotRegister()
        {
            // Arrange
            using (var dirA   = new TemporaryDirectory())
            using (var config = new FakeConfiguration(new List<Tuple<string, string, string>>(),
                                                      null, null))
            {
                var user = new CapturingUser(false, q => true, (msg, objs) => 0);
                using (var mgr = new GameInstanceManager(user, config))
                {
                    var gameDir = FakeKsaGameDir(dirA);
                    // A file squatting on the CKAN state folder path makes setting
                    // it up fail with an IOException (KSAModding/CKAN-KSA#46), which
                    // must surface the generic message naming the underlying cause,
                    // not the access-denied relocation guidance.
                    File.WriteAllText(Path.Combine(gameDir, "CKAN"), "");

                    // Act
                    var result = mgr.AddInstance(gameDir, "ksaA", user);

                    // Assert: nothing registered, one actionable error, no raw crash
                    Assert.IsNull(result);
                    Assert.IsFalse(mgr.HasInstance("ksaA"));
                    var error = user.RaisedErrors.Single();
                    StringAssert.StartsWith("CKAN could not set up its data folder", error);
                    StringAssert.Contains("Fix the reported problem", error);
                    // {0} is the resolved absolute state folder path
                    StringAssert.Contains(Path.Combine(Path.GetFullPath(gameDir), "CKAN")
                                              .Replace('\\', '/'),
                                          error.Replace('\\', '/'));
                    Assert.IsEmpty(user.RaisedYesNoDialogQuestions);
                }
            }
        }

        [Test]
        public void AddInstance_PathOverloadStateDirAccessDenied_RaisesWritabilityGuidance()
        {
            // Arrange
            using (var dirA   = new TemporaryDirectory())
            using (var config = new FakeConfiguration(new List<Tuple<string, string, string>>(),
                                                      null, null))
            {
                var user = new CapturingUser(false, q => true, (msg, objs) => 0);
                using (var mgr = new GameInstanceManager(user, config))
                {
                    var gameDir = FakeKsaGameDir(dirA);
                    // A directory squatting on the playtime.json path makes the
                    // constructor's File.ReadAllText throw UnauthorizedAccessException
                    // on every platform, pinning the access-denied branch (the actual
                    // Program Files scenario of KSAModding/CKAN-KSA#46) without ACL
                    // juggling.
                    Directory.CreateDirectory(Path.Combine(gameDir, "CKAN", "playtime.json"));

                    // Act
                    var result = mgr.AddInstance(gameDir, "ksaA", user);

                    // Assert: nothing registered, the full relocation guidance shown
                    Assert.IsNull(result);
                    Assert.IsFalse(mgr.HasInstance("ksaA"));
                    var error = user.RaisedErrors.Single();
                    StringAssert.StartsWith("CKAN could not create its data folder", error);
                    StringAssert.Contains("write access", error);
                    // Pin the placeholder mapping: {2} is the suggested folder
                    StringAssert.Contains(Path.Combine("Games", new DirectoryInfo(gameDir).Name)
                                              .Replace('\\', '/'),
                                          error.Replace('\\', '/'));
                    Assert.IsEmpty(user.RaisedYesNoDialogQuestions);
                }
            }
        }

        [Test]
        public void AddInstance_PathOverloadStateDirNotCreatable_KeepsExistingInstances()
        {
            // Arrange: pins the manager-level contract the ConsoleUI edit screen's
            // remove-then-re-add flow depends on: a failed add returns null instead
            // of throwing and leaves no residue, so the caller can re-register the
            // old instance under the same name
            using (var dirA   = new TemporaryDirectory())
            using (var dirB   = new TemporaryDirectory())
            using (var config = new FakeConfiguration(new List<Tuple<string, string, string>>(),
                                                      null, null))
            {
                var user = new CapturingUser(false, q => true, (msg, objs) => 0);
                using (var mgr = new GameInstanceManager(user, config))
                {
                    var instA = new GameInstance(new KittenSpaceAgency(), FakeKsaGameDir(dirA), "ksaA");
                    mgr.AddInstance(instA);
                    var gameDirB = FakeKsaGameDir(dirB);
                    File.WriteAllText(Path.Combine(gameDirB, "CKAN"), "");

                    // Act: edit flow removes the old instance, then re-adds at the new path
                    mgr.RemoveInstance("ksaA");
                    var result = mgr.AddInstance(gameDirB, "ksaA", user);
                    if (result == null)
                    {
                        mgr.AddInstance(instA);
                    }

                    // Assert: AddInstance returned null instead of throwing,
                    // so the caller could restore the old instance
                    Assert.IsNull(result);
                    Assert.IsTrue(mgr.HasInstance("ksaA"));
                    Assert.AreEqual(instA.GameDir, mgr.Instances["ksaA"].GameDir);
                }
            }
        }

        [Test]
        public void AddInstance_RestoreSharedRootInstance_DoesNotWarnAgain()
        {
            // Arrange: two instances sharing the external mod root, like the
            // ConsoleUI edit screen's restore after a declined or failed re-add
            using (var dirA    = new TemporaryDirectory())
            using (var dirB    = new TemporaryDirectory())
            using (var modRoot = new TemporaryDirectory())
            using (var config  = new FakeConfiguration(new List<Tuple<string, string, string>>(),
                                                       null, null))
            {
                var user = new CapturingUser(false, q => true, (msg, objs) => 0);
                using (var mgr = new GameInstanceManager(user, config))
                {
                    var game  = SharedModRootGame(modRoot);
                    var instA = new GameInstance(game.Object, dirA, "sharedA");
                    var instB = new GameInstance(game.Object, dirB, "sharedB");
                    mgr.AddInstance(instA);
                    mgr.AddInstance(instB);
                    mgr.RemoveInstance("sharedB");
                    var restoreUser = new CapturingUser(false, q => true, (msg, objs) => 0);

                    // Act: put the previously registered instance back
                    mgr.AddInstance(instB, restoreUser, warnSharedModRoot: false);

                    // Assert: no second warning for an instance the user already had
                    Assert.IsTrue(mgr.HasInstance("sharedB"));
                    Assert.IsEmpty(restoreUser.RaisedErrors);
                }
            }
        }

        [Test]
        public void SuggestedRelocationPath_PlainFolder_KeepsFolderName()
        {
            using (var dirA = new TemporaryDirectory())
            {
                var gameDir    = GameInstance.NormalizeGameDir(dirA);
                var suggestion = GameInstanceManager.SuggestedRelocationPath(
                                     new KittenSpaceAgency(), gameDir);
                StringAssert.EndsWith("Games/" + new DirectoryInfo(gameDir).Name,
                                      suggestion.Replace('\\', '/'));
            }
        }

        [Test]
        public void SuggestedRelocationPath_FilesystemRootInstall_FallsBackToGameName()
        {
            // A game folder at a filesystem root has no usable folder name, and
            // its rooted "name" would reset Path.Combine back to the root itself
            var root       = GameInstance.NormalizeGameDir(Path.GetPathRoot(Path.GetTempPath())!);
            var suggestion = GameInstanceManager.SuggestedRelocationPath(
                                 new KittenSpaceAgency(), root);
            StringAssert.EndsWith("Games/KSA", suggestion.Replace('\\', '/'));
            Assert.AreNotEqual(root, suggestion);
        }

        // A minimal fake game whose mod directory lives outside GameDir (like KSA),
        // valid enough for GameInstanceManager.AddInstance to accept its instances.
        private static Mock<IGame> SharedModRootGame(string externalModRoot)
        {
            var game = new Mock<IGame>();
            game.Setup(g => g.ShortName).Returns("FakeExternal");
            game.Setup(g => g.CompatibleVersionsFile).Returns("compatible_versions.json");
            game.Setup(g => g.GameInFolder(It.IsAny<DirectoryInfo>())).Returns(true);
            game.Setup(g => g.DetectVersion(It.IsAny<DirectoryInfo>()))
                .Returns(new GameVersion(1, 0, 0, 0));
            game.Setup(g => g.DefaultCompatibleVersions(It.IsAny<GameVersion>()))
                .Returns(Array.Empty<GameVersion>());
            game.Setup(g => g.StockFolders).Returns(Array.Empty<string>());
            game.Setup(g => g.PrimaryModDirectoryRelative).Returns("mods");
            game.Setup(g => g.ModDirectoryIsExternal).Returns(true);
            game.Setup(g => g.PrimaryModDirectory(It.IsAny<GameInstance>())).Returns(externalModRoot);
            return game;
        }

        // A folder that KittenSpaceAgency accepts as a valid game instance:
        // the KSA.exe anchor plus one Content/Versions build file.
        private static string FakeKsaGameDir(string dir)
        {
            File.WriteAllText(Path.Combine(dir, "KSA.exe"), "");
            var versionsDir = Path.Combine(dir, "Content", "Versions");
            Directory.CreateDirectory(versionsDir);
            File.WriteAllText(Path.Combine(versionsDir, "v2026.6.X.4750.json"),
                              "{ \"build\": \"2026.6.9.4750\" }");
            return dir;
        }

        private FakeConfiguration GetTestCfg(string name)
            => new FakeConfiguration(
                   new List<Tuple<string, string, string>>
                   {
                       new Tuple<string, string, string>(name,
                                                         tidy!.KSP.GameDir,
                                                         tidy!.KSP.Game.ShortName)
                   },
                   null, null);
    }
}
