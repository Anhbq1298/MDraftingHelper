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
    public sealed class HookAngleOption
    {
        public HookAngle Angle { get; }
        public string Label => (int)Angle + "°";
        public IEnumerable<PreviewShape> Schematic { get; }

        public HookAngleOption(HookAngle angle)
        {
            Angle = angle;
            // Schematic from the real builder with generic bars, so the icon matches the output.
            var r = TieGeometryBuilder.Build(new TieInput
            {
                CenterA = new Point2(0, 0), CenterB = new Point2(160, 0), DiameterA = 25, DiameterB = 25,
                TieDiameter = 10, Angle = angle, Ends = HookEnds.Both, InsideMandrelDiameter = 30, TailLength = 45
            });
            Schematic = new[]
            {
                new PreviewShape(PreviewShapes.Circle(new Point2(0, 0), 12.5), PreviewStyle.Bar),
                new PreviewShape(PreviewShapes.Circle(new Point2(160, 0), 12.5), PreviewStyle.Bar),
                new PreviewShape(r.Path, PreviewStyle.Tie)
            };
        }
    }

    public sealed class TieViewModel : ViewModelBase
    {
        private HookAngleOption _angle;
        private TieBarSize _tieSize;
        private HookEnds _ends = HookEnds.Both;
        private bool _flip;
        private HookPreset _preset;
        private string _mandrelText = "40", _tailText = "100", _gapText = "0";
        private bool _hasBars;
        private Point2 _ca, _cb;
        private double _da, _db;
        private TieResult _result;
        private string _inputError;

        public TieViewModel(IList<HookPreset> presets)
        {
            Presets = presets;
            AngleOptions = new[] { HookAngle.Deg90, HookAngle.Deg135, HookAngle.Deg180 }.Select(a => new HookAngleOption(a)).ToList();
            _angle = AngleOptions[1];
            _tieSize = TieBarSize.Standard[0];
            _preset = presets.FirstOrDefault(p => p.IsManual) ?? presets.First();
            SelectBarsCommand = new RelayCommand(() => Finish(DialogAction.Select));
            DrawCommand = new RelayCommand(() => Finish(DialogAction.Commit), () => CanDraw);
            CancelCommand = new RelayCommand(() => Finish(DialogAction.Close));
            Recalculate();
        }

        public event Action RequestClose;
        public DialogAction Action { get; private set; }

        public ICommand SelectBarsCommand { get; }
        public ICommand DrawCommand { get; }
        public ICommand CancelCommand { get; }

        public IList<HookAngleOption> AngleOptions { get; }
        public IList<TieBarSize> TieSizes => TieBarSize.Standard.ToList();
        public IList<HookEnds> EndOptions { get; } = new[] { HookEnds.Both, HookEnds.EndA, HookEnds.EndB };
        public IList<HookPreset> Presets { get; }

        public HookAngleOption SelectedAngle { get => _angle; set { if (value != null && Set(ref _angle, value)) { ApplyPreset(); Recalculate(); } } }
        public TieBarSize TieSize { get => _tieSize; set { if (value != null && Set(ref _tieSize, value)) { ApplyPreset(); Recalculate(); } } }
        public HookEnds Ends { get => _ends; set { if (Set(ref _ends, value)) Recalculate(); } }
        public bool Flip { get => _flip; set { if (Set(ref _flip, value)) Recalculate(); } }

        public HookPreset Preset
        {
            get => _preset;
            set
            {
                if (value == null || !Set(ref _preset, value)) return;
                ApplyPreset();
                Raise(nameof(IsManual));
                Raise(nameof(PresetProvenance));
                Recalculate();
            }
        }

        public bool IsManual => _preset?.IsManual ?? true;

        public string PresetProvenance
        {
            get
            {
                if (_preset == null) return "";
                var parts = new List<string> { _preset.IsVerified ? "Verified preset." : "NOT verified against authorised code text." };
                if (!string.IsNullOrWhiteSpace(_preset.CodeBasis)) parts.Add("Basis: " + _preset.CodeBasis);
                if (!string.IsNullOrWhiteSpace(_preset.Edition)) parts.Add("Edition: " + _preset.Edition);
                if (!string.IsNullOrWhiteSpace(_preset.Clause)) parts.Add("Clause: " + _preset.Clause);
                if (!string.IsNullOrWhiteSpace(_preset.Scope)) parts.Add("Scope: " + _preset.Scope);
                return string.Join("  ", parts);
            }
        }

        public string MandrelText { get => _mandrelText; set { if (Set(ref _mandrelText, value)) Recalculate(); } }
        public string TailText { get => _tailText; set { if (Set(ref _tailText, value)) Recalculate(); } }
        public string GapText { get => _gapText; set { if (Set(ref _gapText, value)) Recalculate(); } }

        public bool HasBars => _hasBars;
        public string DiameterAText => _hasBars ? Mm(_da, "0.##") : "—";
        public string DiameterBText => _hasBars ? Mm(_db, "0.##") : "—";
        public string EffectiveText => _hasBars ? Mm(Math.Max(_da, _db), "0.##") + "  (max of A and B, used at both hooks)" : "—";
        public string SpacingText => _hasBars ? Mm(_ca.DistanceTo(_cb)) : "—";

        public string FitRadiusText => _result == null || !_hasBars ? "—" : Mm(_result.FitRadius, "0.##");
        public string MandrelRadiusText => _result == null || !_hasBars ? "—" : Mm(_result.MandrelRadius, "0.##");
        public string CentrelineRadiusText => _result == null || !_hasBars ? "—" : Mm(_result.CentrelineRadius, "0.##");
        public string ClearanceText => _result == null || !_hasBars ? "—"
            : "A: " + Mm(_result.ClearanceA, "0.##") + "   B: " + Mm(_result.ClearanceB, "0.##");
        public string LineweightText => _tieSize == null ? "" :
            "Entity lineweight " + (_tieSize.LineweightHundredthsMm / 100.0).ToString("0.00", CultureInfo.InvariantCulture) +
            " mm (graphical only; geometry uses the " + _tieSize.Diameter.ToString(CultureInfo.InvariantCulture) + " mm bar centreline).";

        public string Messages => _inputError ?? (_result == null ? "" : _result.Validation.Summary);
        public bool CanDraw => _hasBars && _result?.Path != null;

        public TieResult Result => _result;

        public IEnumerable<PreviewShape> Preview
        {
            get
            {
                if (!_hasBars) return null;
                var list = new List<PreviewShape>
                {
                    new PreviewShape(PreviewShapes.Circle(_ca, _da / 2), PreviewStyle.Bar),
                    new PreviewShape(PreviewShapes.Circle(_cb, _db / 2), PreviewStyle.Bar),
                    new PreviewShape(PreviewShapes.Line(_ca, _cb), PreviewStyle.Axis)
                };
                if (_result?.Path != null) list.Add(new PreviewShape(_result.Path, PreviewStyle.Tie));
                return list;
            }
        }

        /// <summary>Bars in section-plane coordinates (mm), from the CAD selection.</summary>
        public void SetBars(Point2 ca, double da, Point2 cb, double db)
        {
            _ca = ca; _cb = cb; _da = da; _db = db; _hasBars = true;
            foreach (var n in new[] { nameof(HasBars), nameof(DiameterAText), nameof(DiameterBText), nameof(EffectiveText), nameof(SpacingText) })
                Raise(n);
            Recalculate();
        }

        public TieInput BuildInput()
        {
            TryParse(_mandrelText, out double mandrel);
            TryParse(_tailText, out double tail);
            TryParse(_gapText, out double gap);
            return new TieInput
            {
                CenterA = _ca, CenterB = _cb, DiameterA = _da, DiameterB = _db,
                TieDiameter = _tieSize.Diameter, Angle = _angle.Angle, Ends = _ends, Flip = _flip,
                ClearGap = gap, InsideMandrelDiameter = mandrel, TailLength = tail
            };
        }

        /// <summary>Flips the orientation (also used by the model-space confirmation step).</summary>
        public void ToggleFlip() => Flip = !Flip;

        private void ApplyPreset()
        {
            if (_preset == null || _preset.IsManual || _tieSize == null) return;
            var m = _preset.InsideMandrelDiameter(_tieSize.Diameter);
            var t = _preset.TailLength(_angle.Angle, _tieSize.Diameter);
            if (m.HasValue) { _mandrelText = m.Value.ToString("0.##", CultureInfo.InvariantCulture); Raise(nameof(MandrelText)); }
            if (t.HasValue) { _tailText = t.Value.ToString("0.##", CultureInfo.InvariantCulture); Raise(nameof(TailText)); }
        }

        private void Recalculate()
        {
            _inputError = null;
            _result = null;
            if (!TryParse(_mandrelText, out _) || !TryParse(_tailText, out _) || !TryParse(_gapText, out _))
                _inputError = "Enter numeric values for the mandrel diameter, tail length and clear gap.";
            else if (_hasBars)
                _result = TieGeometryBuilder.Build(BuildInput());
            foreach (var n in new[] { nameof(FitRadiusText), nameof(MandrelRadiusText), nameof(CentrelineRadiusText), nameof(ClearanceText),
                                      nameof(Messages), nameof(CanDraw), nameof(Preview), nameof(LineweightText), nameof(Result) })
                Raise(n);
        }

        private void Finish(DialogAction a)
        {
            Action = a;
            RequestClose?.Invoke();
        }
    }
}
