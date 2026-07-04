using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using CKAN.Games.KerbalSpaceProgram.GameVersionProviders;
using CKAN.Versioning;
using Newtonsoft.Json.Linq;

namespace CKAN.Games.KittenSpaceAgency
{
    public class KsaBuildVersionProvider : IGameVersionProvider
    {
        // The folder the game ships its per-release build info files in, relative
        // to the game dir. GameInstanceManager.FakeInstance writes a minimal file
        // here so a faked KSA instance has a detectable version, so the reader
        // below and that writer must agree on the location and format.
        public static readonly string versionsDirRelative =
            Path.Combine("Content", "Versions");

        // File name for a build info file, mirroring the game's own
        // v<year>.<month>.X.<revision>.json naming (the X is a placeholder in the
        // real files too; the full version lives in the "build" field).
        public static string VersionFileName(GameVersion version)
            => version.IsBuildDefined
                ? string.Format("v{0}.{1}.X.{2}.json", version.Major, version.Minor, version.Build)
                : string.Format("v{0}.json", version);

        // A minimal build info body carrying the one field TryGetVersion reads.
        public static string VersionFileContents(GameVersion version)
            => new JObject { ["build"] = version.ToString() }.ToString();

        public bool TryGetVersion(string directory, [NotNullWhen(true)] out GameVersion? result)
        {
            result = null;

            var versionStr = GetLatestBuildValue(new DirectoryInfo(directory));
            if (versionStr == null) return false;

            return GameVersion.TryParse(versionStr, out result);
        }

        private static string? GetLatestBuildValue(DirectoryInfo root)
        {
            var versionsDir = new DirectoryInfo(
                Path.Combine(root.FullName, versionsDirRelative)
            );

            if (!versionsDir.Exists)
                return null;

            var latest = versionsDir
                .EnumerateFiles("*.json", SearchOption.TopDirectoryOnly)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault();

            if (latest is null)
                return null;

            try
            {
                var text = File.ReadAllText(latest.FullName);
                var obj = JObject.Parse(text);
                return (string?)obj["build"];
            }
            catch
            {
                return null;
            }
        }
    }
}