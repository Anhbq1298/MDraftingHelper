using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using MDraftingHeper.Core.Geometry;
using MDraftingHeper.Core.Rebar;
using MDraftingHeper.UI.Preview;

namespace MDraftingHeper.UI.ViewModels
{
    public sealed class RebarRow
    {
        public string BarSize { get; set; }
        public int Quantity { get; set; }
        public string AreaPerBar { get; set; }
        public string TotalArea { get; set; }
    }

    public sealed class RebarViewModel : ViewModelBase
    {
        private List<(RebarBar bar, BarPlacement placement)> _bars = new List<(RebarBar, BarPlacement)>();
        private SectionBoundary _boundary;
        private RebarResult _result;
        private string _targetRhoText = "";
        private bool _acceptExclusions;
        private double _tolerance = 0.1;

        public RebarViewModel()
        {
            SelectCommand = new RelayCommand(() => Finish(DialogAction.Select));
            InsertTableCommand = new RelayCommand(() => Finish(DialogAction.Commit), () => CanInsert);
            CloseCommand = new RelayCommand(() => Finish(DialogAction.Close));
        }

        public event Action RequestClose;
        public DialogAction Action { get; private set; }

        public ICommand SelectCommand { get; }
        public ICommand InsertTableCommand { get; }
        public ICommand CloseCommand { get; }

        public ObservableCollection<RebarRow> Rows { get; } = new ObservableCollection<RebarRow>();
        public ObservableCollection<string> Issues { get; } = new ObservableCollection<string>();

        public RebarResult Result => _result;
        public bool HasSelection => _boundary != null;
        public string UnitsNote { get; private set; }

        public double DiameterTolerance
        {
            get => _tolerance;
            set { if (value >= 0 && Set(ref _tolerance, value)) Recalculate(); }
        }

        public string TargetRhoText
        {
            get => _targetRhoText;
            set { if (Set(ref _targetRhoText, value)) Recalculate(); }
        }

        public double? TargetRho => TryParse(_targetRhoText, out var v) && v >= 0 ? v : (double?)null;

        public int FlaggedCount => _bars.Count(b => b.placement != BarPlacement.Inside);

        public bool AcceptExclusions
        {
            get => _acceptExclusions;
            set { Set(ref _acceptExclusions, value); Raise(nameof(CanInsert)); }
        }

        public bool CanInsert => _result != null && _result.TotalCount > 0 && (FlaggedCount == 0 || AcceptExclusions)
                                 && (string.IsNullOrWhiteSpace(_targetRhoText) || TargetRho.HasValue);

        public string TotalCountText => _result == null ? "—" : _result.TotalCount.ToString(CultureInfo.InvariantCulture);
        public string AsText => _result == null ? "—" : Mm2(_result.As);
        public string AgText => _boundary == null ? "—" : Mm2(_boundary.Area);
        public string RhoText => _result == null ? "—" : _result.RhoPercent.ToString("0.000", CultureInfo.InvariantCulture) + " %";
        public string AsTargetText => _result?.AsTarget == null ? "—" : Mm2(_result.AsTarget.Value);
        public string DifferenceText => _result?.AreaDifference == null ? "—"
            : (_result.AreaDifference.Value >= 0 ? "+" : "") + Mm2(_result.AreaDifference.Value);

        public IEnumerable<PreviewShape> Preview
        {
            get
            {
                if (_boundary == null) return null;
                var list = new List<PreviewShape> { new PreviewShape(_boundary.Path, PreviewStyle.Boundary) };
                list.AddRange(_bars.Select(b => new PreviewShape(PreviewShapes.Circle(b.bar.Center, b.bar.Diameter / 2),
                    b.placement == BarPlacement.Inside ? PreviewStyle.Bar : PreviewStyle.Flagged)));
                return list;
            }
        }

        /// <summary>Called by the CAD command with geometry already converted to millimetres.</summary>
        public void SetSelection(SectionBoundary boundary, IEnumerable<RebarBar> bars, IEnumerable<string> notes, string unitsNote)
        {
            _boundary = boundary;
            _bars = bars.Select(b => (b, boundary.Classify(b))).ToList();
            AcceptExclusions = false;
            UnitsNote = unitsNote;
            Issues.Clear();
            foreach (var n in notes ?? Enumerable.Empty<string>()) Issues.Add(n);
            int outside = _bars.Count(b => b.placement == BarPlacement.Outside);
            int crossing = _bars.Count(b => b.placement == BarPlacement.Crossing);
            if (outside > 0) Issues.Add(outside + " bar(s) lie outside the boundary and are NOT counted (shown red).");
            if (crossing > 0) Issues.Add(crossing + " bar(s) cross the boundary and are NOT counted (shown red).");
            Recalculate();
            Raise(nameof(UnitsNote));
            Raise(nameof(FlaggedCount));
            Raise(nameof(HasSelection));
        }

        private void Recalculate()
        {
            Rows.Clear();
            _result = null;
            if (_boundary != null)
            {
                var inside = _bars.Where(b => b.placement == BarPlacement.Inside).Select(b => b.bar).ToList();
                if (inside.Count > 0)
                {
                    _result = RebarCalculator.Calculate(inside, _boundary.Area,
                        new RebarGroupingOptions { DiameterTolerance = _tolerance }, TargetRho);
                    foreach (var g in _result.Groups)
                        Rows.Add(new RebarRow
                        {
                            BarSize = g.Label, Quantity = g.Quantity,
                            AreaPerBar = g.AreaPerBar.ToString("0.0", CultureInfo.InvariantCulture),
                            TotalArea = g.TotalArea.ToString("0.0", CultureInfo.InvariantCulture)
                        });
                    if (_result.DuplicateIds.Count > 0)
                        Issues.Add(_result.DuplicateIds.Count + " duplicate selection(s) counted once.");
                }
            }
            foreach (var n in new[] { nameof(TotalCountText), nameof(AsText), nameof(AgText), nameof(RhoText),
                                      nameof(AsTargetText), nameof(DifferenceText), nameof(CanInsert), nameof(Preview), nameof(Result) })
                Raise(n);
        }

        private void Finish(DialogAction a)
        {
            Action = a;
            RequestClose?.Invoke();
        }
    }
}
