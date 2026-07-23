using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
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

        // StarMap replaces KSA.exe instead of injecting into it like BepInEx
        // does for KSP2. StarMap ships its entry point as StarMap.exe (its
        // release build renames both the launcher and loader outputs to that),
        // in a versioned folder, so we scan the mod folders for that exe by
        // name. If it is not there we just launch KSA.exe like before.
        private const string StarMapExeName    = "StarMap.exe";
        private const string StarMapConfigName = "StarMapConfig.json";

        public string[] AdjustCommandLine(string[] args, GameVersion? installedVersion, GameInstance inst)
            => AdjustCommandLine(args, PrimaryModDirectory(inst), inst.GameDir);

        // modRoot/gameDir are separate args so tests can point them at temp
        // dirs instead of the real Documents mods folder and game install.
        internal string[] AdjustCommandLine(string[] args, string modRoot, string gameDir)
        {
            try
            {
                // A leftover older StarMap folder could sit next to the current
                // one, so pick deterministically (highest folder name) instead of
                // relying on filesystem enumeration order.
                var starMapExe = Directory.Exists(modRoot)
                    ? Directory.EnumerateDirectories(modRoot)
                               .OrderByDescending(dir => dir, StringComparer.OrdinalIgnoreCase)
                               .Select(dir => Path.Combine(dir, StarMapExeName))
                               .FirstOrDefault(File.Exists)
                    : null;
                if (args.Length > 0
                    && args[0].Equals(InstanceAnchorFiles[0], StringComparison.OrdinalIgnoreCase)
                    && starMapExe != null)
                {
                    EnsureStarMapConfig(Path.Combine(Path.GetDirectoryName(starMapExe)!, StarMapConfigName), gameDir);
                    // StarMap loads and runs the game itself; its solo mode takes
                    // no arguments (a first argument is read as a named-pipe name,
                    // which would send it into loader mode), so KSA's own launch
                    // args are intentionally dropped here.
                    return new[] { starMapExe };
                }
            }
            catch (Exception e)
            {
                // This runs before PlayGame's own error handling, so detecting or
                // configuring StarMap must never break launching: on any failure
                // fall back to launching KSA.exe unchanged.
                log.WarnFormat("Could not prepare StarMap launch, falling back to {0}: {1}",
                               InstanceAnchorFiles[0], e.Message);
            }
            return args;
        }

        // StarMap's first run just fails and writes this file if it is missing,
        // so we keep GameLocation pointed at this instance ourselves. Everything
        // else in the file (RepositoryLocation, whatever StarMap adds later) is
        // left alone; RepositoryLocation is only seeded on a brand new or
        // unreadable file, since StarMap's docs say it can be left empty.
        private static void EnsureStarMapConfig(string configPath, string gameDir)
        {
            JObject config;
            try
            {
                config = File.Exists(configPath)
                    ? JObject.Parse(File.ReadAllText(configPath))
                    : new JObject { ["RepositoryLocation"] = "" };
            }
            catch (Exception)
            {
                // A blank or corrupt existing config (e.g. a partial write from a
                // prior StarMap crash) must not abort the launch: start fresh.
                config = new JObject { ["RepositoryLocation"] = "" };
            }
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
            new Uri("https://raw.githubusercontent.com/KSP-CKAN/KSA-CKAN-meta/main/builds.json");
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
        public Uri DefaultRepositoryURL => new Uri("https://github.com/KSP-CKAN/KSA-CKAN-meta/archive/main.tar.gz");

        public Uri RepositoryListURL => new Uri("https://raw.githubusercontent.com/KSP-CKAN/KSA-CKAN-meta/main/repositories.json");

        public Uri MetadataBugtrackerURL => new Uri("https://github.com/KSP-CKAN/KSA-NetKAN/issues/new/choose");

        public Uri DiscordURL => new Uri("https://discord.gg/kittenspaceagency");

        public Uri ModSupportURL => new Uri("https://forums.ahwoo.com/forums/guides-and-help.19");

        // Binds the real user-profile paths, which tests must not touch; the
        // internal overload below carries the logic and is covered.
        [ExcludeFromCodeCoverage]
        public void ProcessLoadedModsBeforeGameStart(IReadOnlyCollection<InstalledModule> installedModules)
            => ProcessLoadedModsBeforeGameStart(installedModules,
                                                manifestPath,
                                                manifestManagedModsPath);

        // manifestFile/managedModsFile are separate args so tests can point them
        // at temp files instead of the real Documents folder and app data.
        internal void ProcessLoadedModsBeforeGameStart(IReadOnlyCollection<InstalledModule> installedModules,
                                                       string manifestFile,
                                                       string managedModsFile)
        {
            log.Info("Loaded KSA mods:");
            foreach (var installedModule in installedModules)
            {
                log.Info("   " + installedModule.Module.name);
            }
            try
            {
                // The game keys manifest entries on the mod's on-disk folder name under
                // the mods directory, not on the CKAN identifier, so derive the folder
                // names from the installed files rather than assuming identifier == folder.
                var modFolders = ModFolderNames(installedModules.SelectMany(im => im.Files),
                                                PrimaryModDirectoryRelative);
                UpdateManifestFile(modFolders, manifestFile, managedModsFile);
            }
            catch (Exception e)
            {
                // Keeping the manifest in sync is a best-effort side effect, so a
                // failure here must never surface as an error after an otherwise
                // successful install or uninstall.
                log.WarnFormat("Could not sync the KSA manifest");
                log.Debug(e);
            }
        }

        // The distinct top-level folder names the installed files occupy under the
        // mod directory (e.g. "mods"). These are the ids the game reads from
        // manifest.toml, since it discovers mods by folder name (ModLibrary.AddMods
        // uses the directory name; ModEntry.Exists checks mods/<id>/mod.toml).
        internal static HashSet<string> ModFolderNames(IEnumerable<string> installedFiles,
                                                       string              modDirectoryRelative)
        {
            var prefix = modDirectoryRelative + "/";
            // Case-insensitive on Windows, matching the file system and the game.
            var files  = installedFiles.Select(CKANPathUtils.NormalizePath)
                                       .ToHashSet(Platform.PathComparer);
            return files.Where(f => f.StartsWith(prefix, Platform.PathComparison))
                        .Select(f => f[prefix.Length..])
                        .Select(rest => (rest, slash: rest.IndexOf('/')))
                        // A mod is a folder under the mod directory (mods/<folder>/...),
                        // so a loose file directly under mods/ with no subpath is not one.
                        .Where(x => x.slash > 0)
                        .Select(x => x.rest[..x.slash])
                        // And it counts only if it actually contains a mod.toml, exactly
                        // as the game decides (ModLibrary.AddMods / ModEntry.Exists check
                        // mods/<folder>/mod.toml); a bundled data-only folder is not a mod.
                        .Where(folder => files.Contains($"{prefix}{folder}/mod.toml"))
                        .ToHashSet(Platform.PathComparer);
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
                // Serialize in a stable order so the file does not churn between runs.
                JsonConvert.SerializeObject(
                    currentIdentifiers.OrderBy(id => id, StringComparer.Ordinal))
                    .WriteThroughTo(managedModsPath);
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
                var managed = File.Exists(managedModsPath)
                    ? JsonConvert.DeserializeObject<HashSet<string>>(File.ReadAllText(managedModsPath))
                    : null;
                // Match mod folder names the way the file system does (case-insensitive
                // on Windows) so a casing difference does not duplicate an entry.
                return new HashSet<string>(managed ?? Enumerable.Empty<string>(),
                                           Platform.PathComparer);
            }
            catch (Exception e)
            {
                log.WarnFormat("Could not read managed mods list at: {0}, treating as empty",
                               managedModsPath);
                log.Debug(e);
                return new HashSet<string>(Platform.PathComparer);
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
            // Defaults to true to match the game's ModEntry, which treats an entry
            // with no enabled key as active.
            public bool Enabled = true;
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
                // Skip the preamble, blank separators, and comment lines. Comments are
                // dropped rather than carried, so we never reposition a user's section
                // header on rewrite; the game does not use them anyway.
                if (current == null || line.Length == 0 || eq < 1 || line.StartsWith("#"))
                {
                    continue;
                }
                var key = line[..eq].Trim();
                switch (key)
                {
                    case "id":
                        current.Id = ParseValue(line[(eq + 1)..]);
                        break;
                    case "enabled":
                        current.Enabled = ParseValue(line[(eq + 1)..]) == "true";
                        break;
                    default:
                        // Keys we do not understand round trip verbatim.
                        current.ExtraLines.Add(line);
                        break;
                }
            }
            return entries;
        }

        // Read a TOML scalar, stripping surrounding quotes and any trailing inline
        // comment. Covers the double quoted (with backslash escapes), single quoted
        // literal, and bare forms a hand editor or CKAN's own writer can produce, so a
        // value like `true # note`, `'MyMod'`, or an id whose folder name contains an
        // escaped quote (`"a\"b"`) is read correctly instead of being truncated. The game
        // itself writes the file by hand without escaping, but reads it back with Tomlet.
        internal static string ParseValue(string raw)
        {
            var s = raw.Trim();
            if (s.Length > 0 && s[0] == '"')
            {
                // Basic string: honor backslash escapes, stop at the first unescaped
                // closing quote (so a trailing inline comment is ignored too).
                return UnescapeTomlBasicString(s);
            }
            if (s.Length > 0 && s[0] == '\'')
            {
                // Literal string: no escaping, verbatim until the next single quote.
                var close = s.IndexOf('\'', 1);
                return close > 0 ? s[1..close] : s[1..];
            }
            var hash = s.IndexOf('#');
            return (hash >= 0 ? s[..hash] : s).Trim();
        }

        // Decode the body of a TOML basic string (a value beginning with a double
        // quote), resolving the escape sequences we emit plus the other standard ones,
        // and stopping at the first unescaped closing quote.
        private static string UnescapeTomlBasicString(string s)
        {
            var sb = new StringBuilder(s.Length);
            for (var i = 1; i < s.Length; i++)
            {
                var c = s[i];
                if (c == '"')
                {
                    break;
                }
                if (c == '\\' && i + 1 < s.Length)
                {
                    var next = s[++i];
                    switch (next)
                    {
                        case '"':  sb.Append('"');  break;
                        case '\\': sb.Append('\\'); break;
                        case 'b':  sb.Append('\b'); break;
                        case 't':  sb.Append('\t'); break;
                        case 'n':  sb.Append('\n'); break;
                        case 'f':  sb.Append('\f'); break;
                        case 'r':  sb.Append('\r'); break;
                        case 'u':
                            // A four digit hex code point (\uXXXX).
                            if (i + 4 < s.Length
                                && ushort.TryParse(s.Substring(i + 1, 4),
                                                   NumberStyles.HexNumber,
                                                   CultureInfo.InvariantCulture,
                                                   out var code))
                            {
                                sb.Append((char)code);
                                i += 4;
                            }
                            else
                            {
                                sb.Append(next);
                            }
                            break;
                        default:
                            // Unknown escape: keep the character after the backslash.
                            sb.Append(next);
                            break;
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        // Encode a string as the body of a TOML basic string (the part between the
        // quotes), escaping the characters that would otherwise break the double quoted
        // form so an id containing a quote, backslash, or control character round trips.
        private static string EscapeTomlBasicString(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"':  sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b");  break;
                    case '\t': sb.Append("\\t");  break;
                    case '\n': sb.Append("\\n");  break;
                    case '\f': sb.Append("\\f");  break;
                    case '\r': sb.Append("\\r");  break;
                    default:
                        // TOML basic strings must escape the C0 controls (< U+0020) and
                        // U+007F (DEL); the game's Tomlet reader rejects any of them raw.
                        if (c < ' ' || c == '\u007F')
                        {
                            sb.Append("\\u")
                              .Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            return sb.ToString();
        }

        internal static string SerializeManifest(IEnumerable<ManifestModEntry> entries)
        {
            var sb = new StringBuilder();
            foreach (var entry in entries)
            {
                sb.Append("[[mods]]\n");
                sb.Append($"id = \"{EscapeTomlBasicString(entry.Id)}\"\n");
                sb.Append($"enabled = {(entry.Enabled ? "true" : "false")}\n");
                foreach (var extra in entry.ExtraLines)
                {
                    sb.Append(extra).Append('\n');
                }
                sb.Append('\n');
            }
            return sb.ToString();
        }

        // Give a newly installed mod an enabled entry, drop entries for mods CKAN used
        // to manage that are no longer installed, and leave every entry already present
        // untouched. The enabled state of an existing entry is deliberately never
        // changed, so a mod the user disabled in-game stays disabled even when an
        // unrelated CKAN operation rewrites the manifest. previouslyManaged is only
        // consulted to decide what CKAN may remove (never to decide enabled state), so
        // a stale or empty managed set can at worst leave an orphan entry (which the
        // game prunes on the next launch), never re-enable a mod behind the user's back.
        internal static List<ManifestModEntry> ReconcileManifest(
                List<ManifestModEntry> entries,
                HashSet<string>        previouslyManaged,
                HashSet<string>        currentIdentifiers)
        {
            // Drop entries for mods CKAN used to manage but that are no longer installed.
            entries.RemoveAll(e => previouslyManaged.Contains(e.Id)
                                   && !currentIdentifiers.Contains(e.Id));
            // Add newly installed mods in a stable (sorted) order so the manifest does
            // not churn its ordering between otherwise identical runs.
            foreach (var identifier in currentIdentifiers.OrderBy(id => id, StringComparer.Ordinal))
            {
                var entry = entries.Find(e => string.Equals(e.Id, identifier, Platform.PathComparison));
                if (entry == null)
                {
                    // Not in the manifest yet, so it is newly installed: enable it.
                    entries.Add(new ManifestModEntry { Id = identifier, Enabled = true });
                }
                else
                {
                    // Already present: leave its enabled state alone (respecting a
                    // deliberate in-game disable). Only adopt the current on-disk casing
                    // so the game, which matches folder names ordinally, does not add a
                    // duplicate entry.
                    entry.Id = identifier;
                }
            }
            return entries;
        }

        private static readonly ILog log = LogManager.GetLogger(typeof(KittenSpaceAgency));
    }
}