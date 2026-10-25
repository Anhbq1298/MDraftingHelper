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
        [Fact]
        public void Settings_RoundTrip_AndCorruptOrPartialFilesFallBackToDefaults()
        {
            var dir = Path.Combine(Path.GetTempPath(), "MDH_Tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "settings.json");

            var s = new UserSettings();
            s.Tie.AngleB = 180;
            s.Layers.Tie = "MY_TIES";
            SettingsStore.Save(path, s);
            SettingsStore.Save(path, s); // replace an existing file
            var back = SettingsStore.Load(path, out var problem);
            Assert.Null(problem);
            Assert.Equal(180, back.Tie.AngleB);
            Assert.Equal("MY_TIES", back.Layers.Tie);

            File.WriteAllText(path, "{ this is not json");
            Assert.Equal("MDH_TIE", SettingsStore.Load(path, out problem).Layers.Tie);
            Assert.NotNull(problem);

            File.WriteAllText(path, "{\"Version\":1}");
            var partial = SettingsStore.Load(path, out problem);
            Assert.Equal(0.1, partial.Rebar.DiameterTolerance);
            Assert.Equal(135, partial.Tie.AngleA);               // missing per-end angles fall back to 135°
            Assert.Equal(135, partial.Tie.AngleB);
            Assert.Equal("OpenTie", partial.Tie.Mode);
            Assert.False(partial.Steel.CentreAxes);
        }

        [Fact]
        public void TieUsesEc2DraftingRules_RectangularKeepsShippedPresets()
        {
            // Tie & Hook has no preset choice: the bend is largest bar Ø + 5 mm, tails are automatic.
            var tie = new TieViewModel();
            tie.SetBars(new Geometry.Point2(0, 0), 20, new Geometry.Point2(300, 0), 25);
            Assert.Equal(30, tie.BendDiameter);                   // 25 + 5
            Assert.Equal(15, tie.Result.CentrelineRadius, 9);
            Assert.Equal(100, tie.TailLengthA);                   // 135°: max(10φ, 70)
            tie.AngleB = HookAngle.Deg180;
            Assert.Equal(50, tie.TailLengthB);                    // 180°: max(5φ, 50)

            // MDH_RECT still reads the shipped preset file.
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "HookPresets", "hook-presets.json");
            var presets = HookPresetReader.ReadFile(path, out var problem);
            Assert.Null(problem);
            Assert.Contains(presets, p => p.IsManual);
            Assert.All(presets, p => Assert.False(p.IsVerified));
            var project = presets.Single(p => p.Id == "project-bend-detail");
            Assert.Equal(100, project.TailLength(HookAngle.Deg135, 10));
            Assert.Equal(50, project.TailLength(HookAngle.Deg180, 10));
            Assert.True(Assert.Single(HookPresetReader.Read("garbage", out problem)).IsManual);

            var rectangular = new RectangularViewModel(presets);
            Assert.Equal("project-bend-detail", rectangular.AutoPreset.Id);
            rectangular.AutoHook = HookAngle.Deg180;
            Assert.Equal(50, rectangular.Section.AutoTieSettings.TailLengthA);
        }
    }
}
