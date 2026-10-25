using System;
using System.IO;
using System.Linq;
using MDraftingHeper.Core.Hooks;
using MDraftingHeper.Core.Settings;
using MDraftingHeper.UI.ViewModels;
using Xunit;

namespace MDraftingHeper.Core.Tests
{
    public class SettingsAndPresetTests
    {
        private static string TempFile()
        {
            var dir = Path.Combine(Path.GetTempPath(), "MDH_Tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "settings.json");
        }

        [Fact]
        public void Settings_RoundTrip()
        {
            var path = TempFile();
            var s = new UserSettings();
            s.Tie.AngleB = 180;
            s.Layers.Tie = "MY_TIES";
            SettingsStore.Save(path, s);
            SettingsStore.Save(path, s); // replace an existing file
            var back = SettingsStore.Load(path, out var problem);
            Assert.Null(problem);
            Assert.Equal(180, back.Tie.AngleB);
            Assert.Equal("MY_TIES", back.Layers.Tie);
        }

        [Fact]
        public void Settings_CorruptOrPartialFile_FallsBackToDefaults()
        {
            var path = TempFile();
            File.WriteAllText(path, "{ this is not json");
            var s = SettingsStore.Load(path, out var problem);
            Assert.NotNull(problem);
            Assert.Equal("MDH_TIE", s.Layers.Tie);

            File.WriteAllText(path, "{\"Version\":1}");
            var partial = SettingsStore.Load(path, out problem);
            Assert.NotNull(partial.Rebar);
            Assert.Equal(0.1, partial.Rebar.DiameterTolerance);
            Assert.Equal(135, partial.Tie.AngleA);          // missing per-end angles fall back to 135°
            Assert.Equal(135, partial.Tie.AngleB);
            Assert.Equal("OpenTie", partial.Tie.Mode);
            Assert.False(partial.Steel.CentreAxes);
            Assert.False(partial.Steel.ProfileLabel);
        }

        [Fact]
        public void ShippedHookPresets_IncludeManual_AndNoneClaimVerification()
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "HookPresets", "hook-presets.json");
            var presets = HookPresetReader.ReadFile(path, out var problem);
            Assert.Null(problem);
            Assert.Contains(presets, p => p.IsManual);
            Assert.All(presets, p => Assert.False(p.IsVerified));
            Assert.All(presets.Where(p => !p.IsVerified), p => Assert.Contains("UNVERIFIED", p.DisplayName));
            var ec2 = presets.Single(p => p.Id == "ec2-sg-candidate");
            Assert.Equal(40, ec2.InsideMandrelDiameter(10));
            var project = presets.Single(p => p.Id == "project-bend-detail");
            Assert.Null(project.InsideMandrelDiameter(10));
            Assert.Equal(100, project.TailLength(HookAngle.Deg90, 10));
            Assert.Equal(100, project.TailLength(HookAngle.Deg135, 10));
            Assert.Equal(50, project.TailLength(HookAngle.Deg180, 10));
            Assert.Equal(70, project.TailLength(HookAngle.Deg135, 6));
        }

        [Fact]
        public void BrokenPresetFile_StillOffersManual()
        {
            var presets = HookPresetReader.Read("garbage", out var problem);
            Assert.NotNull(problem);
            Assert.True(Assert.Single(presets).IsManual);
        }

        [Fact]
        public void TieAndHook_UsesEc2DraftingRules_RectangularKeepsProjectPreset()
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "HookPresets", "hook-presets.json");
            var presets = HookPresetReader.ReadFile(path, out var problem);
            Assert.Null(problem);

            // Tie & Hook has no preset choice: mandrel and tails are calculated automatically.
            var tie = new TieViewModel();
            Assert.Equal(40, tie.InsideMandrelDiameter);    // H10: 4φ
            Assert.Equal(100, tie.TailLengthA);              // 135°: max(10φ, 70)
            tie.AngleB = HookAngle.Deg180;
            Assert.Equal(50, tie.TailLengthB);               // 180°: max(5φ, 50)

            var rectangular = new RectangularViewModel(presets);
            Assert.Equal("project-bend-detail", rectangular.AutoPreset.Id);
            Assert.Equal(100, rectangular.Section.AutoTieSettings.TailLengthA);
            rectangular.AutoHook = HookAngle.Deg180;
            Assert.Equal(50, rectangular.Section.AutoTieSettings.TailLengthA);
            Assert.Equal(50, rectangular.Section.AutoTieSettings.TailLengthB);
        }
    }
}
