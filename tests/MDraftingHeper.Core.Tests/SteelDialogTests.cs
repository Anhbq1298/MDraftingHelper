using System.Linq;
using MDraftingHeper.Core.Steel;
using MDraftingHeper.UI.ViewModels;
using Xunit;

namespace MDraftingHeper.Core.Tests
{
    /// <summary>Steel dialog: "(Custom dimensions)" in every family's list; fields locked for catalogue sizes.</summary>
    public class SteelDialogTests
    {
        private const string Json = @"{""schemaVersion"":1,""source"":""test"",""units"":""mm"",""profiles"":[
 {""id"":""ub-a"",""family"":""UB"",""name"":""UB A"",""dimensions"":{""H"":406.4,""B"":177.9,""tw"":7.9,""tf"":12.8,""r"":10.2}},
 {""id"":""ub-b"",""family"":""UB"",""name"":""UB B"",""dimensions"":{""H"":303.4,""B"":165,""tw"":6,""tf"":10.2,""r"":8.9}},
 {""id"":""ub-nor"",""family"":""UB"",""name"":""UB no radius"",""dimensions"":{""H"":300,""B"":150,""tw"":7,""tf"":10}},
 {""id"":""chs-a"",""family"":""CHS"",""name"":""CHS A"",""dimensions"":{""D"":168.3,""t"":6.3}}]}";

        private static SteelViewModel Vm()
        {
            var lib = new ProfileLibrary();
            lib.Add(ProfileLibraryReader.ReadJson(Json));
            return new SteelViewModel(lib, "");
        }

        private static double Dim(SteelViewModel vm, string key) => vm.Dimensions.Single(d => d.Key == key).Value;

        [Fact]
        public void EveryFamilyListStartsWithCustom()
        {
            var vm = Vm();
            foreach (var f in SteelFamilies.All)
            {
                vm.Family = f;
                Assert.True(vm.Profiles[0].IsCustom, f.ToString());
                Assert.Equal("(Custom dimensions)", vm.Profiles[0].Display);
            }
        }

        [Fact]
        public void CatalogueSizeIsSelectedByDefault_AndLocked()
        {
            var vm = Vm();
            vm.Family = SteelFamily.UB;
            Assert.False(vm.IsCustom);
            Assert.Equal("ub-a", vm.SelectedProfile.Id);
            Assert.Equal(406.4, Dim(vm, "H"));
            Assert.Same(vm.SelectedProfile, vm.MatchedProfile);
        }

        [Fact]
        public void FamilyWithoutProfiles_StartsInCustom()
        {
            var vm = Vm();
            vm.Family = SteelFamily.SHS;
            Assert.True(vm.IsCustom);
            Assert.Single(vm.Profiles);
        }

        [Fact]
        public void Custom_UnlocksAndKeepsShownValues_ThenProfileRelocksAndReloads()
        {
            var vm = Vm();
            vm.Family = SteelFamily.UB;
            vm.SelectedItem = vm.Profiles[0];
            Assert.True(vm.IsCustom);
            Assert.Null(vm.SelectedProfile);
            Assert.Null(vm.MatchedProfile);
            Assert.Equal(406.4, Dim(vm, "H"));                 // starts from the values shown
            vm.Dimensions.Single(d => d.Key == "H").Text = "450";
            Assert.StartsWith("Custom UB", vm.LabelText);

            vm.SelectedItem = vm.Profiles.First(i => !i.IsCustom && i.Profile.Id == "ub-b");
            Assert.False(vm.IsCustom);
            Assert.Equal(303.4, Dim(vm, "H"));                 // catalogue values replace the custom ones
            Assert.Equal("UB B", vm.LabelText);
        }

        [Fact]
        public void ProfileWithMissingRadius_OpensUnlocked()
        {
            var vm = Vm();
            vm.Family = SteelFamily.UB;
            Assert.True(vm.SelectProfileById("ub-nor"));
            Assert.True(vm.IsCustom);
            Assert.True(double.IsNaN(Dim(vm, "r")));
            Assert.False(vm.CanInsert);                          // r must be entered first
        }

        [Fact]
        public void SearchKeepsCustomEntry_AndSelection()
        {
            var vm = Vm();
            vm.Family = SteelFamily.UB;
            vm.SelectProfileById("ub-b");
            vm.SearchText = "B";
            Assert.True(vm.Profiles[0].IsCustom);
            Assert.Equal("ub-b", vm.SelectedProfile.Id);
        }
    }
}
