using System.Collections.Generic;
using MDraftingHeper.Core.Geometry;

namespace MDraftingHeper.Core.Rebar
{
    /// <summary>One rebar Circle read from the drawing, already projected into the section plane.</summary>
    public sealed class RebarBar
    {
        /// <summary>Stable identity of the source entity (e.g. the handle); used to reject duplicates.</summary>
        public string SourceId { get; }
        public Point2 Center { get; }
        public double Diameter { get; }

        public RebarBar(string sourceId, Point2 center, double diameter)
        {
            SourceId = sourceId; Center = center; Diameter = diameter;
        }
    }

    public enum BarPlacement { Inside, Crossing, Outside }

    public sealed class RebarGroupingOptions
    {
        /// <summary>Bars whose diameters differ by no more than this (mm) are grouped together.</summary>
        public double DiameterTolerance { get; set; } = 0.1;

        /// <summary>Nominal bar diameters (mm) used only to label groups; never to change measured sizes.</summary>
        public IList<double> NominalDiameters { get; set; } = new List<double> { 6, 8, 10, 12, 13, 16, 20, 25, 32, 40, 50 };

        public string NominalPrefix { get; set; } = "H";
    }

    public sealed class RebarGroup
    {
        public string Label { get; internal set; }
        public bool IsNominal { get; internal set; }
        public double? NominalDiameter { get; internal set; }

        /// <summary>Mean measured diameter of the group (mm).</summary>
        public double MeanDiameter { get; internal set; }
        public double MinDiameter { get; internal set; }
        public double MaxDiameter { get; internal set; }
        public int Quantity { get; internal set; }

        /// <summary>Sum of the measured areas of the bars in the group (mm²).</summary>
        public double TotalArea { get; internal set; }

        /// <summary>TotalArea / Quantity (mm²), i.e. computed from measured diameters.</summary>
        public double AreaPerBar => Quantity == 0 ? 0 : TotalArea / Quantity;
    }

    public sealed class RebarResult
    {
        public IReadOnlyList<RebarGroup> Groups { get; internal set; }
        public int TotalCount { get; internal set; }

        /// <summary>Total reinforcement area (mm²).</summary>
        public double As { get; internal set; }

        /// <summary>Gross section area (mm²).</summary>
        public double Ag { get; internal set; }

        public double RhoPercent => Ag > 0 ? 100.0 * As / Ag : double.NaN;

        public double? TargetRhoPercent { get; internal set; }
        public double? AsTarget => TargetRhoPercent.HasValue ? TargetRhoPercent.Value * Ag / 100.0 : (double?)null;
        public double? AreaDifference => AsTarget.HasValue ? As - AsTarget.Value : (double?)null;

        /// <summary>Source ids that were supplied more than once and counted only once.</summary>
        public IReadOnlyList<string> DuplicateIds { get; internal set; }
    }
}
