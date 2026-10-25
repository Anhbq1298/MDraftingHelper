using System.Linq;
using MDraftingHeper.Core.Geometry;
using MDraftingHeper.Core.Steel;
using MDraftingHeper.UI.ViewModels;
using Xunit;

namespace MDraftingHeper.Core.Tests
{
    public class DialogTests
    {
        private const string Json = @"{""schemaVersion"":1,""source"":""test"",""units"":""mm"",""profiles"":[
 {""id"":""ub-a"",""family"":""UB"",""name"":""UB A"",""dimensions"":{""H"":406.4,""B"":177.9,""tw"":7.9,""tf"":12.8,""r"":10.2}},
 {""id"":""ub-b"",""family"":""UB"",""name"":""UB B"",""dimensions"":{""H"":303.4,""B"":165,""tw"":6,""tf"":10.2,""r"":8.9}},
 {""id"":""ub-nor"",""family"":""UB"",""name"":""UB no radius"",""dimensions"":{""H"":300,""B"":150,""tw"":7,""tf"":10}}]}";

        private static SteelViewModel SteelVm()
        {
            var lib = new ProfileLibrary();
            lib.Add(ProfileLibraryReader.ReadJson(Json));
            return new SteelViewModel(lib, "");
        }

        private static double Dim(SteelViewModel vm, string key) => vm.Dimensions.Single(d => d.Key == key).Value;

        /// <summary>
        /// Commands reopen the same view model after each model-space step; closing a reopened dialog
        /// with X must not repeat the previous round's Draw/Insert.
        /// </summary>
        [Fact]
        public void ReopenedDialogs_ForgetThePreviousAction()
        {
            var tie = new TieViewModel();
            tie.SetBars(new Point2(0, 0), 20, new Point2(300, 0), 25);
            tie.BeginDialog();
            tie.DrawCommand.Execute(null);
            Assert.Equal(DialogAction.Commit, tie.Action);
            tie.BeginDialog();
            Assert.Equal(DialogAction.None, tie.Action);

            var steel = new SteelViewModel(new ProfileLibrary(), "");
            steel.Family = SteelFamily.CHS;
            steel.Dimensions[0].Text = "168.3";
            steel.Dimensions[1].Text = "7.1";
            steel.BeginDialog();
            steel.InsertCommand.Execute(null);
            Assert.Equal(DialogAction.Commit, steel.Action);
            steel.BeginDialog();
            Assert.Equal(DialogAction.None, steel.Action);

            var rebar = new RebarViewModel();
            rebar.BeginDialog();
            rebar.SelectCommand.Execute(null);
            Assert.Equal(DialogAction.Select, rebar.Action);
        }

        [Fact]
        public void SteelDialog_CustomEntryUnlocks_CatalogueSizesStayLocked()
        {
            var vm = SteelVm();
            foreach (var f in SteelFamilies.All)
            {
                vm.Family = f;
                Assert.Equal("(Custom dimensions)", vm.Profiles[0].Display);
            }
            vm.Family = SteelFamily.SHS;                        // no catalogue sizes → Custom
            Assert.True(vm.IsCustom);

            vm.Family = SteelFamily.UB;                         // first catalogue size, locked
            Assert.False(vm.IsCustom);
            Assert.Equal("ub-a", vm.SelectedProfile.Id);
            Assert.Equal(406.4, Dim(vm, "H"));

            vm.SelectedItem = vm.Profiles[0];                   // Custom unlocks, keeps the shown values
            Assert.True(vm.IsCustom);
            Assert.Null(vm.MatchedProfile);
            Assert.Equal(406.4, Dim(vm, "H"));
            vm.Dimensions.Single(d => d.Key == "H").Text = "450";
            Assert.StartsWith("Custom UB", vm.LabelText);

            Assert.True(vm.SelectProfileById("ub-b"));          // catalogue size relocks and reloads
            Assert.False(vm.IsCustom);
            Assert.Equal(303.4, Dim(vm, "H"));
            vm.SearchText = "B";
            Assert.True(vm.Profiles[0].IsCustom);
            Assert.Equal("ub-b", vm.SelectedProfile.Id);

            Assert.True(vm.SelectProfileById("ub-nor"));        // missing radius opens unlocked
            Assert.True(vm.IsCustom);
            Assert.False(vm.CanInsert);
        }
    }
}
