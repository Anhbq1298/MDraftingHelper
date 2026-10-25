using System;
using System.Collections.Generic;
using System.Linq;
using MDraftingHeper.Core.Geometry;
using MDraftingHeper.Core.Hooks;

namespace MDraftingHeper.Core.Rectangular
{
    public enum ReinforcementMode { SpecifyByFace, CustomCoordinates }
    public enum RectangularFace { Top, Bottom, Left, Right }
    [Flags]
    public enum FaceRole { None = 0, Top = 1, Bottom = 2, Left = 4, Right = 8 }
    public enum TieSource { Auto, Manual, AutoModified }
    public enum RectangularTieKind { OuterHoop, ClosedHoop, CrossTie }
    public enum RestraintDistanceConvention { CentreToCentre, ClearDistance }

    public sealed class FaceParameters
    {
        public int BarsOnBFace { get; set; } = 5;
        public double BFaceDiameter { get; set; } = 20;
        public int BarsOnHFace { get; set; } = 4;
        public double HFaceDiameter { get; set; } = 20;
    }

    public sealed class AutoTieSettings
    {
        public bool Enabled { get; set; } = true;
        public double DistanceLimit { get; set; } = 150;
        public RestraintDistanceConvention DistanceConvention { get; set; } = RestraintDistanceConvention.CentreToCentre;
        public string TieSizeName { get; set; } = "H10";
        public HookAngle HookA { get; set; } = HookAngle.Deg135;
        public HookAngle HookB { get; set; } = HookAngle.Deg135;
        public bool Flip { get; set; }
        public string PresetId { get; set; } = "project-bend-detail";
        public double ClearGap { get; set; }
        public double InsideMandrelDiameter { get; set; } = 20;
        public double TailLengthA { get; set; } = 100;
        public double TailLengthB { get; set; } = 100;
        public double TailLength { get => TailLengthA; set { TailLengthA = value; TailLengthB = value; } }
    }

    public sealed class LongitudinalBar
    {
        public Guid BarId { get; }
        public string BarName { get; set; }
        public double Diameter { get; set; }
        public Point2 Center { get; set; }
        public FaceRole FaceRole { get; set; }

        public LongitudinalBar(string name, double diameter, Point2 center, FaceRole role = FaceRole.None, Guid? id = null)
        {
            BarId = id ?? Guid.NewGuid();
            BarName = name;
            Diameter = diameter;
            Center = center;
            FaceRole = role;
        }

        public bool IsOn(RectangularFace face) => (FaceRole & Role(face)) != 0;
        public static FaceRole Role(RectangularFace face)
        {
            switch (face)
            {
                case RectangularFace.Top: return FaceRole.Top;
                case RectangularFace.Bottom: return FaceRole.Bottom;
                case RectangularFace.Left: return FaceRole.Left;
                default: return FaceRole.Right;
            }
        }
    }

    public sealed class TieBar
    {
        public Guid TieId { get; }
        public string TieName { get; set; }
        public TieSource Source { get; set; }
        public RectangularTieKind Kind { get; set; }
        public Guid? BarAId { get; set; }
        public Guid? BarBId { get; set; }
        public List<Guid> OrderedSupportBarIds { get; } = new List<Guid>();
        public List<Guid> DirectlyRestrainedBarIds { get; } = new List<Guid>();
        public string TieSizeName { get; set; } = "H10";
        public HookAngle HookA { get; set; } = HookAngle.Deg135;
        public HookAngle HookB { get; set; } = HookAngle.Deg135;
        public bool Flip { get; set; }
        public string PresetId { get; set; } = "project-bend-detail";
        public double ClearGap { get; set; }
        public double InsideMandrelDiameter { get; set; } = 20;
        public double TailLengthA { get; set; } = 100;
        public double TailLengthB { get; set; } = 100;
        public double TailLength { get => TailLengthA; set { TailLengthA = value; TailLengthB = value; } }
        public long LayoutRevision { get; set; }

        public TieBar(Guid? id = null) { TieId = id ?? Guid.NewGuid(); }
    }

    public sealed class RectangularSectionModel
    {
        public double Width { get; set; } = 500;
        public double Height { get; set; } = 500;
        public double Cover { get; set; } = 40;
        public ReinforcementMode ReinforcementMode { get; set; } = ReinforcementMode.SpecifyByFace;
        public FaceParameters FaceParameters { get; } = new FaceParameters();
        public AutoTieSettings AutoTieSettings { get; } = new AutoTieSettings();
        public List<LongitudinalBar> LongitudinalBars { get; } = new List<LongitudinalBar>();
        public List<TieBar> TieBars { get; } = new List<TieBar>();
        public long LayoutRevision { get; internal set; }
        public bool TiesRequireDefinition { get; set; }

        public double GrossArea => Width * Height;
        public double SteelArea => LongitudinalBars.Sum(b => Math.PI * b.Diameter * b.Diameter / 4);
        public double RhoPercent => GrossArea > 0 ? 100 * SteelArea / GrossArea : double.NaN;
        public LongitudinalBar FindBar(Guid id) => LongitudinalBars.FirstOrDefault(b => b.BarId == id);
    }
}
