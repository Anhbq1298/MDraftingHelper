using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using MDraftingHeper.Core.Geometry;
using MDraftingHeper.Core.Hooks;
using MDraftingHeper.Core.Rectangular;
using MDraftingHeper.Core.Validation;
using MDraftingHeper.UI.Preview;

namespace MDraftingHeper.UI.ViewModels
{
    public sealed class LongitudinalBarRow
    {
        public LongitudinalBarRow(LongitudinalBar bar) { Bar = bar; }
        public LongitudinalBar Bar { get; }
        public Guid BarId => Bar.BarId;
        public string BarName => Bar.BarName;
        public string Diameter => Bar.Diameter.ToString("0.##", CultureInfo.InvariantCulture);
        public string X => Bar.Center.X.ToString("0.##", CultureInfo.InvariantCulture);
        public string Y => Bar.Center.Y.ToString("0.##", CultureInfo.InvariantCulture);
        public string Face => Bar.FaceRole == FaceRole.None ? "" : Bar.FaceRole.ToString().Replace(",", " /");
        public override string ToString() => BarName + "  (Ø" + Diameter + ", " + X + ", " + Y + ")";
    }

    public sealed class TieBarRow
    {
        public TieBarRow(TieBar tie, RectangularSectionModel section)
        {
            Tie = tie;
            string Name(Guid id) => section.FindBar(id)?.BarName ?? "[missing]";
            BarA = tie.BarAId.HasValue ? Name(tie.BarAId.Value) : string.Join(", ", tie.OrderedSupportBarIds.Select(Name));
            BarB = tie.BarBId.HasValue ? Name(tie.BarBId.Value) : "—";
        }
        public TieBar Tie { get; }
        public string TieName => Tie.TieName;
        public string Source => Tie.Source.ToString();
        public string Type => Tie.Kind == RectangularTieKind.CrossTie ? "Cross-tie" : Tie.Kind == RectangularTieKind.OuterHoop ? "Outer hoop" : "Closed hoop";
        public string BarA { get; }
        public string BarB { get; }
        public string Diameter => Tie.TieSizeName;
        public string HookA => (int)Tie.HookA + "°";
        public string HookB => (int)Tie.HookB + "°";
    }

    public sealed class RectangularViewModel : DialogViewModel
    {
        private readonly RectangularSectionModel _section = new RectangularSectionModel();
        private string _width = "500", _height = "500", _cover = "40";
        private string _nB = "5", _dB = "20", _nH = "4", _dH = "20", _limit = "150";
        private string _message;
        private LongitudinalBarRow _selectedBar;
        private TieBarRow _selectedTie;
        private bool _showAxes = true, _showDimensions = true;

        public RectangularViewModel()
        {
            ApplyLayoutCommand = new RelayCommand(ApplyLayout);
            GenerateTiesCommand = new RelayCommand(GenerateTies, () => IsFaceMode);
            DeleteBarCommand = new RelayCommand(DeleteSelectedBar, () => IsCustomMode && SelectedBar != null);
            DeleteTieCommand = new RelayCommand(DeleteSelectedTie, () => SelectedTie != null);
            DrawCommand = new RelayCommand(() => Finish(DialogAction.Commit), () => CanDraw);
            CancelCommand = new RelayCommand(() => Finish(DialogAction.Close));
            ApplyFaceInputs();
            LayoutChangeCoordinator.CommitFaceLayout(_section, true);
            RefreshAll("Ready. Centre-to-centre restraint limit is a drafting assumption, not a verified code interpretation.");
        }

        public RectangularSectionModel Section => _section;
        public ObservableCollection<LongitudinalBarRow> Bars { get; } = new ObservableCollection<LongitudinalBarRow>();
        public ObservableCollection<TieBarRow> Ties { get; } = new ObservableCollection<TieBarRow>();
        public IList<TieBarSize> TieSizes => TieBarSize.Standard.ToList();
        public IList<HookAngle> HookAngles { get; } = new[] { HookAngle.Deg90, HookAngle.Deg135, HookAngle.Deg180 };
        public IList<RestraintDistanceConvention> DistanceConventions { get; } = new[] { RestraintDistanceConvention.CentreToCentre, RestraintDistanceConvention.ClearDistance };

        public ICommand ApplyLayoutCommand { get; }
        public ICommand GenerateTiesCommand { get; }
        public ICommand DeleteBarCommand { get; }
        public ICommand DeleteTieCommand { get; }
        public ICommand DrawCommand { get; }
        public ICommand CancelCommand { get; }

        public string WidthText { get => _width; set => Set(ref _width, value); }
        public string HeightText { get => _height; set => Set(ref _height, value); }
        public string CoverText { get => _cover; set => Set(ref _cover, value); }
        public string BarsOnBText { get => _nB; set => Set(ref _nB, value); }
        public string BDiameterText { get => _dB; set => Set(ref _dB, value); }
        public string BarsOnHText { get => _nH; set => Set(ref _nH, value); }
        public string HDiameterText { get => _dH; set => Set(ref _dH, value); }
        public string LimitText { get => _limit; set => Set(ref _limit, value); }

        public bool IsFaceMode
        {
            get => _section.ReinforcementMode == ReinforcementMode.SpecifyByFace;
            set { if (value && !IsFaceMode) SwitchMode(ReinforcementMode.SpecifyByFace); }
        }
        public bool IsCustomMode
        {
            get => _section.ReinforcementMode == ReinforcementMode.CustomCoordinates;
            set { if (value && !IsCustomMode) SwitchMode(ReinforcementMode.CustomCoordinates); }
        }

        public bool AutoTiesEnabled
        {
            get => _section.AutoTieSettings.Enabled;
            set { if (_section.AutoTieSettings.Enabled != value) { _section.AutoTieSettings.Enabled = value; Raise(); } }
        }
        public TieBarSize AutoTieSize
        {
            get => TieBarSize.Find(_section.AutoTieSettings.TieSizeName);
            set { if (value != null && value.Name != _section.AutoTieSettings.TieSizeName) { _section.AutoTieSettings.TieSizeName = value.Name; Raise(); } }
        }
        public RestraintDistanceConvention DistanceConvention
        {
            get => _section.AutoTieSettings.DistanceConvention;
            set { if (value != _section.AutoTieSettings.DistanceConvention) { _section.AutoTieSettings.DistanceConvention = value; Raise(); } }
        }
        public HookAngle AutoHook
        {
            get => _section.AutoTieSettings.HookA;
            set { _section.AutoTieSettings.HookA = value; _section.AutoTieSettings.HookB = value; Raise(); }
        }

        public LongitudinalBarRow SelectedBar
        {
            get => _selectedBar;
            set { if (Set(ref _selectedBar, value)) Raise(nameof(Preview)); }
        }
        public TieBarRow SelectedTie
        {
            get => _selectedTie;
            set { if (Set(ref _selectedTie, value)) Raise(nameof(Preview)); }
        }
        public bool ShowAxes { get => _showAxes; set { if (Set(ref _showAxes, value)) Raise(nameof(Preview)); } }
        public bool ShowDimensions { get => _showDimensions; set { if (Set(ref _showDimensions, value)) Raise(nameof(Preview)); } }
        public string Message { get => _message; private set => Set(ref _message, value); }
        public string Summary => Bars.Count + " bars   As = " + _section.SteelArea.ToString("#,##0", CultureInfo.InvariantCulture) +
                                 " mm²   ρ = " + _section.RhoPercent.ToString("0.###", CultureInfo.InvariantCulture) + "%";
        public bool CanDraw => ValidateFinal().IsValid;

        public IEnumerable<PreviewShape> Preview
        {
            get
            {
                var result = new List<PreviewShape>();
                double hx = _section.Width / 2, hy = _section.Height / 2;
                result.Add(new PreviewShape(new Path2(new[]
                {
                    new PathVertex(new Point2(-hx, -hy)), new PathVertex(new Point2(hx, -hy)),
                    new PathVertex(new Point2(hx, hy)), new PathVertex(new Point2(-hx, hy))
                }, true), PreviewStyle.Section));
                if (_showAxes)
                {
                    result.Add(new PreviewShape(PreviewShapes.Line(new Point2(-hx, 0), new Point2(hx, 0)), PreviewStyle.Axis));
                    result.Add(new PreviewShape(PreviewShapes.Line(new Point2(0, -hy), new Point2(0, hy)), PreviewStyle.Axis));
                }
                if (_showDimensions)
                {
                    result.Add(new PreviewShape(PreviewShapes.Line(new Point2(-hx, -hy - 25), new Point2(hx, -hy - 25)),
                        PreviewStyle.Axis, "B = " + _section.Width.ToString("0.##", CultureInfo.InvariantCulture) + " mm", new Point2(0, -hy - 25)));
                    result.Add(new PreviewShape(PreviewShapes.Line(new Point2(-hx - 25, -hy), new Point2(-hx - 25, hy)),
                        PreviewStyle.Axis, "H = " + _section.Height.ToString("0.##", CultureInfo.InvariantCulture) + " mm", new Point2(-hx - 25, 0)));
                }
                foreach (var b in _section.LongitudinalBars)
                    result.Add(new PreviewShape(PreviewShapes.Circle(b.Center, b.Diameter / 2),
                        _selectedBar?.BarId == b.BarId ? PreviewStyle.Selected : PreviewStyle.Bar, b.BarName, b.Center));
                foreach (var t in _section.TieBars)
                {
                    var g = RectangularTieGeometryBuilder.Build(_section, t);
                    foreach (var p in g.Paths)
                        result.Add(new PreviewShape(p, _selectedTie?.Tie.TieId == t.TieId ? PreviewStyle.Selected : PreviewStyle.Tie));
                }
                return result;
            }
        }

        public ValidationResult AddCustomBar(string name, double diameter, double x, double y) =>
            CommitCustomEdit(null, name, diameter, x, y);

        public ValidationResult EditCustomBar(Guid id, string name, double diameter, double x, double y)
        {
            var old = _section.FindBar(id);
            if (old != null && Math.Abs(old.Diameter - diameter) < 1e-9 && old.Center.IsEqualTo(new Point2(x, y), 1e-9))
            {
                var rename = LayoutChangeCoordinator.RenameCustomBar(_section, id, name);
                RefreshAll(rename.IsValid ? "Bar renamed; tie identities and connectivity were preserved." : rename.Summary);
                return rename;
            }
            return CommitCustomEdit(id, name, diameter, x, y);
        }

        public ValidationResult CommitCrossTie(TieBar existing, Guid barA, Guid barB, string sizeName,
            HookAngle hookA, HookAngle hookB, bool flip, double mandrel, double tail, double gap)
        {
            var v = new ValidationResult();
            if (barA == barB) return v.Error("Bar A and Bar B must be different.");
            if (_section.FindBar(barA) == null || _section.FindBar(barB) == null) return v.Error("Select two current longitudinal bars.");
            var draft = new TieBar(existing?.TieId)
            {
                TieName = existing?.TieName ?? "ST" + (_section.TieBars.Count + 1), Source = existing == null ? TieSource.Manual : TieSource.AutoModified,
                Kind = RectangularTieKind.CrossTie, BarAId = barA, BarBId = barB, TieSizeName = sizeName,
                HookA = hookA, HookB = hookB, Flip = flip, InsideMandrelDiameter = mandrel,
                TailLength = tail, ClearGap = gap, LayoutRevision = _section.LayoutRevision
            };
            draft.DirectlyRestrainedBarIds.Add(barA); draft.DirectlyRestrainedBarIds.Add(barB);
            var geometry = RectangularTieGeometryBuilder.Build(_section, draft);
            v.Merge(geometry.Validation);
            if (!v.IsValid) return v;
            if (existing != null) _section.TieBars.Remove(existing);
            _section.TieBars.Add(draft);
            RenumberTies();
            RefreshAll(existing == null ? "Manual stirrup added." : "Tie updated and marked AutoModified.");
            return v;
        }

        public ValidationResult CommitHoopProperties(TieBar existing, string sizeName, HookAngle hookA,
            HookAngle hookB, bool flip, double mandrel, double tail, double gap)
        {
            var v = new ValidationResult();
            if (existing == null || existing.Kind == RectangularTieKind.CrossTie)
                return v.Error("The selected row is not a hoop.");
            var draft = new TieBar(existing.TieId)
            {
                TieName = existing.TieName, Source = TieSource.AutoModified, Kind = existing.Kind,
                TieSizeName = sizeName, HookA = hookA, HookB = hookB, Flip = flip,
                InsideMandrelDiameter = mandrel, TailLength = tail, ClearGap = gap,
                LayoutRevision = _section.LayoutRevision
            };
            draft.OrderedSupportBarIds.AddRange(existing.OrderedSupportBarIds);
            draft.DirectlyRestrainedBarIds.AddRange(existing.DirectlyRestrainedBarIds);
            var geometry = RectangularTieGeometryBuilder.Build(_section, draft);
            v.Merge(geometry.Validation);
            if (!v.IsValid) return v;
            int at = _section.TieBars.IndexOf(existing);
            _section.TieBars[at] = draft;
            RefreshAll("Hoop properties updated and marked AutoModified.");
            return v;
        }

        public ValidationResult ValidateFinal()
        {
            var v = RectangularValidator.ValidateSection(_section.Width, _section.Height, _section.Cover);
            v.Merge(RectangularValidator.ValidateBars(_section.Width, _section.Height, _section.LongitudinalBars));
            if (_section.LongitudinalBars.Count < 2) v.Error("Define at least two longitudinal bars before drawing.");
            foreach (var t in _section.TieBars) v.Merge(RectangularTieGeometryBuilder.Build(_section, t).Validation);
            v.Merge(RestraintAuditService.Audit(_section).Validation);
            return v;
        }

        private void ApplyLayout()
        {
            if (!ReadGeometry(out var why)) { Message = why; return; }
            ValidationResult v;
            if (IsFaceMode)
            {
                if (!ApplyFaceInputs()) return;
                v = LayoutChangeCoordinator.CommitFaceLayout(_section, _section.AutoTieSettings.Enabled);
            }
            else v = LayoutChangeCoordinator.CommitCustomLayout(_section, _section.LongitudinalBars.ToList());
            RefreshAll(v.IsValid ? "Layout committed at revision " + _section.LayoutRevision + "; previous ties were invalidated." : v.Summary);
        }

        private void GenerateTies()
        {
            if (!IsFaceMode) { Message = "Automatic tie generation is unavailable in Custom Coordinates mode."; return; }
            if (!ReadAutoSettings(out var why)) { Message = why; return; }
            _section.TieBars.Clear();
            _section.TiesRequireDefinition = true;
            var generated = RestraintArrangementService.Generate(_section);
            if (!generated.Validation.IsValid) { RefreshAll("Auto-tie replacement failed; the old tie list was cleared. " + generated.Validation.Summary); return; }
            _section.TieBars.AddRange(generated.Ties);
            _section.TiesRequireDefinition = false;
            RefreshAll("Auto ties regenerated; the prior tie list was replaced explicitly. " + generated.Validation.Summary);
        }

        private void SwitchMode(ReinforcementMode mode)
        {
            if (!ReadGeometry(out var why)) { Message = why; return; }
            ValidationResult v;
            if (mode == ReinforcementMode.SpecifyByFace)
            {
                if (!ApplyFaceInputs()) return;
                v = LayoutChangeCoordinator.CommitFaceLayout(_section, _section.AutoTieSettings.Enabled);
            }
            else
            {
                // A mode switch is a committed layout change. Start custom mode from no implicit face groups.
                v = LayoutChangeCoordinator.CommitCustomLayout(_section, new LongitudinalBar[0]);
            }
            if (!v.IsValid) { Message = v.Summary; return; }
            Raise(nameof(IsFaceMode)); Raise(nameof(IsCustomMode)); Raise(nameof(AutoTiesEnabled));
            RefreshAll(mode == ReinforcementMode.CustomCoordinates
                ? "Custom Coordinates active. Auto arrangement is disabled; add named bars, then define ties manually."
                : "Specify by Face active. A fresh generated layout and identities were committed.");
        }

        private ValidationResult CommitCustomEdit(Guid? id, string name, double diameter, double x, double y)
        {
            var list = _section.LongitudinalBars.ToList();
            if (id.HasValue) list.RemoveAll(b => b.BarId == id.Value);
            list.Add(new LongitudinalBar((name ?? "").Trim(), diameter, new Point2(x, y), FaceRole.None, id));
            var v = LayoutChangeCoordinator.CommitCustomLayout(_section, list);
            RefreshAll(v.IsValid ? "Longitudinal geometry committed; all prior ties were cleared." : v.Summary);
            return v;
        }

        private void DeleteSelectedBar()
        {
            if (_selectedBar == null) return;
            var list = _section.LongitudinalBars.Where(b => b.BarId != _selectedBar.BarId).ToList();
            var v = LayoutChangeCoordinator.CommitCustomLayout(_section, list);
            RefreshAll(v.IsValid ? "Bar deleted; all prior ties were cleared." : v.Summary);
        }

        private void DeleteSelectedTie()
        {
            if (_selectedTie == null) return;
            _section.TieBars.Remove(_selectedTie.Tie);
            RenumberTies();
            RefreshAll("Tie deleted. " + RestraintAuditService.Audit(_section).Validation.Summary);
        }

        private bool ReadGeometry(out string why)
        {
            why = null;
            if (!TryParse(_width, out var w) || !TryParse(_height, out var h) || !TryParse(_cover, out var c))
            { why = "Enter numeric values for B, H and cover."; return false; }
            _section.Width = w; _section.Height = h; _section.Cover = c;
            return ReadAutoSettings(out why);
        }

        private bool ApplyFaceInputs()
        {
            if (!int.TryParse((_nB ?? "").Trim(), out var nb) || !int.TryParse((_nH ?? "").Trim(), out var nh) ||
                !TryParse(_dB, out var db) || !TryParse(_dH, out var dh))
            { Message = "Enter whole bar counts and numeric longitudinal diameters."; return false; }
            _section.FaceParameters.BarsOnBFace = nb; _section.FaceParameters.BFaceDiameter = db;
            _section.FaceParameters.BarsOnHFace = nh; _section.FaceParameters.HFaceDiameter = dh;
            return true;
        }

        private bool ReadAutoSettings(out string why)
        {
            why = null;
            if (!TryParse(_limit, out var limit)) { why = "Enter a numeric restraint distance limit."; return false; }
            _section.AutoTieSettings.DistanceLimit = limit;
            return true;
        }

        private void RenumberTies()
        {
            for (int i = 0; i < _section.TieBars.Count; i++) _section.TieBars[i].TieName = "ST" + (i + 1);
        }

        private void RefreshAll(string status)
        {
            var barId = _selectedBar?.BarId;
            var tieId = _selectedTie?.Tie.TieId;
            Bars.Clear(); foreach (var b in _section.LongitudinalBars) Bars.Add(new LongitudinalBarRow(b));
            Ties.Clear(); foreach (var t in _section.TieBars) Ties.Add(new TieBarRow(t, _section));
            _selectedBar = Bars.FirstOrDefault(b => b.BarId == barId);
            _selectedTie = Ties.FirstOrDefault(t => t.Tie.TieId == tieId);
            Message = status;
            foreach (var n in new[] { nameof(SelectedBar), nameof(SelectedTie), nameof(Summary), nameof(Preview), nameof(CanDraw) }) Raise(n);
            CommandManager.InvalidateRequerySuggested();
        }
    }
}
