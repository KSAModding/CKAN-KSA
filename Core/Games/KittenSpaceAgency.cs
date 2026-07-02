using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using Autofac;
using CKAN.DLC;
using CKAN.Extensions;
using CKAN.IO;
using CKAN.Versioning;
using log4net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CKAN.Games.KittenSpaceAgency
{
    public class KittenSpaceAgency : IGame
    {
        public string ShortName => "KSA";
        public DateTime FirstReleaseDate => new DateTime(2025, 11, 14);

        public bool GameInFolder(DirectoryInfo where)
            => InstanceAnchorFiles.Any(f => File.Exists(Path.Combine(where.FullName, f)));

        public DirectoryInfo? MacPath()
        {
            if (Platform.IsMac)
            {
                string installPath = Path.Combine(
                    // This is "/Applications" in Mono on Mac
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "Kitten Space Agency"
                );
                return Directory.Exists(installPath) ? new DirectoryInfo(installPath)
                    : null;
            }
            return null;
        }

        public string PrimaryModDirectoryRelative => "Content";
        public string[] AlternateModDirectoriesRelative => Array.Empty<string>();
        public string PrimaryModDirectory(GameInstance inst)
            => CKANPathUtils.NormalizePath(
                Path.Combine(inst.GameDir, PrimaryModDirectoryRelative));

        public string[] StockFolders => new string[]
        {
            "Content",
            "Content/Core",
            "Content/Fonts",
            "Content/Sample",
            "Content/Shaders",
            "Content/Versions",
            "cs",
            "de",
            "es",
            "fr",
            "it",
            "ja",
            "ko",
            "pl",
            "pt-BR",
            "ru",
            "tr",
            "zh-Hans",
            "zh-Hant",
        };
        
        public string[] LeaveEmptyInClones => Array.Empty<string>();
        public string[] ReservedPaths => Array.Empty<string>();
        public string[] CreateableInstallTos => Array.Empty<string>();
        public string[] CreateableDirs => Array.Empty<string>();
        public string[] AutoRemovableDirs => Array.Empty<string>();

        public bool IsReservedDirectory(GameInstance inst, string path)
            => path == inst.GameDir || path == inst.CkanDir
                                    || path == PrimaryModDirectory(inst);

        public bool AllowInstallationIn(string name, [NotNullWhen(true)] out string? path)
            => allowedFolders.TryGetValue(name, out path);

        public void RebuildSubdirectories(string absGameRoot)
        {
            //TBD: Future versions of KSA might need this
        }

        public string[] DefaultCommandLines(SteamLibrary steamLib, DirectoryInfo path)
            => Enumerable.Repeat(Platform.IsMac
                        ? "./KSA.app/Contents/MacOS/KSA"
                        : string.Format(Platform.IsUnix ? "./{0} -single-instance"
                                : "{0} -single-instance",
                            InstanceAnchorFiles.FirstOrDefault(f =>
                                File.Exists(Path.Combine(path.FullName, f)))
                            ?? InstanceAnchorFiles.First()),
                    1)
                .Concat(steamLib.GameAppURLs(path)
                    .Select(url => url.ToString()))
                .ToArray();

        public string[] AdjustCommandLine(string[] args, GameVersion? installedVersion)
            => args;

        public IDlcDetector[] DlcDetectors  => Array.Empty<IDlcDetector>();
        public IDictionary<string, string[]> InstallFilterPresets 
            => new Dictionary<string, string[]>();
        public void RefreshVersions(string? userAgent)
        {
            try
            {
                if (Net.DownloadText(BuildMapUri, userAgent) is string json)
                {
                    // Route through ParseBuildsJson so the downloaded build map has its
                    // build counter normalized exactly like the embedded and cached maps.
                    var parsed = ParseBuildsJson(JToken.Parse(json));
                    if (parsed.Length > 0)
                    {
                        versions = parsed.ToList();
                        new FileInfo(cachedBuildMapPath).Directory?.Create();
                        json.WriteThroughTo(cachedBuildMapPath);
                    }
                }
            }
            catch (Exception e)
            {
                log.WarnFormat("Could not retrieve latest build map from: {0}", BuildMapUri);
                log.Debug(e);
            }
        }
        
        private static readonly Uri BuildMapUri =
            new Uri("https://raw.githubusercontent.com/KSAModding/KSA-CKAN-meta/main/builds.json");
        private static readonly string cachedBuildMapPath =
            Path.Combine(CKANPathUtils.AppDataPath, "builds-ksa.json");
        
        private List<GameVersion> versions =
            NormalizeAll(JsonConvert.DeserializeObject<List<GameVersion>>(
                File.Exists(cachedBuildMapPath)
                    ? File.ReadAllText(cachedBuildMapPath)
                    : EmbeddedBuildMapJson()))
            .ToList();

        // KSA's 3rd version component out of year.month.buildcounter.revision (the "build counter") is local to the build
        // machine and non-monotonic by design, so it must never influence ordering or
        // compatibility. We normalize / pin it to 0 on every KSA version handed to CKAN, which lets
        // GameVersion.CompareTo fall through to the revision (4th component, the game's
        // real ordinal) and lets compatibility be expressed as year.month.0.revision
        // ranges. NetKAN inflation calls this too, so mod authors can declare the raw
        // game version and CKAN normalizes it into the stamped .ckan.
        public static GameVersion NormalizeBuildCounter(GameVersion v)
            => v.IsBuildDefined ? new GameVersion(v.Major, v.Minor, 0, v.Build)
             : v.IsPatchDefined ? new GameVersion(v.Major, v.Minor, 0)
             : v;

        // Normalize a deserialized build map: drop any null entries a malformed map
        // might contain, then pin every build counter to 0.
        private static IEnumerable<GameVersion> NormalizeAll(IEnumerable<GameVersion>? raw)
            => raw?.OfType<GameVersion>().Select(NormalizeBuildCounter)
               ?? Enumerable.Empty<GameVersion>();

        // The embedded builds-ksa.json resource text, or "" if it is missing.
        private static string EmbeddedBuildMapJson()
            => Assembly.GetExecutingAssembly()
                       .GetManifestResourceStream("CKAN.builds-ksa.json")
                   is Stream s
                       ? new StreamReader(s).ReadToEnd()
                       : "";

        public List<GameVersion> KnownVersions => versions;

        public GameVersion[] EmbeddedGameVersions
            => NormalizeAll(JsonConvert.DeserializeObject<GameVersion[]>(EmbeddedBuildMapJson()))
               .ToArray();

        public GameVersion[] ParseBuildsJson(JToken json)
            => NormalizeAll(json.ToObject<GameVersion[]>())
               .ToArray();

        public GameVersion? DetectVersion(DirectoryInfo where)
            => new KsaBuildVersionProvider().TryGetVersion(where.FullName, out var version)
               && version is not null
                ? NormalizeBuildCounter(version)
                : null;

        public GameVersion[] DefaultCompatibleVersions(GameVersion installedVersion)
            => new GameVersion[]
            {
                new GameVersion(installedVersion.Major,
                    installedVersion.Minor)
            };
        
        // Key: Allowed value of install_to
        // Value: Relative path
        // (PrimaryModDirectoryRelative is allowed implicitly)
        private readonly Dictionary<string, string> allowedFolders = new Dictionary<string, string>
        {
            
        };

        public string CompatibleVersionsFile => "compatible_ksa_versions.json";

        public string[] InstanceAnchorFiles => new string[]
        {
            "KSA.exe"
        };
        public Uri DefaultRepositoryURL => new Uri("https://github.com/KSAModding/KSA-CKAN-meta/archive/main.tar.gz");

        public Uri RepositoryListURL => new Uri("https://raw.githubusercontent.com/KSAModding/KSA-CKAN-meta/main/repositories.json");

        public Uri MetadataBugtrackerURL => new Uri("https://github.com/KSAModding/KSA-NetKAN/issues/new/choose");

        public Uri DiscordURL => new Uri("https://discord.gg/kittenspaceagency");

        public Uri ModSupportURL => new Uri("https://forums.ahwoo.com/forums/guides-and-help.19");

        public void ProcessLoadedModsBeforeGameStart(IReadOnlyCollection<InstalledModule> installedModules)
        {
            log.Info("Loaded KSA mods:");
            foreach (var installedModule in installedModules)
            {
                log.Info("   " + installedModule.Module.name);
            }
            //TODO: Actual construciton of manifest from dependency graph (if we need this)
        }
        
        private static readonly ILog log = LogManager.GetLogger(typeof(KittenSpaceAgency));
    }
}