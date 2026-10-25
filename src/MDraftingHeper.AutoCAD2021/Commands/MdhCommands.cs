using System;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using MDraftingHeper.AutoCAD2021.App;
using MDraftingHeper.AutoCAD2021.Blocks;
using MDraftingHeper.AutoCAD2021.Drawing;
using MDraftingHeper.AutoCAD2021.Metadata;
using MDraftingHeper.AutoCAD2021.Selection;
using MDraftingHeper.Core.Geometry;
using MDraftingHeper.Core.Hooks;
using MDraftingHeper.Core.Steel;
using MDraftingHeper.UI.ViewModels;
using MDraftingHeper.UI.Views;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace MDraftingHeper.AutoCAD2021.Commands
{
    public sealed class MdhCommands
    {
        // ------------------------------------------------------------------ Rebar Percentage

        [CommandMethod("MDH", "MDH_REBAR", CommandFlags.Modal)]
        public void Rebar() => Run(RebarFlow);

        private static void RebarFlow(Document doc)
        {
            DrawingGuards.EnsureWritable(doc);
            var units = UnitContext.Resolve(doc);
            if (units == null) return;
            var settings = MdhApp.Settings;
            var vm = new RebarViewModel
            {
                DiameterTolerance = settings.Rebar.DiameterTolerance,
                TargetRhoText = settings.Rebar.TargetRhoPercent?.ToString(CultureInfo.InvariantCulture) ?? ""
            };

            bool select = true;
            while (true)
            {
                if (select)
                {
                    var sel = SelectRebar(doc, units);
                    if (sel == null && !vm.HasSelection) return; // Esc before anything was selected
                    if (sel != null) vm.SetSelection(sel.Boundary, sel.Bars, sel.Notes, units.Description);
                }

                Dialogs.Show(new RebarWindow(vm));
                settings.Rebar.DiameterTolerance = vm.DiameterTolerance;
                settings.Rebar.TargetRhoPercent = vm.TargetRho;
                MdhApp.SaveSettings();

                if (vm.Action == DialogAction.Select) { select = true; continue; }
                if (vm.Action != DialogAction.Commit || vm.Result == null) return;

                var ppo = new PromptPointOptions("\nTable insertion point: ");
                var pr = doc.Editor.GetPoint(ppo);
                if (pr.Status != PromptStatus.OK) { select = false; continue; } // back to the dialog
                var ucs = doc.Editor.CurrentUserCoordinateSystem;
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    var layer = settings.Layers.RebarTable;
                    DrawingGuards.EnsureLayer(tr, doc.Database, layer, 7);
                    RebarTableWriter.Insert(tr, doc.Database, vm.Result, pr.Value.TransformBy(ucs), ucs, settings.Rebar, layer);
                    tr.Commit();
                }
                doc.Editor.WriteMessage("\nRebar table inserted (snapshot; it does not update automatically).");
                return;
            }
        }

        private static RebarSelection SelectRebar(Document doc, UnitContext units)
        {
            var ed = doc.Editor;
            var pso = new PromptSelectionOptions { MessageForAdding = "\nSelect rebar circles: " };
            var filter = new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "CIRCLE") });
            var ps = ed.GetSelection(pso, filter);
            if (ps.Status != PromptStatus.OK) return null;

            var peo = new PromptEntityOptions("\nSelect the closed section boundary polyline: ");
            peo.SetRejectMessage("\nOnly a closed lightweight polyline (or a circle for a circular section) can be the boundary.");
            peo.AddAllowedClass(typeof(Polyline), true);
            peo.AddAllowedClass(typeof(Circle), true);
            var pe = ed.GetEntity(peo);
            if (pe.Status != PromptStatus.OK) return null;

            using (var tr = doc.Database.TransactionManager.StartOpenCloseTransaction())
            {
                var sel = RebarReader.Read(tr, pe.ObjectId, ps.Value.GetObjectIds(), units);
                tr.Commit();
                return sel;
            }
        }

        // ------------------------------------------------------------------ Tie & Hook

        [CommandMethod("MDH", "MDH_TIE", CommandFlags.Modal)]
        public void Tie() => Run(TieFlow);

        private static void TieFlow(Document doc)
        {
            DrawingGuards.EnsureWritable(doc);
            var units = UnitContext.Resolve(doc);
            if (units == null) return;
            var settings = MdhApp.Settings;
            var vm = new TieViewModel(ProfileCache.HookPresets);
            RestoreTieSettings(vm, settings.Tie);

            PlaneFrame frame = null;
            bool select = true;
            while (true)
            {
                if (select)
                {
                    var bars = SelectBars(doc, units);
                    if (bars == null && frame == null) return;
                    if (bars != null)
                    {
                        frame = bars.Value.frame;
                        vm.SetBars(bars.Value.ca, bars.Value.da, bars.Value.cb, bars.Value.db);
                    }
                }

                Dialogs.Show(new TieWindow(vm));
                StoreTieSettings(vm, settings.Tie);
                MdhApp.SaveSettings();

                if (vm.Action == DialogAction.Select) { select = true; continue; }
                if (vm.Action != DialogAction.Commit || vm.Result?.Path == null) return;

                var layer = settings.Layers.Tie;
                var f = frame;
                bool accepted = TiePreview.Confirm(doc.Editor,
                    () => vm.Result?.Path == null ? null : TieWriter.CreatePolyline(f, vm.Result, vm.TieSize, layer),
                    vm.ToggleFlip);
                StoreTieSettings(vm, settings.Tie);
                if (!accepted) { select = false; continue; } // Esc: nothing written, back to the dialog

                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    DrawingGuards.EnsureLayer(tr, doc.Database, layer, 1);
                    var pl = TieWriter.CreatePolyline(f, vm.Result, vm.TieSize, layer);
                    TieWriter.Append(tr, doc.Database, pl, vm.BuildInput(), vm.Result, vm.TieSize, vm.Preset?.Id);
                    tr.Commit();
                }
                MdhApp.SaveSettings();
                doc.Editor.WriteMessage("\nTie drawn (" + vm.TieSize.Name + ", " + (int)vm.SelectedAngle.Angle + "° hooks, Dref " +
                                        vm.Result.ReferenceDiameter.ToString("0.##", CultureInfo.InvariantCulture) + " mm).");
                return;
            }
        }

        private static (PlaneFrame frame, Point2 ca, double da, Point2 cb, double db)? SelectBars(Document doc, UnitContext units)
        {
            var ed = doc.Editor;
            ObjectId PickCircle(string msg)
            {
                var peo = new PromptEntityOptions(msg);
                peo.SetRejectMessage("\nSelect a Circle representing a longitudinal bar.");
                peo.AddAllowedClass(typeof(Circle), true);
                var r = ed.GetEntity(peo);
                return r.Status == PromptStatus.OK ? r.ObjectId : ObjectId.Null;
            }

            var a = PickCircle("\nSelect longitudinal bar Circle A: ");
            if (a.IsNull) return null;
            ObjectId b;
            while (true)
            {
                b = PickCircle("\nSelect longitudinal bar Circle B: ");
                if (b.IsNull) return null;
                if (b != a) break;
                ed.WriteMessage("\nCircle B must be a different entity from Circle A.");
            }

            using (var tr = doc.Database.TransactionManager.StartOpenCloseTransaction())
            {
                var ca = (Circle)tr.GetObject(a, OpenMode.ForRead);
                var cb = (Circle)tr.GetObject(b, OpenMode.ForRead);
                if (!(ca.Radius > 0) || !(cb.Radius > 0)) throw new MdhUserException("Both circles need a positive radius.");
                var frame = PlaneFrame.FromCircle(ca, units);
                if (!frame.Contains(cb, out var why))
                    throw new MdhUserException("Circle B is " + why + " of Circle A. Both bars must lie in one plane.");
                var pa = frame.ToCore(ca.Center);
                var pb = frame.ToCore(cb.Center);
                if (pa.DistanceTo(pb) < 1e-6) throw new MdhUserException("The two circles have coincident centres.");
                var result = (frame, pa, units.ToMm(2 * ca.Radius), pb, units.ToMm(2 * cb.Radius));
                tr.Commit();
                return result;
            }
        }

        private static void RestoreTieSettings(TieViewModel vm, Core.Settings.TieSettings s)
        {
            vm.SelectedAngle = vm.AngleOptions.FirstOrDefault(o => (int)o.Angle == s.AngleDegrees) ?? vm.SelectedAngle;
            vm.TieSize = TieBarSize.Find(s.TieSize) ?? vm.TieSize;
            if (Enum.TryParse(s.Ends, out HookEnds ends)) vm.Ends = ends;
            vm.Preset = vm.Presets.FirstOrDefault(p => p.Id == s.PresetId) ?? vm.Preset;
            vm.GapText = s.ClearGap.ToString(CultureInfo.InvariantCulture);
            if (vm.IsManual)
            {
                vm.MandrelText = s.ManualInsideMandrelDiameter.ToString(CultureInfo.InvariantCulture);
                vm.TailText = s.ManualTailLength.ToString(CultureInfo.InvariantCulture);
            }
        }

        private static void StoreTieSettings(TieViewModel vm, Core.Settings.TieSettings s)
        {
            var input = vm.BuildInput();
            s.AngleDegrees = (int)vm.SelectedAngle.Angle;
            s.TieSize = vm.TieSize.Name;
            s.Ends = vm.Ends.ToString();
            s.PresetId = vm.Preset?.Id;
            s.ClearGap = input.ClearGap;
            if (vm.IsManual)
            {
                s.ManualInsideMandrelDiameter = input.InsideMandrelDiameter;
                s.ManualTailLength = input.TailLength;
            }
        }

        // ------------------------------------------------------------------ Steel Section

        [CommandMethod("MDH", "MDH_STEEL", CommandFlags.Modal)]
        public void Steel() => Run(SteelFlow);

        private static void SteelFlow(Document doc)
        {
            DrawingGuards.EnsureWritable(doc);
            var units = UnitContext.Resolve(doc, mmOnly: true);
            if (units == null) return;
            var s = MdhApp.Settings.Steel;
            var vm = new SteelViewModel(ProfileCache.Library, ProfileCache.LibraryNote);
            if (SteelFamilies.TryParse(s.Family, out var fam)) vm.Family = fam;
            var remembered = vm.Profiles.FirstOrDefault(p => p.Id == s.ProfileId);
            if (remembered != null) vm.SelectedProfile = remembered;
            if (Enum.TryParse(s.Anchor, out InsertionAnchor anchor)) vm.Anchor = anchor;
            vm.RotationText = s.RotationDegrees.ToString(CultureInfo.InvariantCulture);
            vm.CentreAxes = s.CentreAxes;
            vm.ProfileLabel = s.ProfileLabel;

            while (true)
            {
                Dialogs.Show(new SteelWindow(vm));
                s.Family = vm.Family.ToString();
                s.ProfileId = vm.SelectedProfile?.Id;
                s.Anchor = vm.Anchor.ToString();
                if (!double.IsNaN(vm.RotationDegrees)) s.RotationDegrees = vm.RotationDegrees;
                s.CentreAxes = vm.CentreAxes;
                s.ProfileLabel = vm.ProfileLabel;
                MdhApp.SaveSettings();
                if (vm.Action != DialogAction.Commit || !vm.CanInsert) return;

                var pr = doc.Editor.GetPoint(new PromptPointOptions("\nInsertion point (" + vm.Anchor + "): "));
                if (pr.Status != PromptStatus.OK) continue; // back to the dialog
                var ucs = doc.Editor.CurrentUserCoordinateSystem;
                var layers = MdhApp.Settings.Layers;
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    SteelBlocks.Insert(tr, doc.Database, new SteelInsertOptions
                    {
                        Section = vm.Section,
                        Profile = vm.MatchedProfile,
                        Anchor = vm.Anchor,
                        RotationRadians = vm.RotationDegrees * Math.PI / 180,
                        CentreAxes = vm.CentreAxes,
                        Label = vm.ProfileLabel,
                        LabelHeight = s.LabelHeight > 0 ? s.LabelHeight : 2.5,
                        BlockLayer = layers.Steel, AxesLayer = layers.Axes, LabelLayer = layers.Label
                    }, pr.Value.TransformBy(ucs), ucs);
                    tr.Commit();
                }
                doc.Editor.WriteMessage("\n" + vm.LabelText + " inserted. Select it and press Ctrl+1 to edit its dimensions.");
                return;
            }
        }

        [CommandMethod("MDH", "MDH_STEELSYNC", CommandFlags.Modal)]
        public void SteelSyncCommand() => Run(doc =>
        {
            int n = SteelSync.ReconcileAll(doc, onlyIfChanged: false);
            doc.Editor.WriteMessage("\nMDraftingHeper: " + n + " steel section record(s) updated from current geometry.");
        });

        // ------------------------------------------------------------------ shared

        internal static void Run(Action<Document> flow)
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            try
            {
                flow(doc);
            }
            catch (MdhUserException ex)
            {
                doc.Editor.WriteMessage("\nMDraftingHeper: " + ex.Message);
            }
            catch (System.Exception ex)
            {
                // Command boundary: report concisely, keep details in the log, never crash AutoCAD.
                Log.Error("Command failed", ex);
                doc.Editor.WriteMessage("\nMDraftingHeper: unexpected error (" + ex.Message + "). Details: " + Log.FilePath);
            }
        }
    }

    internal static class Dialogs
    {
        public static void Show(System.Windows.Window w) =>
            AcApp.ShowModalWindow(AcApp.MainWindow.Handle, w, false);
    }
}
