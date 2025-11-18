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
                Path.Combine(root.FullName, "content", "Versions")
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