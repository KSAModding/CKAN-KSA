using CKAN.Versioning;
using CKAN.Games;
using CKAN.Games.KittenSpaceAgency;
using CKAN.NetKAN.Model;

namespace CKAN.NetKAN.Validators
{
    /// <summary>
    /// Validates that the compatibility versions use the right number of components for
    /// the game. KSA's 3rd component (the build counter) is per-build-machine and
    /// non-monotonic, so compatibility must be expressed as year.month or
    /// year.month.0.revision (the build counter pinned to 0); inflation normalizes it,
    /// but a hand-authored .ckan could still carry a raw value. Every other game keeps
    /// the historical maximum of three components, which the shared ksp_version schema
    /// pattern no longer enforces on its own now that it allows a fourth part for KSA.
    /// </summary>
    internal sealed class GameVersionComponentsValidator : IValidator
    {
        public GameVersionComponentsValidator(IGame game)
        {
            this.game = game;
        }

        public void Validate(Metadata metadata)
        {
            foreach (var field in versionFields)
            {
                if ((string?)metadata.AllJson[field] is string s
                    && GameVersion.TryParse(s, out var v)
                    && v is not null)
                {
                    if (game is KittenSpaceAgency)
                    {
                        if (v.IsPatchDefined && v.Patch != 0)
                        {
                            throw new Kraken($"{field} \"{s}\" has a non-zero build counter; KSA compatibility must be expressed as year.month or year.month.0.revision because the build counter is not meaningful.");
                        }
                    }
                    else if (v.IsBuildDefined)
                    {
                        throw new Kraken($"{field} \"{s}\" has too many components; game versions may have at most three (major.minor.patch).");
                    }
                }
            }
        }

        private static readonly string[] versionFields =
        {
            "ksp_version", "ksp_version_min", "ksp_version_max",
        };

        private readonly IGame game;
    }
}
