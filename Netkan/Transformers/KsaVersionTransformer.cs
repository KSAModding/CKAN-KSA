using System.Collections.Generic;

using Newtonsoft.Json.Linq;

using CKAN.Games;
using CKAN.Games.KittenSpaceAgency;
using CKAN.Versioning;
using CKAN.NetKAN.Model;

namespace CKAN.NetKAN.Transformers
{
    /// <summary>
    /// An <see cref="ITransformer"/> that pins KSA's build counter (the 3rd version
    /// component, which is per-build-machine and non-monotonic) to 0 in the
    /// compatibility fields. This lets mod authors declare the raw game version
    /// (for example 2026.6.9.4750) while CKAN normalizes it to match how the KSA game
    /// class stores installed and known versions. Does nothing for other games.
    /// </summary>
    internal sealed class KsaVersionTransformer : ITransformer
    {
        public KsaVersionTransformer(IGame game)
        {
            this.game = game;
        }

        public string Name => "ksa_version";

        public IEnumerable<Metadata> Transform(Metadata metadata, TransformOptions opts)
        {
            if (game is KittenSpaceAgency)
            {
                JObject? json = null;
                foreach (var field in versionFields)
                {
                    if (NormalizedString(metadata.AllJson[field]) is string normalized)
                    {
                        json ??= metadata.Json();
                        json[field] = normalized;
                    }
                }
                if (json != null)
                {
                    yield return new Metadata(json);
                    yield break;
                }
            }
            yield return metadata;
        }

        // The normalized version string if the field holds a game version whose build
        // counter needs zeroing, otherwise null (nothing to change).
        private static string? NormalizedString(JToken? token)
        {
            if ((string?)token is string s
                && GameVersion.TryParse(s, out var v)
                && v is not null)
            {
                var normalized = KittenSpaceAgency.NormalizeBuildCounter(v);
                if (!normalized.Equals(v))
                {
                    return normalized.ToString();
                }
            }
            return null;
        }

        private static readonly string[] versionFields =
        {
            "ksp_version", "ksp_version_min", "ksp_version_max",
        };

        private readonly IGame game;
    }
}
