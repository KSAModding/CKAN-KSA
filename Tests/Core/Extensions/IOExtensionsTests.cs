using System.IO;

using NUnit.Framework;

using CKAN.IO;
using CKAN.Extensions;

using Tests.Data;

namespace Tests.Core.Extensions
{
    [TestFixture]
    public sealed class IOExtensionsTests
    {
        [Test]
        public void GetDrive_NotYetCreatedDirectory_ResolvesParentDrive()
        {
            // Arrange
            using (var dir = new TemporaryDirectory())
            {
                var missing = new DirectoryInfo(Path.Combine(dir, "does", "not", "exist"));

                // Act
                var drive = missing.GetDrive();

                // Assert: same drive as the existing parent, and its properties
                // are usable (on Linux they throw for a nonexistent path)
                Assert.IsNotNull(drive);
                Assert.AreEqual(dir.Directory.GetDrive()?.Name, drive?.Name);
                Assert.GreaterOrEqual(drive?.AvailableFreeSpace, 0);
            }
        }

        [Test]
        public void CheckFreeSpace_NotYetCreatedDirectory_DoesNotThrow()
        {
            // Arrange: the mod directory may not exist before the first install
            using (var dir = new TemporaryDirectory())
            {
                var missing = new DirectoryInfo(Path.Combine(dir, "GameData", "Mods"));

                // Act / Assert
                Assert.DoesNotThrow(() => CKANPathUtils.CheckFreeSpace(missing, 1, "no space"));
            }
        }
    }
}
