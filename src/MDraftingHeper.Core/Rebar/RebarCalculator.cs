using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MDraftingHeper.Core.Rebar
{
    public static class RebarCalculator
    {
        public static double BarArea(double diameter) => Math.PI * diameter * diameter / 4.0;

        /// <summary>
        /// Groups bars by measured diameter and computes As, Ag and rho at full precision.
        /// Duplicate source ids are counted once and reported.
        /// </summary>
        public static RebarResult Calculate(IEnumerable<RebarBar> bars, double grossArea,
                                            RebarGroupingOptions options = null, double? targetRhoPercent = null)
        {
            if (bars == null) throw new ArgumentNullException(nameof(bars));
            if (!(grossArea > 0) || double.IsInfinity(grossArea))
                throw new ArgumentOutOfRangeException(nameof(grossArea), "Gross section area must be positive.");
            if (targetRhoPercent.HasValue && !(targetRhoPercent.Value >= 0))
                throw new ArgumentOutOfRangeException(nameof(targetRhoPercent), "Target rho must be zero or positive.");
            options = options ?? new RebarGroupingOptions();
            if (!(options.DiameterTolerance >= 0))
                throw new ArgumentOutOfRangeException(nameof(options), "Diameter tolerance must be zero or positive.");

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var duplicates = new List<string>();
            var unique = new List<RebarBar>();
            foreach (var b in bars)
            {
                if (!(b.Diameter > 0)) throw new ArgumentException("Bar diameters must be positive.", nameof(bars));
                if (!seen.Add(b.SourceId)) { duplicates.Add(b.SourceId); continue; }
                unique.Add(b);
            }

            var groups = Group(unique, options);
            return new RebarResult
            {
                Groups = groups,
                TotalCount = unique.Count,
                As = unique.Sum(b => BarArea(b.Diameter)),
                Ag = grossArea,
                TargetRhoPercent = targetRhoPercent,
                DuplicateIds = duplicates
            };
        }

        /// <summary>
        /// Sorts by diameter and starts a new group whenever a bar is more than the tolerance
        /// larger than the group's smallest bar, so groups cannot chain across sizes.
        /// </summary>
        internal static List<RebarGroup> Group(List<RebarBar> bars, RebarGroupingOptions options)
        {
            var result = new List<RebarGroup>();
            var sorted = bars.OrderBy(b => b.Diameter).ToList();
            int i = 0;
            while (i < sorted.Count)
            {
                double start = sorted[i].Diameter;
                int j = i;
                while (j < sorted.Count && sorted[j].Diameter - start <= options.DiameterTolerance + 1e-9) j++;
                var members = sorted.GetRange(i, j - i);
                result.Add(MakeGroup(members, options));
                i = j;
            }
            return result;
        }

        private static RebarGroup MakeGroup(List<RebarBar> members, RebarGroupingOptions options)
        {
            var g = new RebarGroup
            {
                Quantity = members.Count,
                MinDiameter = members.Min(m => m.Diameter),
                MaxDiameter = members.Max(m => m.Diameter),
                MeanDiameter = members.Average(m => m.Diameter),
                TotalArea = members.Sum(m => BarArea(m.Diameter))
            };

            // A nominal label applies only when every member is within tolerance of it.
            double? nominal = null;
            foreach (var n in options.NominalDiameters ?? Enumerable.Empty<double>())
            {
                if (Math.Abs(g.MinDiameter - n) <= options.DiameterTolerance + 1e-9 &&
                    Math.Abs(g.MaxDiameter - n) <= options.DiameterTolerance + 1e-9)
                {
                    nominal = n;
                    break;
                }
            }

            g.NominalDiameter = nominal;
            g.IsNominal = nominal.HasValue;
            g.Label = nominal.HasValue
                ? options.NominalPrefix + nominal.Value.ToString("0.##", CultureInfo.InvariantCulture)
                : "Ø" + g.MeanDiameter.ToString("0.##", CultureInfo.InvariantCulture) + " (non-standard)";
            return g;
        }
    }
}
