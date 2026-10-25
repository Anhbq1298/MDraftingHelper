using System;
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

        /// <summary>Demonstration library (test fixture; not shipped).</summary>
        private static string DemoPath() => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestData", "demo-profiles.json");

        private const string Header = "id,family,name,units,source,D,B,H,t,tw,tf,r,cornerRadiusConvention\n";

        [Fact]
        public void Validation_Areas_AndAnchors()
        {
            Assert.False(SteelValidator.Validate(S(SteelFamily.CHS, ("D", 100), ("t", 50))).IsValid);
            Assert.False(SteelValidator.Validate(S(SteelFamily.CHS, ("D", 100), ("t", 0))).IsValid);
            var chs = S(SteelFamily.CHS, ("D", 168.3), ("t", 7.1));
            Assert.Equal(Math.PI / 4 * (168.3 * 168.3 - 154.1 * 154.1), SectionGeometry.GeometricArea(chs), 6);

            foreach (var (t, r) in new[] { (8.0, 8.0), (8.0, 50.0), (50.0, 60.0) }) // r = t, 2r = B, 2t = B
                Assert.False(SteelValidator.Validate(S(SteelFamily.RHS, ("H", 200), ("B", 100), ("t", t), ("r", r))).IsValid);
            var rhs = S(SteelFamily.RHS, ("H", 250), ("B", 150), ("t", 10), ("r", 20));
            double outer = 150 * 250 - (4 - Math.PI) * 400, inner = 130 * 230 - (4 - Math.PI) * 100;
            Assert.Equal(outer - inner, SectionGeometry.GeometricArea(rhs), 6);

            var ub = S(SteelFamily.UB, ("H", 406.4), ("B", 177.9), ("tw", 7.9), ("tf", 12.8), ("r", 10.2));
            Assert.Equal(2 * 177.9 * 12.8 + (406.4 - 2 * 12.8) * 7.9 + (4 - Math.PI) * 10.2 * 10.2, SectionGeometry.GeometricArea(ub), 6);
            foreach (var bad in new[] { ub.With("r", 90), ub.With("tf", 210), ub.With("tw", 200), ub.With("r", 200) })
                Assert.False(SteelValidator.Validate(bad).IsValid);

            var tl = AnchorMath.CentroidOffset(S(SteelFamily.UB, ("H", 400), ("B", 200), ("tw", 8), ("tf", 12), ("r", 10)), InsertionAnchor.TopLeft);
            Assert.Equal(100, tl.X); Assert.Equal(-200, tl.Y);
        }

        [Fact]
        public void Import_ConvertsUnitsAndRadii_KeepsProvenance_LoadsTemplates()
        {
            var demo = ProfileLibraryReader.ReadFile(DemoPath());
            Assert.DoesNotContain(demo.Issues, i => i.IsError);
            Assert.All(demo.Profiles, p => Assert.True(p.IsDemonstration && p.DisplayName.StartsWith("[DEMO]") && p.Source.Length > 0));

            var csv = ProfileLibraryReader.ReadFile(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "Profiles", "profile-template.csv"));
            Assert.Equal(SteelFamily.RHS, Assert.Single(csv.Profiles).Family);

            var r = ProfileLibraryReader.ReadCsv(Header +
                "a,CHS,CHS a,cm,src,16.83,,,0.71,,,,\n" +
                "b,RHS,RHS b,mm,src,,100,200,8,,,6,inner\n" +
                "m,UB,no root radius,mm,src,,180,400,,8,12,,\n");
            Assert.DoesNotContain(r.Issues, i => i.IsError);
            Assert.Equal(168.3, r.Profiles[0].Section["D"], 9);                 // cm → mm
            Assert.Equal(14, r.Profiles[1].Section["r"], 9);                    // inner 6 + t 8 → outer radius
            Assert.Equal(CornerRadiusStatus.Missing, r.Profiles[2].RadiusStatus); // kept missing, not invented
        }

        [Fact]
        public void Import_RejectsBadRecordsAndFiles()
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
            foreach (var text in new[] { "Duplicate id", "Missing dimension tw", "units", "2t < D", "cornerRadiusConvention", "stable id", "source" })
                Assert.Contains(r.Issues, i => i.IsError && i.Message.Contains(text));

            Assert.Contains(ProfileLibraryReader.ReadJson("{\"schemaVersion\":2,\"source\":\"x\",\"profiles\":[]}").Issues, i => i.IsError);
            Assert.Contains(ProfileLibraryReader.ReadJson("not json").Issues, i => i.IsError);
        }

        [Fact]
        public void Library_MatchesWithinTolerance_AndRejectsDuplicateIdsAcrossFiles()
        {
            var lib = new ProfileLibrary();
            lib.Add(ProfileLibraryReader.ReadFile(DemoPath()));
            var ub = lib.FindById("demo-ub-406x178x60");
            Assert.Same(ub, lib.Match(ub.Section.With("tw", 7.905), 0.01));
            Assert.Null(lib.Match(ub.Section.With("tw", 8.5), 0.01));
            Assert.Null(lib.Match(new SteelSection(SteelFamily.UC, ub.Section.Dimensions.ToDictionary(k => k.Key, k => k.Value)), 0.01)); // family matters

            lib.Add(ProfileLibraryReader.ReadFile(DemoPath()));
            Assert.Equal(12, lib.Profiles.Count);
            Assert.Contains(lib.Issues, i => i.IsError && i.Message.Contains("Duplicate"));
        }
    }
}
