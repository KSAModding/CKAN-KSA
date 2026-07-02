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

        // KSA loads mods from the user's Documents folder, which survives game
        // updates, rather than from inside the game directory. "mods" is the
        // relative prefix CKAN records in the registry for each installed file;
        // it is mapped onto the external UserModDirectory (which itself ends in
        // "mods") whenever a path is resolved. Because the mod root is outside
        // GameDir, ModDirectoryIsExternal is true and the mapping happens in
        // GameInstance.ToAbsoluteGameDir / ToRelativeGameDir.
        public string PrimaryModDirectoryRelative => "mods";
        public string[] AlternateModDirectoriesRelative => Array.Empty<string>();

        // Unlike KSP1/KSP2 this absolute mod root is outside GameDir: it is the
        // game's own recommended mods folder under the user's Documents directory,
        // shared across all KSA instances. The inst parameter is unused because
        // the location is user-global, matching how the game itself resolves it.
        public string PrimaryModDirectory(GameInstance inst) => UserModDirectory;

        public bool ModDirectoryIsExternal => true;

        // <Documents>/My Games/Kitten Space Agency/mods, the folder the game
        // scans for user-installed mods. Computed once because it is process
        // stable and resolved on every path conversion. Path.GetFullPath keeps
        // the root absolute even on an unusual profile where SpecialFolder.Personal
        // is empty, so ToAbsoluteGameDir never trips on a non-rooted mod root.
        private static readonly string UserModDirectory =
            CKANPathUtils.NormalizePath(Path.GetFullPath(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Personal),
                "My Games", "Kitten Space Agency", "mods")));

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
        // Let the installer create the per-mod folders under the mod root (and the
        // mod root itself), the same way KSP2 lists its GameData/Mods target here.
        public string[] CreateableDirs => new string[] { "mods" };
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

        // StarMap replaces KSA.exe instead of injecting into it like BepInEx
        // does for KSP2. Its release folder name is versioned
        // (StarMapLauncher-0.4.5 as of writing) so we scan for the exe by
        // name instead of assuming a fixed folder. If it's not there we just
        // launch KSA.exe like before.
        private const string StarMapExeName    = "StarMap.exe";
        private const string StarMapConfigName = "StarMapConfig.json";

        public string[] AdjustCommandLine(string[] args, GameVersion? installedVersion, GameInstance inst)
            => AdjustCommandLine(args, PrimaryModDirectory(inst), inst.GameDir);

        // modRoot/gameDir are separate args so tests can point them at temp
        // dirs instead of the real Documents mods folder and game install
        internal string[] AdjustCommandLine(string[] args, string modRoot, string gameDir)
        {
            var starMapExe = Directory.Exists(modRoot)
                ? Directory.EnumerateDirectories(modRoot)
                           .Select(dir => Path.Combine(dir, StarMapExeName))
                           .FirstOrDefault(File.Exists)
                : null;
            if (args.Length > 0
                && args[0].Equals(InstanceAnchorFiles[0], StringComparison.OrdinalIgnoreCase)
                && starMapExe != null)
            {
                EnsureStarMapConfig(Path.Combine(Path.GetDirectoryName(starMapExe)!, StarMapConfigName), gameDir);
                return new[] { starMapExe }.Concat(args.Skip(1)).ToArray();
            }
            return args;
        }

        // StarMap's first run just fails and writes this file if it's missing
        // or wrong, so we keep GameLocation pointed at this instance ourselves.
        // Everything else in the file (RepositoryLocation, whatever StarMap
        // adds later) is left alone, RepositoryLocation only gets set on a
        // brand new file since StarMap's docs say it can be left empty.
        private static void EnsureStarMapConfig(string configPath, string gameDir)
        {
            var config = File.Exists(configPath)
                ? JObject.Parse(File.ReadAllText(configPath))
                : new JObject { ["RepositoryLocation"] = "" };
            config["GameLocation"] = gameDir;
            config.ToString().WriteThroughTo(configPath);
        }

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

        // KSA compatibility can be expressed month-granular (year.month), which
        // covers any revision in that month. Render such a value as year.month.*
        // so it does not look like an incomplete build number in the UI.
        public string FormatVersion(GameVersion v)
            => v.IsMinorDefined && !v.IsPatchDefined
                ? $"{v.Major}.{v.Minor}.*"
                : v.ToString() ?? "";

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