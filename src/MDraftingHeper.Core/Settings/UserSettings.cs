using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace MDraftingHeper.Core.Settings
{
    [DataContract]
    public sealed class LayerSettings
    {
        [DataMember] public string RebarTable { get; set; } = "MDH_REBAR_TABLE";
        [DataMember] public string Tie { get; set; } = "MDH_TIE";
        [DataMember] public string Steel { get; set; } = "MDH_STEEL";
        [DataMember] public string Axes { get; set; } = "MDH_AXES";
        [DataMember] public string Label { get; set; } = "MDH_LABEL";
        [DataMember] public string Section { get; set; } = "MDH_SECTION";
        [DataMember] public string LongitudinalRebar { get; set; } = "MDH_REBAR";
    }

    [DataContract]
    public sealed class RebarSettings
    {
        [DataMember] public double DiameterTolerance { get; set; } = 0.1;
        [DataMember] public double? TargetRhoPercent { get; set; }
        [DataMember] public string TableStyle { get; set; } = "Standard";
        [DataMember] public string TextStyle { get; set; } = "Standard";
        [DataMember] public double TableScale { get; set; } = 1.0;
        [DataMember] public double TextHeight { get; set; } = 2.5;
    }

    [DataContract]
    public sealed class TieSettings
    {
        /// <summary>"OpenTie" or "ClosedHoop".</summary>
        [DataMember] public string Mode { get; set; } = "OpenTie";
        [DataMember] public int AngleA { get; set; } = 135;
        [DataMember] public int AngleB { get; set; } = 135;
        [DataMember] public string TieSize { get; set; } = "H10";
        [DataMember] public string Ends { get; set; } = "Both";
        [DataMember] public double ClearGap { get; set; }
    }

    [DataContract]
    public sealed class SteelSettings
    {
        [DataMember] public string Family { get; set; } = "UB";
        [DataMember] public string ProfileId { get; set; }
        [DataMember] public string Anchor { get; set; } = "Centroid";
        [DataMember] public double RotationDegrees { get; set; }
        [DataMember] public bool CentreAxes { get; set; }
        [DataMember] public bool ProfileLabel { get; set; }
        [DataMember] public double LabelHeight { get; set; } = 2.5;
        [DataMember] public double MatchTolerance { get; set; } = 0.01;
    }

    /// <summary>Per-user options, persisted as JSON under %APPDATA%\MDraftingHeper.</summary>
    [DataContract]
    public sealed class UserSettings
    {
        public const int CurrentVersion = 3;

        [DataMember(Order = 0)] public int Version { get; set; } = CurrentVersion;
        [DataMember] public LayerSettings Layers { get; set; } = new LayerSettings();
        [DataMember] public RebarSettings Rebar { get; set; } = new RebarSettings();
        [DataMember] public TieSettings Tie { get; set; } = new TieSettings();
        [DataMember] public SteelSettings Steel { get; set; } = new SteelSettings();

        /// <summary>Restores defaults for sections that a partial or old file left empty.</summary>
        public UserSettings Normalised()
        {
            Layers = Layers ?? new LayerSettings();
            if (string.IsNullOrWhiteSpace(Layers.RebarTable)) Layers.RebarTable = "MDH_REBAR_TABLE";
            if (string.IsNullOrWhiteSpace(Layers.Tie)) Layers.Tie = "MDH_TIE";
            if (string.IsNullOrWhiteSpace(Layers.Steel)) Layers.Steel = "MDH_STEEL";
            if (string.IsNullOrWhiteSpace(Layers.Axes)) Layers.Axes = "MDH_AXES";
            if (string.IsNullOrWhiteSpace(Layers.Label)) Layers.Label = "MDH_LABEL";
            if (string.IsNullOrWhiteSpace(Layers.Section)) Layers.Section = "MDH_SECTION";
            if (string.IsNullOrWhiteSpace(Layers.LongitudinalRebar)) Layers.LongitudinalRebar = "MDH_REBAR";
            Rebar = Rebar ?? new RebarSettings();
            Tie = Tie ?? new TieSettings();
            Steel = Steel ?? new SteelSettings();
            // Files from before per-end angles have no AngleA/AngleB (deserialised as 0).
            if (Tie.AngleA != 90 && Tie.AngleA != 135 && Tie.AngleA != 180) Tie.AngleA = 135;
            if (Tie.AngleB != 90 && Tie.AngleB != 135 && Tie.AngleB != 180) Tie.AngleB = 135;
            if (Tie.Mode != "OpenTie" && Tie.Mode != "ClosedHoop") Tie.Mode = "OpenTie";
            Steel.CentreAxes = false;
            Steel.ProfileLabel = false;
            Version = CurrentVersion;
            return this;
        }
    }

    public static class SettingsStore
    {
        public static string DefaultDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MDraftingHeper");

        public static string DefaultPath => Path.Combine(DefaultDirectory, "settings.json");

        /// <summary>Loads settings; a missing or corrupt file yields defaults (and reports why).</summary>
        public static UserSettings Load(string path, out string problem)
        {
            problem = null;
            try
            {
                if (!File.Exists(path)) return new UserSettings();
                var ser = new DataContractJsonSerializer(typeof(UserSettings));
                using (var fs = File.OpenRead(path))
                {
                    var s = ser.ReadObject(fs) as UserSettings;
                    if (s == null) { problem = "Settings file was empty; defaults used."; return new UserSettings(); }
                    return s.Normalised();
                }
            }
            catch (Exception ex) when (ex is SerializationException || ex is IOException || ex is UnauthorizedAccessException || ex is InvalidCastException)
            {
                problem = "Settings could not be read (" + ex.Message + "); defaults used.";
                return new UserSettings();
            }
        }

        public static void Save(string path, UserSettings settings)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var ser = new DataContractJsonSerializer(typeof(UserSettings));
            var tmp = path + ".tmp";
            using (var fs = File.Create(tmp))
            using (var w = JsonReaderWriterFactory.CreateJsonWriter(fs, Encoding.UTF8, true, true))
                ser.WriteObject(w, settings);
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
        }
    }
}
