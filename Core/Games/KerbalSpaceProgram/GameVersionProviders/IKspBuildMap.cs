using CKAN.Versioning;

namespace CKAN.Games.KerbalSpaceProgram.GameVersionProviders
{
    public interface IKspBuildMap
    {
        GameVersion? this[string buildId] { get; }

        GameVersion[] KnownVersions { get; }

        /// <summary>
        /// Download the build map from the server to the cache
        /// </summary>
        void Refresh(string? userAgent);
    }
}
