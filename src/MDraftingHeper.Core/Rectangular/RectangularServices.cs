using System;
using System.Collections.Generic;
using System.Linq;
using MDraftingHeper.Core.Geometry;
using MDraftingHeper.Core.Hooks;
using MDraftingHeper.Core.Validation;

namespace MDraftingHeper.Core.Rectangular
{
    public static class FaceRebarGenerator
    {
        public static ValidationResult Generate(RectangularSectionModel section, out List<LongitudinalBar> bars)
        {
            bars = new List<LongitudinalBar>();
            var v = RectangularValidator.ValidateSection(section.Width, section.Height, section.Cover);
            var p = section.FaceParameters;
            if (p.BarsOnBFace < 2) v.Error("Bars per B face must be at least 2 (the two corner bars).");
            if (p.BarsOnHFace < 2) v.Error("Bars per H face must be at least 2 (the two corner bars).");
            if (!(p.BFaceDiameter > 0) || !(p.HFaceDiameter > 0)) v.Error("Longitudinal bar diameters must be positive.");
            var tie = TieBarSize.Find(section.AutoTieSettings.TieSizeName);
            if (tie == null) v.Error("Select a supported tie size.");
            if (!v.IsValid) return v;

            double td = tie.Diameter;
            double cd = Math.Max(p.BFaceDiameter, p.HFaceDiameter);
            double cx = section.Width / 2 - section.Cover - td - cd / 2;
            double cy = section.Height / 2 - section.Cover - td - cd / 2;
            if (!(cx > 0) || !(cy > 0))
            {
                v.Error("The cover, tie and corner bars do not fit inside the section.");
                return v;
            }

            bars.Add(new LongitudinalBar("C-TL", cd, new Point2(-cx, cy), FaceRole.Top | FaceRole.Left));
            bars.Add(new LongitudinalBar("C-TR", cd, new Point2(cx, cy), FaceRole.Top | FaceRole.Right));
            bars.Add(new LongitudinalBar("C-BL", cd, new Point2(-cx, -cy), FaceRole.Bottom | FaceRole.Left));
            bars.Add(new LongitudinalBar("C-BR", cd, new Point2(cx, -cy), FaceRole.Bottom | FaceRole.Right));

            AddHorizontal(bars, "T", p.BarsOnBFace, p.BFaceDiameter,
                section.Height / 2 - section.Cover - td - p.BFaceDiameter / 2, cx, FaceRole.Top);
            AddHorizontal(bars, "B", p.BarsOnBFace, p.BFaceDiameter,
                -section.Height / 2 + section.Cover + td + p.BFaceDiameter / 2, cx, FaceRole.Bottom);
            AddVertical(bars, "L", p.BarsOnHFace, p.HFaceDiameter,
                -section.Width / 2 + section.Cover + td + p.HFaceDiameter / 2, cy, FaceRole.Left);
            AddVertical(bars, "R", p.BarsOnHFace, p.HFaceDiameter,
                section.Width / 2 - section.Cover - td - p.HFaceDiameter / 2, cy, FaceRole.Right);

            v.Merge(RectangularValidator.ValidateBars(section.Width, section.Height, bars));
            double r = cd / 2 + td / 2;
            double hx = section.Width / 2 - section.Cover - td / 2;
            double hy = section.Height / 2 - section.Cover - td / 2;
            v.Require(hx > r && hy > r, "The rounded outer tie corners do not fit the section and cover envelope.");
            return v;
        }

        private static void AddHorizontal(List<LongitudinalBar> bars, string prefix, int count, double d, double y, double cornerX, FaceRole role)
        {
            for (int i = 1; i < count - 1; i++)
            {
                double x = -cornerX + 2 * cornerX * i / (count - 1.0);
                bars.Add(new LongitudinalBar(prefix + i, d, new Point2(x, y), role));
            }
        }

        private static void AddVertical(List<LongitudinalBar> bars, string prefix, int count, double d, double x, double cornerY, FaceRole role)
        {
            for (int i = 1; i < count - 1; i++)
            {
                double y = cornerY - 2 * cornerY * i / (count - 1.0);
                bars.Add(new LongitudinalBar(prefix + i, d, new Point2(x, y), role));
            }
        }
    }

    public static class RectangularValidator
    {
        public static ValidationResult ValidateSection(double width, double height, double cover)
        {
            var v = new ValidationResult();
            v.Require(width > 0, "Width B must be positive.");
            v.Require(height > 0, "Height H must be positive.");
            v.Require(cover >= 0, "Concrete cover cannot be negative.");
            v.Require(width > 0 && height > 0 && 2 * cover < Math.Min(width, height), "Concrete cover leaves no usable section interior.");
            return v;
        }

        public static ValidationResult ValidateBars(double width, double height, IEnumerable<LongitudinalBar> source)
        {
            var v = new ValidationResult();
            var bars = source == null ? new List<LongitudinalBar>() : source.ToList();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var b in bars)
            {
                string name = (b.BarName ?? "").Trim();
                if (name.Length == 0) v.Error("Every longitudinal bar needs a name.");
                else if (!names.Add(name)) v.Error("Bar Name '" + name + "' is duplicated (names are case-insensitive).");
                if (!(b.Diameter > 0)) v.Error("Bar '" + (name.Length == 0 ? "(unnamed)" : name) + "' needs a positive diameter.");
                if (Math.Abs(b.Center.X) + b.Diameter / 2 > width / 2 + 1e-9 || Math.Abs(b.Center.Y) + b.Diameter / 2 > height / 2 + 1e-9)
                    v.Error("Bar '" + name + "' lies outside the concrete boundary.");
            }
            for (int i = 0; i < bars.Count; i++)
                for (int j = i + 1; j < bars.Count; j++)
                    if (bars[i].Center.DistanceTo(bars[j].Center) < (bars[i].Diameter + bars[j].Diameter) / 2 - 1e-6)
                        v.Error("Bars '" + bars[i].BarName + "' and '" + bars[j].BarName + "' overlap.");
            return v;
        }
    }

    public static class LayoutChangeCoordinator
    {
        public static ValidationResult CommitFaceLayout(RectangularSectionModel section, bool generateAutoTies)
        {
            var v = FaceRebarGenerator.Generate(section, out var bars);
            if (!v.IsValid) return v;
            section.ReinforcementMode = ReinforcementMode.SpecifyByFace;
            CommitGeometry(section, bars);
            if (generateAutoTies && section.AutoTieSettings.Enabled)
            {
                var generated = RestraintArrangementService.Generate(section);
                v.Merge(generated.Validation);
                if (generated.Validation.IsValid)
                {
                    section.TieBars.AddRange(generated.Ties);
                    section.TiesRequireDefinition = false;
                }
            }
            return v;
        }

        public static ValidationResult CommitCustomLayout(RectangularSectionModel section, IEnumerable<LongitudinalBar> bars)
        {
            var list = bars.ToList();
            var v = RectangularValidator.ValidateSection(section.Width, section.Height, section.Cover);
            v.Merge(RectangularValidator.ValidateBars(section.Width, section.Height, list));
            var tie = TieBarSize.Find(section.AutoTieSettings.TieSizeName);
            if (tie == null) v.Error("Select a supported tie size.");
            else foreach (var b in list)
                if (Math.Abs(b.Center.X) + b.Diameter / 2 + tie.Diameter > section.Width / 2 - section.Cover + 1e-9 ||
                    Math.Abs(b.Center.Y) + b.Diameter / 2 + tie.Diameter > section.Height / 2 - section.Cover + 1e-9)
                    v.Error("Bar '" + b.BarName + "' does not fit inside the cover/tie envelope.");
            if (!v.IsValid) return v;
            section.ReinforcementMode = ReinforcementMode.CustomCoordinates;
            CommitGeometry(section, list);
            return v;
        }

        public static ValidationResult RenameCustomBar(RectangularSectionModel section, Guid id, string newName)
        {
            var v = new ValidationResult();
            var bar = section.FindBar(id);
            if (bar == null) return v.Error("The selected bar no longer exists.");
            string name = (newName ?? "").Trim();
            if (name.Length == 0) return v.Error("Bar Name cannot be blank.");
            if (section.LongitudinalBars.Any(b => b.BarId != id && string.Equals(b.BarName.Trim(), name, StringComparison.OrdinalIgnoreCase)))
                return v.Error("Bar Name '" + name + "' is already in use.");
            bar.BarName = name;
            return v;
        }

        public static void InvalidateTies(RectangularSectionModel section)
        {
            section.TieBars.Clear();
            section.TiesRequireDefinition = true;
        }

        private static void CommitGeometry(RectangularSectionModel section, List<LongitudinalBar> bars)
        {
            section.LongitudinalBars.Clear();
            section.LongitudinalBars.AddRange(bars);
            section.LayoutRevision++;
            InvalidateTies(section);
        }
    }

    public sealed class ArrangementResult
    {
        public ValidationResult Validation { get; } = new ValidationResult();
        public List<TieBar> Ties { get; } = new List<TieBar>();
    }

    public static class RestraintArrangementService
    {
        public static ArrangementResult Generate(RectangularSectionModel section)
        {
            var r = new ArrangementResult();
            if (section.ReinforcementMode != ReinforcementMode.SpecifyByFace)
            {
                r.Validation.Error("Automatic tie arrangement is available only in Specify by Face mode.");
                return r;
            }
            if (!(section.AutoTieSettings.DistanceLimit > 0))
            {
                r.Validation.Error("The restraint distance limit must be positive.");
                return r;
            }
            var corners = new[] { "C-TL", "C-TR", "C-BR", "C-BL" }
                .Select(n => section.LongitudinalBars.FirstOrDefault(b => b.BarName == n)).ToList();
            if (corners.Any(b => b == null))
            {
                r.Validation.Error("The generated corner bars are incomplete.");
                return r;
            }
            var outer = NewTie(section, RectangularTieKind.OuterHoop, TieSource.Auto);
            outer.OrderedSupportBarIds.AddRange(corners.Select(b => b.BarId));
            outer.DirectlyRestrainedBarIds.AddRange(corners.Select(b => b.BarId));
            r.Ties.Add(outer);

            AddOpposingLinks(section, r.Ties, RectangularFace.Top, RectangularFace.Bottom, horizontalFace: true);
            AddOpposingLinks(section, r.Ties, RectangularFace.Left, RectangularFace.Right, horizontalFace: false);
            for (int i = 0; i < r.Ties.Count; i++) r.Ties[i].TieName = "ST" + (i + 1);
            foreach (var tie in r.Ties)
            {
                var geometry = RectangularTieGeometryBuilder.Build(section, tie);
                if (!geometry.Validation.IsValid)
                    foreach (var error in geometry.Validation.Errors) r.Validation.Error(tie.TieName + ": " + error);
                foreach (var warning in geometry.Validation.Warnings) r.Validation.Warn(tie.TieName + ": " + warning);
            }
            var audit = RestraintAuditService.Audit(section, r.Ties);
            r.Validation.Merge(audit.Validation);
            return r;
        }

        private static void AddOpposingLinks(RectangularSectionModel s, List<TieBar> ties, RectangularFace aFace, RectangularFace bFace, bool horizontalFace)
        {
            var a = FaceBars(s, aFace, horizontalFace).ToList();
            var b = FaceBars(s, bFace, horizontalFace).ToList();
            var directA = new List<LongitudinalBar> { a.First(), a.Last() };
            var directB = new List<LongitudinalBar> { b.First(), b.Last() };
            int n = Math.Min(a.Count, b.Count);
            for (int i = 1; i < n - 1; i++)
            {
                int j = n - 1 - i;
                if (i > j) break;
                bool needI = !WithinLimit(a[i], directA, s.AutoTieSettings) || !WithinLimit(b[i], directB, s.AutoTieSettings);
                bool needJ = !WithinLimit(a[j], directA, s.AutoTieSettings) || !WithinLimit(b[j], directB, s.AutoTieSettings);
                if (!needI && !needJ) continue;
                AddLink(i);
                if (j != i) AddLink(j); // preserve symmetry; merging is deliberately not assumed valid
            }

            void AddLink(int index)
            {
                var tie = NewTie(s, RectangularTieKind.CrossTie, TieSource.Auto);
                tie.BarAId = a[index].BarId;
                tie.BarBId = b[index].BarId;
                tie.DirectlyRestrainedBarIds.Add(a[index].BarId);
                tie.DirectlyRestrainedBarIds.Add(b[index].BarId);
                ties.Add(tie);
                directA.Add(a[index]); directB.Add(b[index]);
            }
        }

        private static IEnumerable<LongitudinalBar> FaceBars(RectangularSectionModel s, RectangularFace face, bool horizontal)
        {
            var q = s.LongitudinalBars.Where(b => b.IsOn(face));
            return horizontal ? q.OrderBy(b => b.Center.X) : q.OrderByDescending(b => b.Center.Y);
        }

        internal static bool WithinLimit(LongitudinalBar bar, IEnumerable<LongitudinalBar> directlyRestrained, AutoTieSettings settings)
        {
            return directlyRestrained.Any(d => FaceDistance(bar, d, settings.DistanceConvention) <= settings.DistanceLimit + 1e-9);
        }

        public static double FaceDistance(LongitudinalBar a, LongitudinalBar b, RestraintDistanceConvention convention)
        {
            double centre = Math.Max(Math.Abs(a.Center.X - b.Center.X), Math.Abs(a.Center.Y - b.Center.Y));
            return convention == RestraintDistanceConvention.ClearDistance
                ? Math.Max(0, centre - a.Diameter / 2 - b.Diameter / 2)
                : centre;
        }

        private static TieBar NewTie(RectangularSectionModel s, RectangularTieKind kind, TieSource source)
        {
            var a = s.AutoTieSettings;
            return new TieBar
            {
                Kind = kind, Source = source, TieSizeName = a.TieSizeName, HookA = a.HookA, HookB = a.HookB,
                Flip = a.Flip, PresetId = a.PresetId, ClearGap = a.ClearGap,
                InsideMandrelDiameter = a.InsideMandrelDiameter, TailLength = a.TailLength,
                LayoutRevision = s.LayoutRevision
            };
        }
    }

    public sealed class RestraintAuditResult
    {
        public ValidationResult Validation { get; } = new ValidationResult();
        public List<string> UnrestrainedNames { get; } = new List<string>();
    }

    public static class RestraintAuditService
    {
        public static RestraintAuditResult Audit(RectangularSectionModel section, IEnumerable<TieBar> ties = null)
        {
            var r = new RestraintAuditResult();
            var actual = (ties ?? section.TieBars).ToList();
            var ids = new HashSet<Guid>(section.LongitudinalBars.Select(b => b.BarId));
            var direct = new HashSet<Guid>();
            foreach (var t in actual)
            {
                if (t.LayoutRevision != section.LayoutRevision) { r.Validation.Error("Tie '" + t.TieName + "' uses a stale layout revision."); continue; }
                var refs = t.DirectlyRestrainedBarIds.Concat(t.OrderedSupportBarIds)
                    .Concat(t.BarAId.HasValue ? new[] { t.BarAId.Value } : new Guid[0])
                    .Concat(t.BarBId.HasValue ? new[] { t.BarBId.Value } : new Guid[0]);
                if (refs.Any(id => !ids.Contains(id))) r.Validation.Error("Tie '" + t.TieName + "' references a missing longitudinal bar.");
                foreach (var id in t.DirectlyRestrainedBarIds) if (ids.Contains(id)) direct.Add(id);
            }
            foreach (var bar in section.LongitudinalBars)
            {
                if (direct.Contains(bar.BarId)) continue;
                bool adequate = Enum.GetValues(typeof(RectangularFace)).Cast<RectangularFace>()
                    .Where(bar.IsOn)
                    .Any(face => section.LongitudinalBars.Where(b => b.IsOn(face) && direct.Contains(b.BarId))
                        .Any(d => RestraintArrangementService.FaceDistance(bar, d, section.AutoTieSettings.DistanceConvention) <= section.AutoTieSettings.DistanceLimit + 1e-9));
                if (!adequate) r.UnrestrainedNames.Add(bar.BarName);
            }
            if (r.UnrestrainedNames.Count > 0)
                r.Validation.Warn("Incomplete restraint: " + string.Join(", ", r.UnrestrainedNames) + ".");
            return r;
        }
    }
}
