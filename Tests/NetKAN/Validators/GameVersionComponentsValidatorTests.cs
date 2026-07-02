using Newtonsoft.Json.Linq;
using NUnit.Framework;

using CKAN.NetKAN.Model;
using CKAN.NetKAN.Validators;
using CKAN.Games.KittenSpaceAgency;
using CKAN.Games.KerbalSpaceProgram2;

namespace Tests.NetKAN.Validators
{
    [TestFixture]
    public sealed class GameVersionComponentsValidatorTests
    {
        [Test,
            TestCase("2026.6.0.4750"),   // normalized 4-part revision range
            TestCase("2026.6.0"),        // whole month, build counter 0
            TestCase("2026.6"),          // year.month
            TestCase("2026"),
            TestCase("any"),
        ]
        public void Validate_KsaBuildCounterZeroOrAbsent_DoesNotThrow(string kspVersion)
        {
            Assert.DoesNotThrow(() => ValidateKsa(kspVersion));
        }

        [Test,
            TestCase("2026.6.9.4750"),   // raw 4-part build counter 9
            TestCase("2026.6.9"),        // raw 3-part build counter 9
        ]
        public void Validate_KsaNonZeroBuildCounter_Throws(string kspVersion)
        {
            Assert.Throws<CKAN.Kraken>(() => ValidateKsa(kspVersion));
        }

        [Test]
        public void Validate_KsaMinMaxNonZeroBuildCounter_Throws()
        {
            var metadata = new Metadata(new JObject()
            {
                { "ksp_version_min", "2026.6.0.4700" },
                { "ksp_version_max", "2026.6.9.4800" },   // non-zero build counter
            });
            Assert.Throws<CKAN.Kraken>(
                () => new GameVersionComponentsValidator(new KittenSpaceAgency())
                          .Validate(metadata));
        }

        [Test,
            TestCase("0.2.2"),           // three parts are fine
            TestCase("1.12"),
            TestCase("any"),
        ]
        public void Validate_OtherGameUpToThreeParts_DoesNotThrow(string kspVersion)
        {
            Assert.DoesNotThrow(
                () => new GameVersionComponentsValidator(new KerbalSpaceProgram2())
                          .Validate(new Metadata(new JObject() { { "ksp_version", kspVersion } })));
        }

        [Test]
        public void Validate_OtherGameFourParts_Throws()
        {
            // The widened schema now accepts four parts; this validator restores the
            // max-three-components guard for non-KSA games.
            Assert.Throws<CKAN.Kraken>(
                () => new GameVersionComponentsValidator(new KerbalSpaceProgram2())
                          .Validate(new Metadata(new JObject() { { "ksp_version", "1.2.3.4" } })));
        }

        private static void ValidateKsa(string kspVersion)
            => new GameVersionComponentsValidator(new KittenSpaceAgency())
                   .Validate(new Metadata(new JObject() { { "ksp_version", kspVersion } }));
    }
}
