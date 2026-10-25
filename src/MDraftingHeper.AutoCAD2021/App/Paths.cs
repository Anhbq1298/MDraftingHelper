using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MDraftingHeper.AutoCAD2021.App
{
    /// <summary>
    /// Locates shipped resources. Bundle layout: Contents\Windows\*.dll + Contents\Resources\...
    /// Development layout: bin\...\*.dll + bin\...\Resources\...
    /// User-supplied data (e.g. an authorised profile export) lives under %APPDATA%\MDraftingHeper.
    /// </summary>
    public static class Paths
    {
        public static string AssemblyDirectory =>
            Path.GetDirectoryName(typeof(Paths).Assembly.Location) ?? AppDomain.CurrentDomain.BaseDirectory;

        public static string ResourcesDirectory
        {
            get
            {
                var bundle = Path.GetFullPath(Path.Combine(AssemblyDirectory, "..", "Resources"));
                if (Directory.Exists(bundle)) return bundle;
                return Path.Combine(AssemblyDirectory, "Resources");
            }
        }

        public static string Resource(params string[] parts) =>
            Path.Combine(new[] { ResourcesDirectory }.Concat(parts).ToArray());

        public static string SteelTemplateDrawing => Resource("DynamicBlocks", "MDH_SteelSections.dwg");
        public static string HookPresetFile => Resource("HookPresets", "hook-presets.json");
        public static string Icon(string name) => Resource("Icons", name);

        public static string UserDataDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MDraftingHeper");

        public static string UserProfilesDirectory => Path.Combine(UserDataDirectory, "Profiles");

        public static string LogDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MDraftingHeper", "logs");

        /// <summary>Shipped profile files first, then user-supplied files.</summary>
        public static IEnumerable<string> ProfileFiles()
        {
            foreach (var dir in new[] { Resource("Profiles"), UserProfilesDirectory })
            {
                if (!Directory.Exists(dir)) continue;
                foreach (var f in Directory.GetFiles(dir).Where(f =>
                             f.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                             (f.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) &&
                              !Path.GetFileName(f).Equals("profile-template.csv", StringComparison.OrdinalIgnoreCase)))
                         .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                    yield return f;
            }
        }
    }
}
