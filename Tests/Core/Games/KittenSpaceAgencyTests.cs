using NUnit.Framework;

using CKAN.Versioning;
using CKAN.Games.KittenSpaceAgency;

namespace Tests.Core.Games
{
    [TestFixture]
    public class KittenSpaceAgencyTests
    {
        [Test]
        public void EmbeddedGameVersions_Called_ContainsKnownBuild()
        {
            // Arrange
            var game = new KittenSpaceAgency();

            // Act
            var versions = game.EmbeddedGameVersions;

            // Assert
            CollectionAssert.IsNotEmpty(versions);
            CollectionAssert.Contains(versions, GameVersion.Parse("2025.11.6.2829"));
        }
    }
}
