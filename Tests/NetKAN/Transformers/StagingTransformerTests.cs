using System.Linq;

using Newtonsoft.Json.Linq;
using NUnit.Framework;

using CKAN.NetKAN.Model;
using CKAN.NetKAN.Transformers;
using CKAN.Games.KerbalSpaceProgram;
using CKAN.Games.KittenSpaceAgency;
using CKAN.Versioning;

namespace Tests.NetKAN.Transformers
{
    [TestFixture]
    public sealed class StagingTransformerTests
    {
        [Test]
        public void Transform_LatestGameVersion_Unstaged()
        {
            // Arrange
            var sut      = new StagingTransformer(new KerbalSpaceProgram());
            var opts     = new TransformOptions(1, null, null, null, false, null);
            var metadata = new Metadata(new JObject()
            {
                { "ksp_version", "1.12.5" },
            });

            // Act
            sut.Transform(metadata, opts).First();

            // Assert
            Assert.IsFalse(opts.Staged);
            CollectionAssert.IsEmpty(opts.StagingReasons);
        }

        [Test]
        public void Transform_OldGameVersion_Staged()
        {
            // Arrange
            var sut      = new StagingTransformer(new KerbalSpaceProgram());
            var opts     = new TransformOptions(1, null, null, null, false, null);
            var metadata = new Metadata(new JObject()
            {
                { "ksp_version", "1.12.4" },
            });

            // Act
            sut.Transform(metadata, opts).First();

            // Assert
            Assert.IsTrue(opts.Staged);
            CollectionAssert.IsNotEmpty(opts.StagingReasons);
        }

        [Test]
        public void Transform_KsaRawCurrentVersion_Unstaged()
        {
            // Arrange: a mod author hard-codes the raw current game version, i.e. with a
            // real (non-zero) build counter. It must not be staged just because the
            // build map normalizes the build counter to 0.
            var game    = new KittenSpaceAgency();
            var current = game.KnownVersions.Max()!;
            var raw     = new GameVersion(current.Major, current.Minor, 7, current.Build);
            var sut      = new StagingTransformer(game);
            var opts     = new TransformOptions(1, null, null, null, false, null);
            var metadata = new Metadata(new JObject()
            {
                { "ksp_version", raw.ToString() },
            });

            // Act
            sut.Transform(metadata, opts).First();

            // Assert
            Assert.IsFalse(opts.Staged);
            CollectionAssert.IsEmpty(opts.StagingReasons);
        }

        [Test]
        public void Transform_KsaOldVersion_Staged()
        {
            // A genuinely old revision (build counter 99 is irrelevant) is still flagged
            // for review.
            var sut      = new StagingTransformer(new KittenSpaceAgency());
            var opts     = new TransformOptions(1, null, null, null, false, null);
            var metadata = new Metadata(new JObject()
            {
                { "ksp_version", "2025.8.99.2091" },
            });

            sut.Transform(metadata, opts).First();

            Assert.IsTrue(opts.Staged);
            CollectionAssert.IsNotEmpty(opts.StagingReasons);
        }
    }
}
