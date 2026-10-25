using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace MDraftingHeper.Core.Steel
{
    public enum CornerRadiusStatus { Published, Assumed, Missing }

    /// <summary>A library profile. Dimensions are stored in millimetres after import.</summary>
    public sealed class SteelProfile
    {
        public string Id { get; internal set; }
        public SteelFamily Family { get; internal set; }
        public string Standard { get; internal set; }
        public string Series { get; internal set; }
        public string Name { get; internal set; }
        public SteelSection Section { get; internal set; }
        public CornerRadiusStatus RadiusStatus { get; internal set; }
        public double? PublishedAreaMm2 { get; internal set; }
        public double? PublishedMassKgPerM { get; internal set; }
        public string Source { get; internal set; }
        public string SourceVersion { get; internal set; }
        public bool IsDemonstration { get; internal set; }

        public string DisplayName => (IsDemonstration ? "[DEMO] " : "") + Name;
        public override string ToString() => DisplayName;
    }

    public sealed class ProfileImportIssue
    {
        public string RecordRef { get; }
        public string Message { get; }
        public bool IsError { get; }

        public ProfileImportIssue(string recordRef, string message, bool isError)
        {
            RecordRef = recordRef; Message = message; IsError = isError;
        }

        public override string ToString() => (IsError ? "Error" : "Warning") + " [" + RecordRef + "]: " + Message;
    }

    public sealed class ProfileImportResult
    {
        public List<SteelProfile> Profiles { get; } = new List<SteelProfile>();
        public List<ProfileImportIssue> Issues { get; } = new List<ProfileImportIssue>();
        public int RejectedCount => Issues.Where(i => i.IsError).Select(i => i.RecordRef).Distinct().Count();
    }

    #region JSON schema (version 1)

    [DataContract]
    public sealed class ProfileFileDto
    {
        [DataMember(Name = "schemaVersion", Order = 1)] public int SchemaVersion { get; set; }
        [DataMember(Name = "source", Order = 2)] public string Source { get; set; }
        [DataMember(Name = "sourceVersion", Order = 3)] public string SourceVersion { get; set; }
        [DataMember(Name = "isDemonstration", Order = 4)] public bool IsDemonstration { get; set; }
        [DataMember(Name = "units", Order = 5)] public string Units { get; set; }
        [DataMember(Name = "notes", Order = 6, EmitDefaultValue = false)] public string Notes { get; set; }
        [DataMember(Name = "profiles", Order = 7)] public List<ProfileDto> Profiles { get; set; }
    }

    [DataContract]
    public sealed class ProfileDto
    {
        [DataMember(Name = "id", Order = 1)] public string Id { get; set; }
        [DataMember(Name = "family", Order = 2)] public string Family { get; set; }
        [DataMember(Name = "standard", Order = 3)] public string Standard { get; set; }
        [DataMember(Name = "series", Order = 4)] public string Series { get; set; }
        [DataMember(Name = "name", Order = 5)] public string Name { get; set; }
        [DataMember(Name = "units", Order = 6, EmitDefaultValue = false)] public string Units { get; set; }
        [DataMember(Name = "dimensions", Order = 7)] public Dictionary<string, double> Dimensions { get; set; }

        /// <summary>"outer" or "inner": which corner radius the value of r represents (hollow sections).</summary>
        [DataMember(Name = "cornerRadiusConvention", Order = 8, EmitDefaultValue = false)] public string CornerRadiusConvention { get; set; }

        /// <summary>"published", "assumed" or "missing".</summary>
        [DataMember(Name = "cornerRadiusStatus", Order = 9, EmitDefaultValue = false)] public string CornerRadiusStatus { get; set; }

        [DataMember(Name = "publishedArea_mm2", Order = 10, EmitDefaultValue = false)] public double? PublishedAreaMm2 { get; set; }
        [DataMember(Name = "publishedMass_kg_per_m", Order = 11, EmitDefaultValue = false)] public double? PublishedMassKgPerM { get; set; }
    }

    #endregion

    public static class ProfileLibraryReader
    {
        public const int SupportedSchemaVersion = 1;

        private static readonly DataContractJsonSerializerSettings JsonSettings =
            new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true };

        public static ProfileImportResult ReadFile(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            string text = File.ReadAllText(path, Encoding.UTF8);
            return ext == ".csv" ? ReadCsv(text, Path.GetFileName(path)) : ReadJson(text);
        }

        public static ProfileImportResult ReadJson(string json)
        {
            var result = new ProfileImportResult();
            ProfileFileDto file;
            try
            {
                var ser = new DataContractJsonSerializer(typeof(ProfileFileDto), JsonSettings);
                using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                    file = (ProfileFileDto)ser.ReadObject(ms);
            }
            catch (SerializationException ex)
            {
                result.Issues.Add(new ProfileImportIssue("file", "Not a valid profile JSON file: " + ex.Message, true));
                return result;
            }

            if (file == null || file.SchemaVersion != SupportedSchemaVersion)
            {
                result.Issues.Add(new ProfileImportIssue("file",
                    "Unsupported schemaVersion " + (file?.SchemaVersion.ToString(CultureInfo.InvariantCulture) ?? "?") +
                    "; expected " + SupportedSchemaVersion + ".", true));
                return result;
            }
            if (string.IsNullOrWhiteSpace(file.Source))
                result.Issues.Add(new ProfileImportIssue("file", "The file has no source; provenance is required.", true));
            if (file.Profiles == null || file.Profiles.Count == 0)
                result.Issues.Add(new ProfileImportIssue("file", "The file contains no profiles.", true));
            if (result.Issues.Any(i => i.IsError)) return result;

            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int index = 0;
            foreach (var dto in file.Profiles)
            {
                index++;
                var p = Convert(dto, dto.Units ?? file.Units, file.Source, file.SourceVersion, file.IsDemonstration,
                                "#" + index + (string.IsNullOrWhiteSpace(dto.Id) ? "" : " " + dto.Id), ids, result.Issues);
                if (p != null) result.Profiles.Add(p);
            }
            return result;
        }

        /// <summary>
        /// CSV columns (header required, case-insensitive): id, family, standard, series, name,
        /// units, D, B, H, t, tw, tf, r, cornerRadiusConvention, cornerRadiusStatus,
        /// publishedArea_mm2, publishedMass_kg_per_m, source, sourceVersion, isDemonstration.
        /// </summary>
        public static ProfileImportResult ReadCsv(string csv, string sourceName = "csv")
        {
            var result = new ProfileImportResult();
            var lines = csv.Replace("\r\n", "\n").Split('\n').Where(l => l.Trim().Length > 0 && !l.TrimStart().StartsWith("#")).ToList();
            if (lines.Count < 2)
            {
                result.Issues.Add(new ProfileImportIssue(sourceName, "The CSV file needs a header row and at least one profile.", true));
                return result;
            }
            var header = SplitCsv(lines[0]).Select(h => h.Trim()).ToList();
            int Col(string n) => header.FindIndex(h => string.Equals(h, n, StringComparison.OrdinalIgnoreCase));
            foreach (var required in new[] { "id", "family", "name", "units", "source" })
                if (Col(required) < 0)
                    result.Issues.Add(new ProfileImportIssue(sourceName, "Missing required column '" + required + "'.", true));
            if (result.Issues.Any(i => i.IsError)) return result;

            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 1; i < lines.Count; i++)
            {
                var cells = SplitCsv(lines[i]);
                string Get(string n) { int c = Col(n); return c >= 0 && c < cells.Count ? cells[c].Trim() : ""; }
                var rowRef = "row " + (i + 1) + (Get("id").Length > 0 ? " " + Get("id") : "");

                var dto = new ProfileDto
                {
                    Id = Get("id"), Family = Get("family"), Standard = Get("standard"), Series = Get("series"),
                    Name = Get("name"), CornerRadiusConvention = NullIfEmpty(Get("cornerRadiusConvention")),
                    CornerRadiusStatus = NullIfEmpty(Get("cornerRadiusStatus")),
                    Dimensions = new Dictionary<string, double>(StringComparer.Ordinal)
                };
                bool bad = false;
                foreach (var key in new[] { "D", "B", "H", "t", "tw", "tf", "r" })
                {
                    var s = Get(key);
                    if (s.Length == 0) continue;
                    if (TryNumber(s, out var x)) dto.Dimensions[key] = x;
                    else { result.Issues.Add(new ProfileImportIssue(rowRef, "Value '" + s + "' for " + key + " is not a number.", true)); bad = true; }
                }
                if (TryNumber(Get("publishedArea_mm2"), out var area)) dto.PublishedAreaMm2 = area;
                if (TryNumber(Get("publishedMass_kg_per_m"), out var mass)) dto.PublishedMassKgPerM = mass;
                if (bad) continue;

                bool demo = string.Equals(Get("isDemonstration"), "true", StringComparison.OrdinalIgnoreCase) || Get("isDemonstration") == "1";
                var p = Convert(dto, Get("units"), Get("source"), Get("sourceVersion"), demo, rowRef, ids, result.Issues);
                if (p != null) result.Profiles.Add(p);
            }
            return result;
        }

        public static bool TryUnitFactor(string units, out double toMm)
        {
            switch ((units ?? "").Trim().ToLowerInvariant())
            {
                case "mm": toMm = 1; return true;
                case "cm": toMm = 10; return true;
                case "m": toMm = 1000; return true;
                case "in": case "inch": case "inches": toMm = 25.4; return true;
                default: toMm = double.NaN; return false;
            }
        }

        private static SteelProfile Convert(ProfileDto dto, string units, string source, string sourceVersion, bool demo,
                                            string rowRef, HashSet<string> ids, List<ProfileImportIssue> issues)
        {
            int before = issues.Count(i => i.IsError);
            void Err(string m) => issues.Add(new ProfileImportIssue(rowRef, m, true));

            if (string.IsNullOrWhiteSpace(dto.Id)) Err("Missing stable id.");
            else if (!ids.Add(dto.Id.Trim())) Err("Duplicate id '" + dto.Id + "'. Ids must be unique within a library.");
            if (string.IsNullOrWhiteSpace(dto.Name)) Err("Missing profile name.");
            if (string.IsNullOrWhiteSpace(source)) Err("Missing source (provenance is required).");
            if (!SteelFamilies.TryParse(dto.Family, out var family)) { Err("Unknown family '" + dto.Family + "'."); return null; }
            if (!TryUnitFactor(units, out double k)) { Err("Unknown or missing units '" + units + "'. Use mm, cm, m or in."); return null; }

            var dims = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var kv in dto.Dimensions ?? new Dictionary<string, double>())
            {
                if (!SteelFamilies.Parameters(family).Contains(kv.Key))
                {
                    issues.Add(new ProfileImportIssue(rowRef, "Ignored dimension '" + kv.Key + "', which " + family + " does not use.", false));
                    continue;
                }
                dims[kv.Key] = kv.Value * k;
            }

            var status = CornerRadiusStatus.Published;
            if (!string.IsNullOrEmpty(dto.CornerRadiusStatus) &&
                !Enum.TryParse(dto.CornerRadiusStatus, true, out status))
                Err("cornerRadiusStatus must be published, assumed or missing.");

            if (!dims.ContainsKey(SteelFamilies.R) && SteelFamilies.Parameters(family).Contains(SteelFamilies.R))
                status = CornerRadiusStatus.Missing;

            if (SteelFamilies.IsHollowRect(family) && dims.ContainsKey(SteelFamilies.R))
            {
                var conv = (dto.CornerRadiusConvention ?? "").Trim().ToLowerInvariant();
                if (conv == "inner")
                {
                    if (dims.TryGetValue(SteelFamilies.T, out var t)) dims[SteelFamilies.R] += t; // store the outer radius
                }
                else if (conv != "outer")
                    Err("Hollow sections must state cornerRadiusConvention (outer or inner).");
            }

            foreach (var p in SteelFamilies.Parameters(family))
                if (!dims.ContainsKey(p) && !(p == SteelFamilies.R && status == CornerRadiusStatus.Missing))
                    Err("Missing dimension " + p + ".");

            if (issues.Count(i => i.IsError) > before) return null;

            var section = new SteelSection(family, dims);
            if (status != CornerRadiusStatus.Missing)
            {
                var v = SteelValidator.Validate(section);
                if (!v.IsValid)
                {
                    foreach (var e in v.Errors) Err(e);
                    return null;
                }
            }
            if (status == CornerRadiusStatus.Assumed)
                issues.Add(new ProfileImportIssue(rowRef, "Corner/root radius is an assumption, not a published value.", false));

            return new SteelProfile
            {
                Id = dto.Id.Trim(), Family = family, Standard = dto.Standard, Series = dto.Series, Name = dto.Name.Trim(),
                Section = section, RadiusStatus = status,
                PublishedAreaMm2 = dto.PublishedAreaMm2.HasValue ? dto.PublishedAreaMm2 * k * k : null,
                PublishedMassKgPerM = dto.PublishedMassKgPerM,
                Source = source, SourceVersion = sourceVersion, IsDemonstration = demo
            };
        }

        private static string NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;

        private static bool TryNumber(string s, out double x) =>
            double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out x) && !double.IsNaN(x) && !double.IsInfinity(x);

        internal static List<string> SplitCsv(string line)
        {
            var cells = new List<string>();
            var sb = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (quoted)
                {
                    if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else if (c == '"') quoted = false;
                    else sb.Append(c);
                }
                else if (c == '"') quoted = true;
                else if (c == ',') { cells.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(c);
            }
            cells.Add(sb.ToString());
            return cells;
        }
    }

    /// <summary>Holds the loaded profiles of one or more libraries; parse once and cache.</summary>
    public sealed class ProfileLibrary
    {
        private readonly List<SteelProfile> _profiles = new List<SteelProfile>();
        public IReadOnlyList<SteelProfile> Profiles => _profiles;
        public List<ProfileImportIssue> Issues { get; } = new List<ProfileImportIssue>();

        public void Add(ProfileImportResult r)
        {
            var existing = new HashSet<string>(_profiles.Select(p => p.Id), StringComparer.OrdinalIgnoreCase);
            foreach (var p in r.Profiles)
            {
                if (existing.Add(p.Id)) _profiles.Add(p);
                else Issues.Add(new ProfileImportIssue(p.Id, "Duplicate id across libraries; the first loaded profile is kept.", true));
            }
            Issues.AddRange(r.Issues);
        }

        public SteelProfile FindById(string id) =>
            _profiles.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

        public IEnumerable<SteelProfile> ForFamily(SteelFamily f) => _profiles.Where(p => p.Family == f);

        public IEnumerable<SteelProfile> Search(SteelFamily f, string text)
        {
            var q = ForFamily(f);
            if (string.IsNullOrWhiteSpace(text)) return q;
            var terms = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return q.Where(p => terms.All(t =>
                (p.Name ?? "").IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0 ||
                (p.Series ?? "").IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0 ||
                (p.Standard ?? "").IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0));
        }

        /// <summary>
        /// Returns the library profile whose dimensions all match the section within the
        /// tolerance, preferring <paramref name="preferredId"/>; null means the section is Custom.
        /// </summary>
        public SteelProfile Match(SteelSection s, double tolerance = 0.01, string preferredId = null)
        {
            bool Matches(SteelProfile p) =>
                p.Family == s.Family && p.RadiusStatus != CornerRadiusStatus.Missing &&
                SteelFamilies.Parameters(s.Family).All(k => Math.Abs(p.Section[k] - s[k]) <= tolerance);

            var preferred = preferredId == null ? null : FindById(preferredId);
            if (preferred != null && Matches(preferred)) return preferred;
            return _profiles.FirstOrDefault(Matches);
        }
    }
}
