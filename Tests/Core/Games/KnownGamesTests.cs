using System.Linq;

using NUnit.Framework;

using CKAN.Games;
using CKAN.Games.KerbalSpaceProgram;
using CKAN.Games.KerbalSpaceProgram2;
using CKAN.Games.KittenSpaceAgency;

namespace Tests.Core.Games
{
    [TestFixture]
    public sealed class KnownGamesTests
    {
        [Test]
        public void AllShortGameNames_Called_ReturnsAllThreeGames()
        {
            // Arrange / Act
            var names = KnownGames.AllGameShortNames().ToArray();

            // Act / Assert
            CollectionAssert.AreEquivalent(new string[] { "KSP", "KSP2", "KSA" },
                                           names);
        }

        [Test]
        public void GameByShortName_EachGame_CorrectType()
        {
            // Arrange / Act
            var ksp  = KnownGames.GameByShortName("KSP");
            var ksp2 = KnownGames.GameByShortName("KSP2");
            var ksa  = KnownGames.GameByShortName("KSA");
            var ksp3 = KnownGames.GameByShortName("KSP3");

            // Act/ Assert
            Assert.IsTrue(ksp  is KerbalSpaceProgram);
            Assert.IsTrue(ksp2 is KerbalSpaceProgram2);
            Assert.IsTrue(ksa  is KittenSpaceAgency);
            Assert.IsNull(ksp3);
        }

        [Test]
        public void AllInstanceAnchorFiles()
        {
            var ksp1 = new KerbalSpaceProgram();
            var ksp2 = new KerbalSpaceProgram2();
            var ksa  = new KittenSpaceAgency();

            CollectionAssert.AreEquivalent(
                ksp1.InstanceAnchorFiles
                    .Concat(ksp2.InstanceAnchorFiles)
                    .Concat(ksa.InstanceAnchorFiles)
                    .Distinct(),
                KnownGames.AllInstanceAnchorFiles);
        }
    }
}
