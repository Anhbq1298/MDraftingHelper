using System;
using System.IO;
using System.Linq;
using MDraftingHeper.Core.Steel;
using Xunit;

namespace MDraftingHeper.Core.Tests
{
    /// <summary>The shipped profile libraries converted from openly licensed datasets (tools/Profiles).</summary>
    public class OpenProfileLibraryTests
    {
        private static readonly string[] Files = { "open-uk-ub-uc.json", "open-en-ipe-he.json", "open-en-hollow.json" };

        private static ProfileLibrary Load()
        {
            var lib = new ProfileLibrary();
            foreach (var f in Files)
                lib.Add(ProfileLibraryReader.ReadFile(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "Profiles", f)));
            return lib;
        }

        private static void AssertDims(SteelProfile p, params (string k, double v)[] expected)
        {
            Assert.NotNull(p);
            foreach (var (k, v) in expected) Assert.Equal(v, p.Section[k], 9);
        }

        [Fact]
        public void AllFilesLoad_WithoutErrors_UniqueIds_AndProvenance()
        {
            var lib = Load();
            Assert.DoesNotContain(lib.Issues, i => i.IsError);
            Assert.Equal(1749, lib.Profiles.Count);
            Assert.Equal(lib.Profiles.Count, lib.Profiles.Select(p => p.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.All(lib.Profiles, p =>
            {
                Assert.False(p.IsDemonstration);
                Assert.Contains("github.com", p.SourceVersion);
                Assert.True(SteelValidator.Validate(p.Section).IsValid, p.Name);
            });
            Assert.Equal(107, lib.ForFamily(SteelFamily.UB).Count());
            Assert.Equal(46, lib.ForFamily(SteelFamily.UC).Count());
            Assert.Equal(90, lib.ForFamily(SteelFamily.IH).Count());
            Assert.Equal(344, lib.ForFamily(SteelFamily.CHS).Count());
            Assert.Equal(406, lib.ForFamily(SteelFamily.SHS).Count());
            Assert.Equal(756, lib.ForFamily(SteelFamily.RHS).Count());
        }

        [Fact]
        public void SpotChecks_CornerRadiusRules_AndIhFamily()
        {
            var lib = Load();
            AssertDims(lib.FindById("uk-uc-203x203x46"), ("H", 203.2), ("B", 203.6), ("tw", 7.2), ("tf", 11.0), ("r", 10.2));
            AssertDims(lib.FindById("uk-ub-406x178x60"), ("H", 406.4), ("B", 177.9), ("tw", 7.9), ("tf", 12.8), ("r", 10.2));
            AssertDims(lib.FindById("en-ipe-300"), ("H", 300), ("B", 150), ("tw", 7.1), ("tf", 10.7), ("r", 15));
            AssertDims(lib.FindById("en-chs-168.3x6.3-hf"), ("D", 168.3), ("t", 6.3));

            var heb = lib.FindById("en-heb-300");
            AssertDims(heb, ("H", 300), ("B", 300), ("tw", 11), ("tf", 19), ("r", 27));
            Assert.Equal(SteelFamily.IH, heb.Family);
            Assert.Equal("MDH_ISECTION", SteelFamilies.BlockName(SteelFamily.IH));
            Assert.Same(heb, lib.Match(heb.Section, 0.01));

            // EN 10210-2 hot finished: ro = 1.5 t. EN 10219-2 cold formed: 2 t (t <= 6), 2.5 t (6 < t <= 10), 3 t (t > 10).
            AssertDims(lib.FindById("en-shs-100x100x5-hf"), ("B", 100), ("t", 5), ("r", 7.5));
            AssertDims(lib.FindById("en-shs-100x100x5-cf"), ("B", 100), ("t", 5), ("r", 10));
            foreach (var p in lib.ForFamily(SteelFamily.SHS).Concat(lib.ForFamily(SteelFamily.RHS)))
            {
                double t = p.Section["t"], r = p.Section["r"];
                double factor = p.Name.EndsWith(" HF") ? 1.5 : t <= 6 ? 2.0 : t <= 10 ? 2.5 : 3.0;
                if (p.RadiusStatus == CornerRadiusStatus.Assumed) Assert.Equal(factor * t, r, 6);
            }
        }
    }
}
