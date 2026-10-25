using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace MDraftingHeper.Core.Hooks
{
    public enum HookAngle { Deg90 = 90, Deg135 = 135, Deg180 = 180 }

    public enum HookEnds { EndA, EndB, Both }

    /// <summary>Tie bar size with its drafting lineweight. Lineweight is graphical only.</summary>
    public sealed class TieBarSize
    {
        public string Name { get; }
        public double Diameter { get; }

        /// <summary>Entity lineweight in hundredths of a millimetre (AutoCAD LineWeight enum value).</summary>
        public int LineweightHundredthsMm { get; }

        public TieBarSize(string name, double diameter, int lineweight)
        {
            Name = name; Diameter = diameter; LineweightHundredthsMm = lineweight;
        }

        public static readonly IReadOnlyList<TieBarSize> Standard = new[]
        {
            new TieBarSize("H10", 10, 18),
            new TieBarSize("H12", 12, 20),
            new TieBarSize("H13", 13, 25),
            new TieBarSize("H16", 16, 25),
            new TieBarSize("H20", 20, 30),
        };

        public static TieBarSize Find(string name) =>
            Standard.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

        public override string ToString() => Name;
    }

    [DataContract]
    public sealed class MandrelRule
    {
        /// <summary>Rule applies to tie diameters up to and including this value (mm); null = no limit.</summary>
        [DataMember(Order = 1)] public double? MaxTieDiameter { get; set; }

        /// <summary>Minimum inside mandrel (bend) diameter as a multiple of the tie diameter.</summary>
        [DataMember(Order = 2)] public double FactorOfTieDiameter { get; set; }
    }

    [DataContract]
    public sealed class TailRule
    {
        [DataMember(Order = 1)] public int AngleDegrees { get; set; }
        [DataMember(Order = 2)] public double FactorOfTieDiameter { get; set; }
        [DataMember(Order = 3)] public double MinimumMm { get; set; }
    }

    /// <summary>
    /// A hook preset with its provenance. Values are kept separate from the clause text they are
    /// meant to represent; <see cref="IsVerified"/> is true only when checked against the
    /// authorised code text.
    /// </summary>
    [DataContract]
    public sealed class HookPreset
    {
        [DataMember(Order = 1)] public string Id { get; set; }
        [DataMember(Order = 2)] public string Name { get; set; }
        [DataMember(Order = 3)] public bool IsManual { get; set; }
        [DataMember(Order = 4)] public bool IsVerified { get; set; }
        [DataMember(Order = 5)] public string CodeBasis { get; set; }
        [DataMember(Order = 6)] public string Edition { get; set; }
        [DataMember(Order = 7)] public string Clause { get; set; }
        [DataMember(Order = 8)] public string Scope { get; set; }
        [DataMember(Order = 9)] public List<MandrelRule> MandrelRules { get; set; } = new List<MandrelRule>();
        [DataMember(Order = 10)] public List<TailRule> TailRules { get; set; } = new List<TailRule>();
        [DataMember(Order = 11)] public string Notes { get; set; }

        public string DisplayName => IsVerified ? Name : Name + " — UNVERIFIED";

        /// <summary>Inside mandrel diameter (mm) for the tie, or null when the preset has no rule.</summary>
        public double? InsideMandrelDiameter(double tieDiameter)
        {
            var rule = MandrelRules
                .Where(r => !r.MaxTieDiameter.HasValue || tieDiameter <= r.MaxTieDiameter.Value + 1e-9)
                .OrderBy(r => r.MaxTieDiameter ?? double.MaxValue)
                .FirstOrDefault();
            return rule == null ? (double?)null : rule.FactorOfTieDiameter * tieDiameter;
        }

        /// <summary>Straight tail length (mm) for the hook angle, or null when the preset has no rule.</summary>
        public double? TailLength(HookAngle angle, double tieDiameter)
        {
            var rule = TailRules.FirstOrDefault(r => r.AngleDegrees == (int)angle);
            return rule == null ? (double?)null : Math.Max(rule.FactorOfTieDiameter * tieDiameter, rule.MinimumMm);
        }

        public override string ToString() => DisplayName;
    }
}
