using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using Autofac;
using CKAN.DLC;
using CKAN.Games.KerbalSpaceProgram.GameVersionProviders;
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
            //TODO: Implement
        }
        
        private static readonly string cachedBuildMapPath =
            Path.Combine(CKANPathUtils.AppDataPath, "builds-ksa.json");
        
        private List<GameVersion> versions =
            JsonConvert.DeserializeObject<List<GameVersion>>(
                File.Exists(cachedBuildMapPath)
                    ? File.ReadAllText(cachedBuildMapPath)
                    : Assembly.GetExecutingAssembly()
                            .GetManifestResourceStream("CKAN.builds-ksa.json")
                        is Stream s
                        ? new StreamReader(s).ReadToEnd()
                        : "")
            ?? new List<GameVersion>();

        public List<GameVersion> KnownVersions => versions;
            
        
        public GameVersion[] EmbeddedGameVersions
        => (Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream("CKAN.builds-ksp.json")
                        is Stream s
                    ? JsonConvert.DeserializeObject<JBuilds>(
                    new StreamReader(s).ReadToEnd())
                        : null)
                        ?.Builds
                        ?.Select(b => GameVersion.Parse(b.Value))
                        .ToArray()
                        ?? Array.Empty<GameVersion>();
        public GameVersion[] ParseBuildsJson(JToken json)
            => json.ToObject<JBuilds>()
                   ?.Builds
                   ?.Select(b => GameVersion.Parse(b.Value))
                   .ToArray()
               ?? Array.Empty<GameVersion>();

        public GameVersion? DetectVersion(DirectoryInfo where)
        {
            var versionProvider = new KsaBuildVersionProvider();
            GameVersion? version = null;

            versionProvider.TryGetVersion(where.FullName, out version);
            
            return version;
        }

        public GameVersion[] DefaultCompatibleVersions(GameVersion installedVersion)
            => new GameVersion[]
            {
                GameVersion.Parse("any")
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
        public Uri DefaultRepositoryURL => new Uri("https://github.com/KSP-CKAN/KSA-CKAN-meta/archive/main.tar.gz");

        public Uri RepositoryListURL => new Uri("https://raw.githubusercontent.com/KSP-CKAN/KSA-CKAN-meta/main/repositories.json");

        public Uri MetadataBugtrackerURL => new Uri("https://github.com/KSP-CKAN/KSA-NetKAN/issues/new/choose");

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