#if NETFRAMEWORK || WINDOWS

#if NET5_0_OR_GREATER
using System.Runtime.Versioning;
#endif

using NUnit.Framework;

using CKAN;
using CKAN.GUI;
using Tests.Core.Configuration;
using Tests.Data;

namespace Tests.GUI
{
    #if NET5_0_OR_GREATER
    [SupportedOSPlatform("windows")]
    #endif
    [TestFixture]
    public class ModChangeTests
    {
        [Test]
        public void AllProperties_InstallingMM_Correct()
        {
            // Arrange
            var user = new NullUser();
            using (var inst    = new DisposableKSP())
            using (var config  = new FakeConfiguration(inst.KSP, inst.KSP.Name))
            using (var manager = new GameInstanceManager(user, config))
            {

                // Act
                var sut = new ModChange(TestData.ModuleManagerModule(),
                                        GUIModChangeType.Install,
                                        config);

                // Assert
                Assert.IsTrue(sut.IsUserRequested);
                Assert.AreEqual("Requested by user", sut.Description);
                Assert.AreEqual("Install ModuleManager 2.5.1 (Requested by user)", sut.ToString());
                Assert.AreEqual(new ModChange(TestData.ModuleManagerModule(),
                                              GUIModChangeType.Install,
                                              config),
                                sut);
                Assert.AreNotEqual(new ModChange(TestData.ModuleManagerModule(),
                                                 GUIModChangeType.Update,
                                                 config),
                                   sut);
                Assert.AreNotEqual(new ModChange(TestData.BurnControllerModule(),
                                                 GUIModChangeType.Install,
                                                 config),
                                   sut);
            }
        }

        [Test]
        public void AllProperties_Upgrade_Correct()
        {
            // Arrange
            var user = new NullUser();
            using (var inst    = new DisposableKSP())
            using (var config  = new FakeConfiguration(inst.KSP, inst.KSP.Name))
            using (var manager = new GameInstanceManager(user, config))
            {

                // Act
                var sut = new ModUpgrade(TestData.ModuleManagerModule(),
                                         TestData.ModuleManagerModule(),
                                         config);

                // Assert
                Assert.IsTrue(sut.IsUserRequested);
                Assert.AreEqual("Update to version 2.5.1.", sut.Description);
                Assert.AreEqual("Update ModuleManager 2.5.1 (Update to version 2.5.1.)", sut.ToString());
                Assert.AreEqual(new ModUpgrade(TestData.ModuleManagerModule(),
                                               TestData.ModuleManagerModule(),
                                               config),
                                sut);
                Assert.AreNotEqual(new ModChange(TestData.BurnControllerModule(),
                                                 GUIModChangeType.Update,
                                                 config),
                                   sut);
                Assert.AreNotEqual(new ModChange(TestData.ModuleManagerModule(),
                                                 GUIModChangeType.Install,
                                                 config),
                                   sut);
            }
        }

        [TestCase(false, false, false, "Re-install (missing folders or files)"),
         TestCase(false, true,  false, "Re-install (metadata changed)"),
         TestCase(false, true,  true,  "Re-install (metadata changed)"),
         TestCase(true,  false, false, "Re-install (user requested)"),
         TestCase(true,  true,  false, "Re-install (user requested)")]
        public void AllProperties_Reinstall_Correct(bool   userReinstall,
                                                    bool   metadataChanged,
                                                    bool   reinstallFiles,
                                                    string reason)
        {
            // Arrange
            var user = new NullUser();
            using (var inst    = new DisposableKSP())
            using (var config  = new FakeConfiguration(inst.KSP, inst.KSP.Name))
            using (var manager = new GameInstanceManager(user, config))
            {

                // Act
                var sut = new ModReinstall(TestData.ModuleManagerModule(),
                                           userReinstall, metadataChanged, reinstallFiles,
                                           config);

                // Assert
                Assert.IsTrue(sut.IsUserRequested);
                Assert.AreEqual(!userReinstall && metadataChanged && !reinstallFiles,
                                sut.SkipReinstallingFiles);
                Assert.AreEqual(reason, sut.Description);
                Assert.AreEqual($"Update ModuleManager 2.5.1 ({reason})",
                                sut.ToString());
                Assert.AreEqual(new ModReinstall(TestData.ModuleManagerModule(),
                                                 userReinstall, metadataChanged, false,
                                                 config),
                                sut);
                Assert.AreNotEqual(new ModReinstall(TestData.BurnControllerModule(),
                                                    userReinstall, metadataChanged, false,
                                                    config),
                                   sut);
            }
        }
    }
}

#endif
