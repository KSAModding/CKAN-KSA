using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
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
            UpdateManifestFile(installedModules.Select(im => im.identifier).ToHashSet(),
                                manifestPath, manifestManagedModsPath);
        }

        // manifestPath/managedModsPath are separate args so tests can point them
        // at temp files instead of the real Documents folder and app data.
        //
        // KSA reads manifest.toml at <Documents>/My Games/Kitten Space Agency/manifest.toml
        // to decide which dropped mod folders are active on the next launch. The game
        // auto discovers new mod folders and adds them here as disabled, so without this
        // every mod CKAN installs would sit inactive until the user flips it on by hand.
        internal static void UpdateManifestFile(HashSet<string> currentIdentifiers,
                                                string           manifestPath,
                                                string           managedModsPath)
        {
            try
            {
                var entries = File.Exists(manifestPath)
                    ? ParseManifest(File.ReadAllText(manifestPath))
                    : new List<ManifestModEntry>();
                var previouslyManaged = ReadManagedMods(managedModsPath);

                var updated = ReconcileManifest(entries, previouslyManaged, currentIdentifiers);

                new FileInfo(manifestPath).Directory?.Create();
                SerializeManifest(updated).WriteThroughTo(manifestPath);

                new FileInfo(managedModsPath).Directory?.Create();
                JsonConvert.SerializeObject(currentIdentifiers).WriteThroughTo(managedModsPath);
            }
            catch (Exception e)
            {
                log.WarnFormat("Could not update manifest at: {0}", manifestPath);
                log.Debug(e);
            }
        }

        // A malformed managed-mods file must not block every future manifest
        // update until someone deletes it by hand, so a bad or missing file
        // is just treated as empty.
        internal static HashSet<string> ReadManagedMods(string managedModsPath)
        {
            try
            {
                return File.Exists(managedModsPath)
                    ? JsonConvert.DeserializeObject<HashSet<string>>(File.ReadAllText(managedModsPath))
                      ?? new HashSet<string>()
                    : new HashSet<string>();
            }
            catch (Exception e)
            {
                log.WarnFormat("Could not read managed mods list at: {0}, treating as empty",
                               managedModsPath);
                log.Debug(e);
                return new HashSet<string>();
            }
        }

        private static readonly string manifestPath =
            Path.Combine(Path.GetDirectoryName(UserModDirectory) ?? "", "manifest.toml");

        // Identifiers whose manifest entry CKAN currently owns, kept in CKAN's own
        // app data rather than in manifest.toml itself. This is how we tell "a mod
        // CKAN used to install but has since removed" (safe to drop the entry for)
        // apart from entries CKAN has never touched, like the game's own Core entry.
        private static readonly string manifestManagedModsPath =
            Path.Combine(CKANPathUtils.AppDataPath, "ksa-manifest-managed-mods.json");

        // One [[mods]] entry from manifest.toml. Keys other than id and enabled are
        // kept as raw lines so a future game update that adds fields does not lose
        // them the next time we rewrite the file.
        internal sealed class ManifestModEntry
        {
            public string Id = "";
            public bool Enabled;
            public List<string> ExtraLines = new List<string>();
        }

        // manifest.toml is a flat list of [[mods]] entries with id and enabled keys.
        // Only those two keys are understood here, everything else in an entry is
        // kept as a raw line so it round trips through SerializeManifest untouched.
        internal static List<ManifestModEntry> ParseManifest(string text)
        {
            var entries = new List<ManifestModEntry>();
            ManifestModEntry? current = null;
            foreach (var rawLine in text.Split('\n'))
            {
                var line = rawLine.Trim();
                if (line == "[[mods]]")
                {
                    current = new ManifestModEntry();
                    entries.Add(current);
                    continue;
                }
                var eq = line.IndexOf('=');
                if (current == null || line.Length == 0 || eq < 1)
                {
                    continue;
                }
                var key   = line[..eq].Trim();
                var value = line[(eq + 1)..].Trim();
                switch (key)
                {
                    case "id":
                        current.Id = value.Trim('"');
                        break;
                    case "enabled":
                        current.Enabled = value == "true";
                        break;
                    default:
                        current.ExtraLines.Add(line);
                        break;
                }
            }
            return entries;
        }

        internal static string SerializeManifest(IEnumerable<ManifestModEntry> entries)
        {
            var sb = new StringBuilder();
            foreach (var entry in entries)
            {
                sb.Append("[[mods]]\n");
                sb.Append($"id = \"{entry.Id}\"\n");
                sb.Append($"enabled = {(entry.Enabled ? "true" : "false")}\n");
                foreach (var extra in entry.ExtraLines)
                {
                    sb.Append(extra).Append('\n');
                }
                sb.Append('\n');
            }
            return sb.ToString();
        }

        // Make sure every currently installed mod has an enabled entry, and drop
        // entries for mods CKAN used to manage that are no longer installed.
        // Entries CKAN has never managed are left alone either way.
        internal static List<ManifestModEntry> ReconcileManifest(
                List<ManifestModEntry> entries,
                HashSet<string>        previouslyManaged,
                HashSet<string>        currentIdentifiers)
        {
            entries.RemoveAll(e => previouslyManaged.Contains(e.Id)
                                   && !currentIdentifiers.Contains(e.Id));
            foreach (var identifier in currentIdentifiers)
            {
                var entry = entries.Find(e => e.Id == identifier);
                if (entry == null)
                {
                    entries.Add(new ManifestModEntry { Id = identifier, Enabled = true });
                }
                else
                {
                    entry.Enabled = true;
                }
            }
            return entries;
        }

        private static readonly ILog log = LogManager.GetLogger(typeof(KittenSpaceAgency));
    }
}