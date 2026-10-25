using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using MDraftingHeper.Core.Geometry;
using MDraftingHeper.Core.Hooks;
using MDraftingHeper.UI.Preview;

namespace MDraftingHeper.UI.ViewModels
{
    public enum TieMode { OpenTie, ClosedHoop }

    /// <summary>A hoop corner the closure hooks can sit on (index into the selected bars).</summary>
    public sealed class ClosureCorner
    {
        public int BarIndex { get; }
        public string Label { get; }
        public ClosureCorner(int barIndex, string label) { BarIndex = barIndex; Label = label; }
        public override string ToString() => Label;
    }

    /// <summary>
    /// Tie &amp; Hook: 2D drafting of tie / closed-hoop centrelines around fixed longitudinal bars.
    /// Mandrel and tail lengths come from <see cref="Ec2DraftingRules"/> (drafting rules, not a design check).
    /// </summary>
    public sealed class TieViewModel : DialogViewModel
    {
        private TieMode _mode = TieMode.OpenTie;
        private HookAngle _angleA = HookAngle.Deg135, _angleB = HookAngle.Deg135;
        private TieBarSize _tieSize = TieBarSize.Standard[0];
        private HookEnds _ends = HookEnds.Both;
        private ClosureCorner _closure;
        private bool _flip;
        private string _gapText = "0";
        private bool _hasBars, _hasHoopBars;
        private Point2 _ca, _cb;
        private double _da, _db;
        private List<HoopBar> _hoopBars = new List<HoopBar>();
        private List<ClosureCorner> _corners = new List<ClosureCorner>();
        private TieResult _result;
        private string _inputError;

        public TieViewModel()
        {
            SelectBarsCommand = new RelayCommand(() => Finish(DialogAction.Select));
            DrawCommand = new RelayCommand(() => Finish(DialogAction.Commit), () => CanDraw);
            CancelCommand = new RelayCommand(() => Finish(DialogAction.Close));
            Recalculate();
        }

        public ICommand SelectBarsCommand { get; }
        public ICommand DrawCommand { get; }
        public ICommand CancelCommand { get; }

        public IList<TieMode> Modes { get; } = new[] { TieMode.OpenTie, TieMode.ClosedHoop };
        public IList<HookAngle> AngleOptions { get; } = new[] { HookAngle.Deg90, HookAngle.Deg135, HookAngle.Deg180 };
        public IList<TieBarSize> TieSizes => TieBarSize.Standard.ToList();
        public IList<HookEnds> EndOptions { get; } = new[] { HookEnds.Both, HookEnds.EndA, HookEnds.EndB };
        public IList<ClosureCorner> ClosureCorners => _corners;

        public TieMode Mode
        {
            get => _mode;
            set
            {
                if (!Set(ref _mode, value)) return;
                foreach (var n in new[] { nameof(IsHoop), nameof(IsOpenTie), nameof(SelectButtonText), nameof(BarsText) }) Raise(n);
                Recalculate();
            }
        }

        public bool IsHoop { get => _mode == TieMode.ClosedHoop; set { if (value) Mode = TieMode.ClosedHoop; } }
        public bool IsOpenTie { get => _mode == TieMode.OpenTie; set { if (value) Mode = TieMode.OpenTie; } }
        public string SelectButtonText => IsHoop ? "Select Bars…" : "Select 2 Circles…";

        public HookAngle AngleA { get => _angleA; set { if (Set(ref _angleA, value)) Recalculate(); } }
        public HookAngle AngleB { get => _angleB; set { if (Set(ref _angleB, value)) Recalculate(); } }
        public TieBarSize TieSize { get => _tieSize; set { if (value != null && Set(ref _tieSize, value)) Recalculate(); } }
        public HookEnds Ends { get => _ends; set { if (Set(ref _ends, value)) Recalculate(); } }
        public ClosureCorner Closure { get => _closure; set { if (value != null && Set(ref _closure, value)) Recalculate(); } }
        public bool Flip { get => _flip; set { if (Set(ref _flip, value)) Recalculate(); } }
        public string GapText { get => _gapText; set { if (Set(ref _gapText, value)) Recalculate(); } }

        /// <summary>Automatically calculated by the EC2 drafting rules (read-only in the dialog).</summary>
        public double InsideMandrelDiameter => Ec2DraftingRules.InsideMandrelDiameter(_tieSize.Diameter);
        public double TailLengthA => Ec2DraftingRules.TailLength(_angleA, _tieSize.Diameter);
        public double TailLengthB => Ec2DraftingRules.TailLength(_angleB, _tieSize.Diameter);
        public string MandrelText => Mm(InsideMandrelDiameter, "0.#");
        public string TailAText => Mm(TailLengthA, "0.#");
        public string TailBText => Mm(TailLengthB, "0.#");
        public string RulesNote => "EC2 drafting rules (not a design check): mandrel 4φ up to φ16, 7φ above; tail max(10φ, 70) up to 150°, max(5φ, 50) above.";

        public bool HasBars => IsHoop ? _hasHoopBars : _hasBars;

        public string BarsText
        {
            get
            {
                if (IsHoop)
                {
                    if (!_hasHoopBars) return "No bars selected.";
                    var s = _hoopBars.Count + " bars, largest Ø" + _hoopBars.Max(b => b.Diameter).ToString("0.##", CultureInfo.InvariantCulture) + " mm (governs the bends)";
                    if (_result?.InteriorBars > 0) s += "; " + _result.InteriorBars + " inside the hoop (not wrapped)";
                    return s + ".";
                }
                if (!_hasBars) return "No bars selected.";
                return "A Ø" + _da.ToString("0.##", CultureInfo.InvariantCulture) + ", B Ø" + _db.ToString("0.##", CultureInfo.InvariantCulture) +
                       " mm, spacing " + _ca.DistanceTo(_cb).ToString("0.#", CultureInfo.InvariantCulture) + " mm; Ø" +
                       Math.Max(_da, _db).ToString("0.##", CultureInfo.InvariantCulture) + " governs both bends.";
            }
        }

        public string RadiusText => _result == null || _result.CentrelineRadius <= 0 ? "—"
            : Mm(_result.CentrelineRadius, "0.#") + " centreline bend radius";

        public string LineweightText =>
            "Lineweight " + (_tieSize.LineweightHundredthsMm / 100.0).ToString("0.00", CultureInfo.InvariantCulture) + " mm (graphical only).";

        public string Messages => _inputError ?? (_result == null ? "" : _result.Validation.Summary);
        public bool CanDraw => HasBars && _result?.Path != null;
        public TieResult Result => _result;

        public IEnumerable<PreviewShape> Preview
        {
            get
            {
                if (!HasBars) return null;
                var list = new List<PreviewShape>();
                if (IsHoop) list.AddRange(_hoopBars.Select(b => new PreviewShape(PreviewShapes.Circle(b.Center, b.Diameter / 2), PreviewStyle.Bar)));
                else
                {
                    list.Add(new PreviewShape(PreviewShapes.Circle(_ca, _da / 2), PreviewStyle.Bar));
                    list.Add(new PreviewShape(PreviewShapes.Circle(_cb, _db / 2), PreviewStyle.Bar));
                }
                if (_result?.Path != null) list.Add(new PreviewShape(_result.Path, PreviewStyle.Tie));
                return list;
            }
        }

        /// <summary>Open tie bars in section-plane coordinates (mm), from the CAD selection.</summary>
        public void SetBars(Point2 ca, double da, Point2 cb, double db)
        {
            _ca = ca; _cb = cb; _da = da; _db = db; _hasBars = true;
            Raise(nameof(HasBars)); Raise(nameof(BarsText));
            Recalculate();
        }

        /// <summary>Closed hoop bars in section-plane coordinates (mm). The closure defaults to the bottom-left corner.</summary>
        public void SetHoopBars(IList<HoopBar> bars)
        {
            _hoopBars = bars.ToList();
            _hasHoopBars = _hoopBars.Count > 0;
            _corners = CornerLabels(_hoopBars);
            _closure = _corners.FirstOrDefault();
            Raise(nameof(ClosureCorners)); Raise(nameof(Closure)); Raise(nameof(HasBars)); Raise(nameof(BarsText));
            Recalculate();
        }

        public TieInput BuildInput()
        {
            TryParse(_gapText, out double gap);
            return new TieInput
            {
                CenterA = _ca, CenterB = _cb, DiameterA = _da, DiameterB = _db,
                TieDiameter = _tieSize.Diameter, Angle = _angleA, AngleA = _angleA, AngleB = _angleB,
                Ends = _ends, Flip = _flip, ClearGap = gap,
                InsideMandrelDiameter = InsideMandrelDiameter, TailLengthA = TailLengthA, TailLengthB = TailLengthB
            };
        }

        public HoopInput BuildHoopInput()
        {
            TryParse(_gapText, out double gap);
            return new HoopInput
            {
                Bars = _hoopBars, TieDiameter = _tieSize.Diameter, AngleA = _angleA, AngleB = _angleB,
                ClosureBar = _closure?.BarIndex ?? 0, Flip = _flip, ClearGap = gap,
                InsideMandrelDiameter = InsideMandrelDiameter, TailLengthA = TailLengthA, TailLengthB = TailLengthB
            };
        }

        public void ToggleFlip() => Flip = !Flip;

        private static List<ClosureCorner> CornerLabels(IList<HoopBar> bars)
        {
            if (bars.Count < 3) return new List<ClosureCorner>();
            var hull = TieGeometryBuilder.HoopCorners(bars);
            if (hull.Count < 3) return new List<ClosureCorner>();
            double cx = hull.Average(i => bars[i].Center.X), cy = hull.Average(i => bars[i].Center.Y);
            double sx = hull.Max(i => Math.Abs(bars[i].Center.X - cx)), sy = hull.Max(i => Math.Abs(bars[i].Center.Y - cy));
            string Word(double v, double span, string lo, string hi) => v < -0.3 * span ? lo : v > 0.3 * span ? hi : "";
            return hull.Select((i, n) =>
            {
                var c = bars[i].Center;
                var where = (Word(c.Y - cy, sy, "bottom", "top") + "-" + Word(c.X - cx, sx, "left", "right")).Trim('-');
                return new ClosureCorner(i, "Corner " + (n + 1) + (where.Length > 0 ? " (" + where + ")" : ""));
            }).ToList();
        }

        private void Recalculate()
        {
            _inputError = null;
            _result = null;
            if (!TryParse(_gapText, out double gap) || gap < 0)
                _inputError = "Clear gap must be a number ≥ 0 (mm).";
            else if (IsHoop && _hasHoopBars)
                _result = TieGeometryBuilder.BuildHoop(BuildHoopInput());
            else if (!IsHoop && _hasBars)
                _result = TieGeometryBuilder.Build(BuildInput());
            foreach (var n in new[] { nameof(MandrelText), nameof(TailAText), nameof(TailBText), nameof(RadiusText), nameof(Messages),
                                      nameof(CanDraw), nameof(Preview), nameof(LineweightText), nameof(Result), nameof(BarsText),
                                      nameof(InsideMandrelDiameter), nameof(TailLengthA), nameof(TailLengthB) })
                Raise(n);
        }
    }
}
