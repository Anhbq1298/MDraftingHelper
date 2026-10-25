using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MDraftingHeper.AutoCAD2021.App;
using MDraftingHeper.Core.Hooks;
using MDraftingHeper.Core.Steel;

namespace MDraftingHeper.AutoCAD2021.Blocks
{
    /// <summary>Parses profile libraries and hook presets once; reloads only when the files change.</summary>
    public static class ProfileCache
    {
        private static ProfileLibrary _library;
        private static string _libraryStamp;
        private static List<HookPreset> _presets;
        private static string _presetStamp;

        public static ProfileLibrary Library
        {
            get
            {
                var files = Paths.ProfileFiles().ToList();
                var stamp = Stamp(files);
                if (_library != null && stamp == _libraryStamp) return _library;
                var lib = new ProfileLibrary();
                foreach (var f in files)
                {
                    try { lib.Add(ProfileLibraryReader.ReadFile(f)); }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    {
                        lib.Issues.Add(new ProfileImportIssue(Path.GetFileName(f), "Could not read file: " + ex.Message, true));
                    }
                }
                // Errors individually; warnings (e.g. ~1,000 rule-based hollow-section radii) as one summary line.
                foreach (var i in lib.Issues.Where(x => x.IsError)) Log.Warn("Profile import: " + i);
                int warnings = lib.Issues.Count(x => !x.IsError);
                Log.Info("Profile libraries: " + lib.Profiles.Count + " profiles from " + files.Count + " files; " +
                         lib.Issues.Count(x => x.IsError) + " rejected records, " + warnings + " warnings (e.g. assumed corner radii).");
                _library = lib;
                _libraryStamp = stamp;
                return lib;
            }
        }

        public static string LibraryNote
        {
            get
            {
                var lib = Library;
                int demo = lib.Profiles.Count(p => p.IsDemonstration);
                var note = lib.Profiles.Count + " profiles loaded";
                if (demo > 0) note += " (" + demo + " DEMONSTRATION records)";
                int errors = lib.Issues.Count(i => i.IsError);
                if (errors > 0) note += "; " + errors + " import errors (see log)";
                return note + ". Add authorised data to " + Paths.UserProfilesDirectory + ".";
            }
        }

        public static List<HookPreset> HookPresets
        {
            get
            {
                var stamp = Stamp(new[] { Paths.HookPresetFile });
                if (_presets != null && stamp == _presetStamp) return _presets;
                _presets = HookPresetReader.ReadFile(Paths.HookPresetFile, out var problem);
                if (problem != null) Log.Warn(problem);
                _presetStamp = stamp;
                return _presets;
            }
        }

        private static string Stamp(IEnumerable<string> files) =>
            string.Join("|", files.Select(f => f + "@" + (File.Exists(f) ? File.GetLastWriteTimeUtc(f).Ticks : 0)));
    }
}
