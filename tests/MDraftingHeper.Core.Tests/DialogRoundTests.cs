using System.Collections.Generic;
using MDraftingHeper.Core.Geometry;
using MDraftingHeper.Core.Hooks;
using MDraftingHeper.Core.Steel;
using MDraftingHeper.UI.ViewModels;
using Xunit;

namespace MDraftingHeper.Core.Tests
{
    /// <summary>
    /// The commands reopen the same view model after each model-space selection step. Closing a
    /// reopened dialog with the window's X must not repeat the previous round's action.
    /// </summary>
    public class DialogRoundTests
    {
        [Fact]
        public void Tie_CommitThenReopenAndCloseWithX_IsNotCommit()
        {
            var vm = new TieViewModel();
            vm.SetBars(new Point2(0, 0), 20, new Point2(300, 0), 25);
            vm.BeginDialog();
            Assert.True(vm.DrawCommand.CanExecute(null));
            vm.DrawCommand.Execute(null);
            Assert.Equal(DialogAction.Commit, vm.Action);

            // A new dialog round must not retain the previous Apply action.
            vm.BeginDialog();
            Assert.Equal(DialogAction.None, vm.Action);
        }

        [Fact]
        public void Steel_InsertThenReopenAndCloseWithX_IsNotCommit()
        {
            var lib = new ProfileLibrary();
            var vm = new SteelViewModel(lib, "");
            vm.Family = SteelFamily.CHS;
            vm.Dimensions[0].Text = "168.3";
            vm.Dimensions[1].Text = "7.1";
            vm.BeginDialog();
            vm.InsertCommand.Execute(null);
            Assert.Equal(DialogAction.Commit, vm.Action);
            vm.BeginDialog();
            Assert.Equal(DialogAction.None, vm.Action);
        }

        [Fact]
        public void Rebar_ExplicitButtonsStillReportTheirAction()
        {
            var vm = new RebarViewModel();
            vm.BeginDialog();
            vm.SelectCommand.Execute(null);
            Assert.Equal(DialogAction.Select, vm.Action);
            vm.BeginDialog();
            vm.CloseCommand.Execute(null);
            Assert.Equal(DialogAction.Close, vm.Action);
        }
    }
}
