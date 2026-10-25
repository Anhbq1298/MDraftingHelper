using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using MDraftingHeper.Core.Geometry;
using MDraftingHeper.Core.Steel;
using MDraftingHeper.UI.Preview;

namespace MDraftingHeper.UI.ViewModels
{
    public sealed class DimensionRow : ViewModelBase
    {
        private string _text;
        public string Key { get; }
        public string Description { get; }
        internal Action Changed;

        public DimensionRow(string key, string description, double value)
        {
            Key = key; Description = description;
            _text = double.IsNaN(value) ? "" : value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        public string Text
        {
            get => _text;
            set { if (Set(ref _text, value)) Changed?.Invoke(); }
        }

        public double Value => TryParse(_text, out var v) ? v : double.NaN;
    }

    /// <summary>One row of the profile list: a library profile, or the "(Custom dimensions)" entry.</summary>
    public sealed class ProfileItem
    {
        public ProfileItem(SteelProfile profile) { Profile = profile; }
        public SteelProfile Profile { get; }
        public bool IsCustom => Profile == null;
        public string Display => Profile == null ? "(Custom dimensions)" : Profile.DisplayName;
        public override string ToString() => Display;
    }

    public sealed class SteelViewModel : DialogViewModel
    {
        private readonly ProfileLibrary _library;
        private SteelFamily _family;
        private string _search = "";
        private SteelProfile _profile;
        private ProfileItem _item;
        private bool _isCustom;
        private bool _suppress;
        private InsertionAnchor _anchor = InsertionAnchor.Centroid;
        private string _rotationText = "0";
        private bool _axes = true, _label = true;

        public SteelViewModel(ProfileLibrary library, string libraryNote)
        {
            _library = library;
            LibraryNote = libraryNote;
            InsertCommand = new RelayCommand(() => Finish(DialogAction.Commit), () => CanInsert);
            CancelCommand = new RelayCommand(() => Finish(DialogAction.Close));
            Family = SteelFamily.UB;
        }

        public ICommand InsertCommand { get; }
        public ICommand CancelCommand { get; }

        public string LibraryNote { get; }
        public IList<SteelFamily> Families { get; } = SteelFamilies.All.ToList();
        public IList<InsertionAnchor> Anchors { get; } = new[] { InsertionAnchor.Centroid, InsertionAnchor.TopLeft, InsertionAnchor.BottomCentre };
        /// <summary>"(Custom dimensions)" first, then the family's library profiles matching the search.</summary>
        public ObservableCollection<ProfileItem> Profiles { get; } = new ObservableCollection<ProfileItem>();
        public ObservableCollection<DimensionRow> Dimensions { get; } = new ObservableCollection<DimensionRow>();

        public SteelFamily Family
        {
            get => _family;
            set
            {
                _family = value;
                Raise();
                _profile = null;
                RefreshProfiles();
                // Default to the first catalogue size (fields locked); Custom only when the family has none.
                SelectedItem = Profiles.FirstOrDefault(i => !i.IsCustom) ?? Profiles[0];
            }
        }

        public string SearchText
        {
            get => _search;
            set { if (Set(ref _search, value)) RefreshProfiles(); }
        }

        /// <summary>
        /// Selecting a library profile loads its dimensions and locks them; selecting "(Custom dimensions)"
        /// unlocks the fields, starting from the values currently shown.
        /// </summary>
        public ProfileItem SelectedItem
        {
            get => _item;
            set
            {
                if (value == null) return;
                _item = value;
                if (value.IsCustom)
                {
                    _isCustom = true;
                    LoadRows(null); // keep the values currently shown as the starting point
                }
                else
                {
                    _profile = value.Profile;
                    // A profile without a published radius cannot be inserted as-is: let the user complete it.
                    _isCustom = value.Profile.RadiusStatus == CornerRadiusStatus.Missing;
                    LoadRows(value.Profile);
                }
                Raise(); Raise(nameof(IsCustom)); Raise(nameof(SelectedProfile));
            }
        }

        /// <summary>The selected library profile, or null while "(Custom dimensions)" is selected.</summary>
        public SteelProfile SelectedProfile => _item != null && !_item.IsCustom ? _profile : null;

        /// <summary>Selects a library profile of the current family by id; false if it is not in the list.</summary>
        public bool SelectProfileById(string id)
        {
            var item = Profiles.FirstOrDefault(i => !i.IsCustom && string.Equals(i.Profile.Id, id, StringComparison.OrdinalIgnoreCase));
            if (item == null) return false;
            SelectedItem = item;
            return true;
        }

        /// <summary>Custom: dimensions are user-entered (fields unlocked) and not tied to a library profile.</summary>
        public bool IsCustom => _isCustom;
        public InsertionAnchor Anchor { get => _anchor; set { if (Set(ref _anchor, value)) Raise(nameof(Preview)); } }
        public string RotationText { get => _rotationText; set { if (Set(ref _rotationText, value)) Update(); } }
        public double RotationDegrees => TryParse(_rotationText, out var r) ? r : double.NaN;
        public bool CentreAxes { get => _axes; set { if (Set(ref _axes, value)) Raise(nameof(Preview)); } }
        public bool ProfileLabel { get => _label; set => Set(ref _label, value); }

        public SteelSection Section =>
            new SteelSection(_family, Dimensions.ToDictionary(d => d.Key, d => d.Value));

        /// <summary>The library profile the current dimensions match, or null for Custom.</summary>
        public SteelProfile MatchedProfile => _isCustom ? null : _profile;

        public string LabelText => MatchedProfile != null
            ? MatchedProfile.Name
            : "Custom " + _family + " " + Section.DimensionText();

        public string Provenance
        {
            get
            {
                if (_profile == null || (_isCustom && _item != null && _item.IsCustom)) return "Custom dimensions (not from a library profile).";
                var p = _profile;
                var s = "Source: " + p.Source + (string.IsNullOrEmpty(p.SourceVersion) ? "" : " (" + p.SourceVersion + ")");
                if (!string.IsNullOrEmpty(p.Standard)) s += "  Standard: " + p.Standard;
                if (p.IsDemonstration) s += "  DEMONSTRATION DATA — not for production.";
                if (p.RadiusStatus == CornerRadiusStatus.Assumed) s += "  Radius r is ASSUMED, not published.";
                if (p.RadiusStatus == CornerRadiusStatus.Missing) s += "  Radius r is MISSING from the source: fields unlocked to enter it (inserted as Custom).";
                return s;
            }
        }

        public string Messages
        {
            get
            {
                var msgs = new List<string>();
                var v = SteelValidator.Validate(Section);
                msgs.AddRange(v.Errors);
                if (double.IsNaN(RotationDegrees)) msgs.Add("Rotation must be a number (degrees).");
                return string.Join("\n", msgs);
            }
        }

        public string AreaText
        {
            get
            {
                var s = Section;
                if (!SteelValidator.Validate(s).IsValid) return "—";
                var calc = "Area from drafted geometry: " + Mm2(SectionGeometry.GeometricArea(s));
                if (MatchedProfile?.PublishedAreaMm2 != null) calc += "   Published: " + Mm2(MatchedProfile.PublishedAreaMm2.Value);
                return calc;
            }
        }

        public bool CanInsert => SteelValidator.Validate(Section).IsValid && !double.IsNaN(RotationDegrees);

        public IEnumerable<PreviewShape> Preview
        {
            get
            {
                var s = Section;
                if (!SteelValidator.Validate(s).IsValid) return null;
                var list = SectionGeometry.Build(s).Select(p => new PreviewShape(p, PreviewStyle.Section)).ToList();
                double hw = (_family == SteelFamily.CHS ? s["D"] : s["B"]) / 2 * 1.15;
                double hh = (_family == SteelFamily.CHS ? s["D"] : _family == SteelFamily.SHS ? s["B"] : s["H"]) / 2 * 1.15;
                if (_axes)
                {
                    list.Add(new PreviewShape(PreviewShapes.Line(new Point2(-hw, 0), new Point2(hw, 0)), PreviewStyle.Axis));
                    list.Add(new PreviewShape(PreviewShapes.Line(new Point2(0, -hh), new Point2(0, hh)), PreviewStyle.Axis));
                }
                // Anchor marker: small cross at the insertion point.
                var o = AnchorMath.CentroidOffset(s, _anchor);
                var a = new Point2(-o.X, -o.Y);
                double m = Math.Max(hw, hh) * 0.06;
                list.Add(new PreviewShape(PreviewShapes.Line(new Point2(a.X - m, a.Y - m), new Point2(a.X + m, a.Y + m)), PreviewStyle.Flagged));
                list.Add(new PreviewShape(PreviewShapes.Line(new Point2(a.X - m, a.Y + m), new Point2(a.X + m, a.Y - m)), PreviewStyle.Flagged));
                return list;
            }
        }

        private void RefreshProfiles()
        {
            Profiles.Clear();
            var custom = new ProfileItem(null);
            Profiles.Add(custom);
            foreach (var p in _library.Search(_family, _search)) Profiles.Add(new ProfileItem(p));
            // Keep the current choice selected when it is still listed.
            if (_item == null) return;
            var keep = _item.IsCustom ? custom : Profiles.FirstOrDefault(i => !i.IsCustom && i.Profile == _item.Profile);
            if (keep != null) { _item = keep; Raise(nameof(SelectedItem)); }
        }

        private void LoadRows(SteelProfile p)
        {
            _suppress = true;
            var previous = Dimensions.ToDictionary(d => d.Key, d => d.Value);
            Dimensions.Clear();
            foreach (var key in SteelFamilies.Parameters(_family))
            {
                double v = p != null ? p.Section[key] : (previous.TryGetValue(key, out var old) ? old : double.NaN);
                var row = new DimensionRow(key, SteelFamilies.Describe(key, _family), v);
                row.Changed = OnRowChanged;
                Dimensions.Add(row);
            }
            _suppress = false;
            Update();
        }

        private void OnRowChanged()
        {
            if (!_suppress) Update();
        }
        private void Update()
        {
            foreach (var n in new[] { nameof(Provenance), nameof(Messages), nameof(AreaText), nameof(CanInsert),
                                      nameof(Preview), nameof(LabelText), nameof(MatchedProfile) })
                Raise(n);
        }
    }
}
