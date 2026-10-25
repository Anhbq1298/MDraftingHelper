using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MDraftingHeper.Core.Steel;
using Xunit;

namespace MDraftingHeper.Core.Tests
{
    public class SteelTests
    {
        private static SteelSection S(SteelFamily f, params (string k, double v)[] dims) =>
            new SteelSection(f, dims.ToDictionary(d => d.k, d => d.v));

        [Fact]
        public void Chs_RequiresPositiveVoid_AndAreaIsAnnulus()
        {
            Assert.False(SteelValidator.Validate(S(SteelFamily.CHS, ("D", 100), ("t", 50))).IsValid);
            Assert.False(SteelValidator.Validate(S(SteelFamily.CHS, ("D", 100), ("t", 0))).IsValid);
            var s = S(SteelFamily.CHS, ("D", 168.3), ("t", 7.1));
            Assert.True(SteelValidator.Validate(s).IsValid);
            Assert.Equal(Math.PI / 4 * (168.3 * 168.3 - 154.1 * 154.1), SectionGeometry.GeometricArea(s), 6);
        }

        [Theory]
        [InlineData(200, 100, 8, 8)]    // r = t: inner radius zero
        [InlineData(200, 100, 8, 50)]   // 2r = B: no straight side
        [InlineData(200, 100, 50, 60)]  // 2t = B: no void
        public void Rhs_RejectsImpossibleCorners(double h, double b, double t, double r)
        {
            Assert.False(SteelValidator.Validate(S(SteelFamily.RHS, ("H", h), ("B", b), ("t", t), ("r", r))).IsValid);
        }

        [Fact]
        public void HollowRect_AreaIncludesCornerRadiiOnBothLoops()
        {
            double h = 250, b = 150, t = 10, r = 20;
            var s = S(SteelFamily.RHS, ("H", h), ("B", b), ("t", t), ("r", r));
            double outer = b * h - (4 - Math.PI) * r * r;
            double inner = (b - 2 * t) * (h - 2 * t) - (4 - Math.PI) * (r - t) * (r - t);
            Assert.Equal(outer - inner, SectionGeometry.GeometricArea(s), 6);

            var loops = SectionGeometry.Build(S(SteelFamily.SHS, ("B", 150), ("t", 8), ("r", 16)));
            Assert.Equal(2, loops.Count);
            Assert.Equal(150 * 150 - (4 - Math.PI) * 256, loops[0].Area, 6);
        }

        [Fact]
        public void ISection_AreaAndFilletValidity()
        {
            double h = 406.4, b = 177.9, tw = 7.9, tf = 12.8, r = 10.2;
            var s = S(SteelFamily.UB, ("H", h), ("B", b), ("tw", tw), ("tf", tf), ("r", r));
            Assert.True(SteelValidator.Validate(s).IsValid);
            double expected = 2 * b * tf + (h - 2 * tf) * tw + (4 - Math.PI) * r * r;
            Assert.Equal(expected, SectionGeometry.GeometricArea(s), 6);

            Assert.False(SteelValidator.Validate(s.With("r", 90)).IsValid);   // fillet wider than outstand
            Assert.False(SteelValidator.Validate(s.With("tf", 210)).IsValid); // flanges overlap
            Assert.False(SteelValidator.Validate(s.With("tw", 200)).IsValid); // web wider than flange
            Assert.False(SteelValidator.Validate(s.With("r", 200)).IsValid);  // fillets overlap along web
        }

        [Fact]
        public void Anchor_OffsetsAreRelativeToCentroid()
        {
            var s = S(SteelFamily.UB, ("H", 400), ("B", 200), ("tw", 8), ("tf", 12), ("r", 10));
            var tl = AnchorMath.CentroidOffset(s, InsertionAnchor.TopLeft);
            Assert.Equal(100, tl.X); Assert.Equal(-200, tl.Y);
            var bc = AnchorMath.CentroidOffset(s, InsertionAnchor.BottomCentre);
            Assert.Equal(0, bc.X); Assert.Equal(200, bc.Y);
        }

        private static string DataPath(params string[] parts) =>
            Path.Combine(new[] { AppDomain.CurrentDomain.BaseDirectory, "Data" }.Concat(parts).ToArray());

        [Fact]
        public void DemoLibrary_LoadsWithProvenance_AndEveryProfileIsValid()
        {
            var r = ProfileLibraryReader.ReadFile(DataPath("Profiles", "demo-profiles.json"));
            Assert.DoesNotContain(r.Issues, i => i.IsError);
            Assert.Equal(12, r.Profiles.Count);
            Assert.All(r.Profiles, p =>
            {
                Assert.True(p.IsDemonstration);
                Assert.StartsWith("[DEMO]", p.DisplayName);
                Assert.False(string.IsNullOrWhiteSpace(p.Source));
                Assert.True(SteelValidator.Validate(p.Section).IsValid);
            });
            foreach (var f in SteelFamilies.All) Assert.NotEmpty(r.Profiles.Where(p => p.Family == f));
        }

        [Fact]
        public void CsvTemplate_Loads()
        {
            var r = ProfileLibraryReader.ReadFile(DataPath("Profiles", "profile-template.csv"));
            Assert.DoesNotContain(r.Issues, i => i.IsError);
            var p = Assert.Single(r.Profiles);
            Assert.Equal(SteelFamily.RHS, p.Family);
            Assert.Equal(150, p.Section["H"]);
            Assert.Equal(CornerRadiusStatus.Assumed, p.RadiusStatus);
        }

        private const string Header = "id,family,name,units,source,D,B,H,t,tw,tf,r,cornerRadiusConvention\n";

        [Fact]
        public void Import_ConvertsUnits_AndInnerCornerRadius()
        {
            var r = ProfileLibraryReader.ReadCsv(Header +
                "a,CHS,CHS a,cm,src,16.83,,,0.71,,,,\n" +
                "b,RHS,RHS b,mm,src,,100,200,8,,,6,inner\n");
            Assert.DoesNotContain(r.Issues, i => i.IsError);
            Assert.Equal(168.3, r.Profiles[0].Section["D"], 9);
            Assert.Equal(7.1, r.Profiles[0].Section["t"], 9);
            Assert.Equal(14, r.Profiles[1].Section["r"], 9); // inner 6 + t 8 → stored as outer radius
        }

        [Fact]
        public void Import_RejectsDuplicateIds_MissingData_UnknownUnits_AndInvalidGeometry()
        {
            var r = ProfileLibraryReader.ReadCsv(Header +
                "dup,CHS,first,mm,src,100,,,5,,,,\n" +
                "dup,CHS,second,mm,src,120,,,5,,,,\n" +
                "nod,UB,missing tw,mm,src,,100,200,,,8,8,\n" +
                "unit,CHS,feet,ft,src,100,,,5,,,,\n" +
                "bad,CHS,too thick,mm,src,100,,,60,,,,\n" +
                "noconv,SHS,no convention,mm,src,,100,,5,,,8,\n" +
                ",CHS,no id,mm,src,100,,,5,,,,\n" +
                "nosrc,CHS,no source,mm,,100,,,5,,,,\n");
            Assert.Equal(new[] { "dup" }, r.Profiles.Select(p => p.Id).ToArray());
            Assert.Contains(r.Issues, i => i.IsError && i.Message.Contains("Duplicate id"));
            Assert.Contains(r.Issues, i => i.IsError && i.Message.Contains("Missing dimension tw"));
            Assert.Contains(r.Issues, i => i.IsError && i.Message.Contains("units"));
            Assert.Contains(r.Issues, i => i.IsError && i.Message.Contains("2t < D"));
            Assert.Contains(r.Issues, i => i.IsError && i.Message.Contains("cornerRadiusConvention"));
            Assert.Contains(r.Issues, i => i.IsError && i.Message.Contains("stable id"));
            Assert.Contains(r.Issues, i => i.IsError && i.Message.Contains("source"));
        }

        [Fact]
        public void Import_MissingRadius_IsKeptAsMissing_NotInvented()
        {
            var r = ProfileLibraryReader.ReadCsv(Header + "m,UB,no root radius,mm,src,,180,400,,8,12,,\n");
            var p = Assert.Single(r.Profiles);
            Assert.Equal(CornerRadiusStatus.Missing, p.RadiusStatus);
            Assert.True(double.IsNaN(p.Section["r"]));
        }

        [Fact]
        public void Json_RejectsWrongSchemaAndMissingProvenance()
        {
            Assert.Contains(ProfileLibraryReader.ReadJson("{\"schemaVersion\":2,\"source\":\"x\",\"profiles\":[]}").Issues, i => i.IsError);
            Assert.Contains(ProfileLibraryReader.ReadJson("{\"schemaVersion\":1,\"units\":\"mm\",\"profiles\":[{\"id\":\"a\",\"family\":\"CHS\",\"name\":\"a\",\"dimensions\":{\"D\":100,\"t\":5}}]}").Issues,
                            i => i.IsError && i.Message.Contains("source"));
            Assert.Contains(ProfileLibraryReader.ReadJson("not json").Issues, i => i.IsError);
        }

        [Fact]
        public void Match_FindsProfileWithinTolerance_OtherwiseCustom()
        {
            var lib = new ProfileLibrary();
            lib.Add(ProfileLibraryReader.ReadFile(DataPath("Profiles", "demo-profiles.json")));
            var ub = lib.FindById("demo-ub-406x178x60");
            Assert.Same(ub, lib.Match(ub.Section, 0.01));
            Assert.Same(ub, lib.Match(ub.Section.With("tw", 7.905), 0.01));
            Assert.Null(lib.Match(ub.Section.With("tw", 8.5), 0.01));
            Assert.Null(lib.Match(new SteelSection(SteelFamily.UC, ub.Section.Dimensions.ToDictionary(k => k.Key, k => k.Value)), 0.01)); // family matters
        }

        [Fact]
        public void Library_RejectsDuplicateIdsAcrossFiles()
        {
            var lib = new ProfileLibrary();
            var r = ProfileLibraryReader.ReadFile(DataPath("Profiles", "demo-profiles.json"));
            lib.Add(r);
            lib.Add(ProfileLibraryReader.ReadFile(DataPath("Profiles", "demo-profiles.json")));
            Assert.Equal(12, lib.Profiles.Count);
            Assert.Contains(lib.Issues, i => i.IsError && i.Message.Contains("Duplicate"));
        }
    }
}
