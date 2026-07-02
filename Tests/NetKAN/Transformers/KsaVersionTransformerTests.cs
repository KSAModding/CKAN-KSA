using System.Linq;

using Newtonsoft.Json.Linq;
using NUnit.Framework;

using CKAN.NetKAN.Model;
using CKAN.NetKAN.Transformers;
using CKAN.Games.KittenSpaceAgency;
using CKAN.Games.KerbalSpaceProgram2;

namespace Tests.NetKAN.Transformers
{
    [TestFixture]
    public sealed class KsaVersionTransformerTests
    {
        private readonly TransformOptions opts =
            new TransformOptions(1, null, null, null, false, null);

        [Test]
        public void Transform_KsaFourPartVersion_NormalizesBuildCounter()
        {
            // Arrange: a mod author declares the raw game version.
            var sut      = new KsaVersionTransformer(new KittenSpaceAgency());
            var metadata = new Metadata(new JObject()
            {
                { "ksp_version", "2026.6.9.4750" },
            });

            // Act
            var json = sut.Transform(metadata, opts).First().Json();

            // Assert: the build counter (3rd component) is pinned to 0.
            Assert.AreEqual("2026.6.0.4750", (string?)json["ksp_version"]);
        }

        [Test]
        public void Transform_KsaMinMaxVersions_NormalizeBuildCounter()
        {
            var sut      = new KsaVersionTransformer(new KittenSpaceAgency());
            var metadata = new Metadata(new JObject()
            {
                { "ksp_version_min", "2026.6.5.4700" },
                { "ksp_version_max", "2026.6.9.4800" },
            });

            var json = sut.Transform(metadata, opts).First().Json();

            Assert.AreEqual("2026.6.0.4700", (string?)json["ksp_version_min"]);
            Assert.AreEqual("2026.6.0.4800", (string?)json["ksp_version_max"]);
        }

        [Test]
        public void Transform_NonKsaGame_LeavesVersionUnchanged()
        {
            // A 4-part version on another game must not be touched.
            var sut      = new KsaVersionTransformer(new KerbalSpaceProgram2());
            var metadata = new Metadata(new JObject()
            {
                { "ksp_version", "1.2.3.4" },
            });

            var json = sut.Transform(metadata, opts).First().Json();

            Assert.AreEqual("1.2.3.4", (string?)json["ksp_version"]);
        }

        [Test]
        public void Transform_TwoPartVersion_LeftUnchanged()
        {
            // year.month has no build counter to strip.
            var sut      = new KsaVersionTransformer(new KittenSpaceAgency());
            var metadata = new Metadata(new JObject()
            {
                { "ksp_version", "2026.6" },
            });

            var json = sut.Transform(metadata, opts).First().Json();

            Assert.AreEqual("2026.6", (string?)json["ksp_version"]);
        }
    }
}
