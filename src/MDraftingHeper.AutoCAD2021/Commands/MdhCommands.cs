using System;
using System.Collections.Generic;
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
using MDraftingHeper.Core.Rectangular;
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
            var vm = new TieViewModel();
            RestoreTieSettings(vm, settings.Tie);

            PlaneFrame openFrame = null, hoopFrame = null;
            bool select = false;
            while (true)
            {
                if (select)
                {
                    select = false;
                    if (vm.IsHoop)
                    {
                        var hoop = SelectHoopBars(doc, units);
                        if (hoop != null) { hoopFrame = hoop.Value.frame; vm.SetHoopBars(hoop.Value.bars); }
                    }
                    else
                    {
                        var bars = SelectBars(doc, units);
                        if (bars != null) { openFrame = bars.Value.frame; vm.SetBars(bars.Value.ca, bars.Value.da, bars.Value.cb, bars.Value.db); }
                    }
                }

                Dialogs.Show(new TieWindow(vm));
                StoreTieSettings(vm, settings.Tie);
                MdhApp.SaveSettings();

                if (vm.Action == DialogAction.Select) { select = true; continue; }
                if (vm.Action != DialogAction.Commit || vm.Result?.Path == null) return;

                var frame = vm.IsHoop ? hoopFrame : openFrame;
                var layer = settings.Layers.Tie;
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    DrawingGuards.EnsureLayer(tr, doc.Database, layer, 1);
                    TieWriter.Append(tr, doc.Database, TieWriter.CreatePolyline(frame, vm.Result, vm.TieSize, layer));
                    tr.Commit();
                }
                doc.Editor.WriteMessage("\n" + (vm.IsHoop ? "Closed hoop" : "Tie") + " drawn (" + vm.TieSize.Name + ", hooks " +
                                        (int)vm.AngleA + "°/" + (int)vm.AngleB + "°, bends sized for Ø" +
                                        vm.Result.ReferenceDiameter.ToString("0.##", CultureInfo.InvariantCulture) + ").");
                return;
            }
        }

        private static (PlaneFrame frame, List<HoopBar> bars)? SelectHoopBars(Document doc, UnitContext units)
        {
            var ed = doc.Editor;
            var pso = new PromptSelectionOptions { MessageForAdding = "\nSelect the longitudinal bar circles for the hoop: " };
            var ps = ed.GetSelection(pso, new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "CIRCLE") }));
            if (ps.Status != PromptStatus.OK) return null;
            var ids = ps.Value.GetObjectIds();
            if (ids.Length < 3) throw new MdhUserException("A closed hoop needs at least three bar circles.");

            using (var tr = doc.Database.TransactionManager.StartOpenCloseTransaction())
            {
                var circles = ids.Select(id => (Circle)tr.GetObject(id, OpenMode.ForRead)).ToList();
                var frame = PlaneFrame.FromCircle(circles[0], units);
                var bars = new List<HoopBar>();
                int skipped = 0;
                foreach (var c in circles)
                {
                    if (!(c.Radius > 0) || !frame.Contains(c, out _)) { skipped++; continue; }
                    bars.Add(new HoopBar(frame.ToCore(c.Center), units.ToMm(2 * c.Radius)));
                }
                tr.Commit();
                if (skipped > 0) ed.WriteMessage("\n" + skipped + " circle(s) not in the plane of the first one were ignored.");
                if (bars.Count < 3) throw new MdhUserException("A closed hoop needs at least three coplanar bar circles.");
                return (frame, bars);
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
            if (Enum.TryParse(s.Mode, out TieMode mode)) vm.Mode = mode;
            if (Enum.IsDefined(typeof(HookAngle), s.AngleA)) vm.AngleA = (HookAngle)s.AngleA;
            if (Enum.IsDefined(typeof(HookAngle), s.AngleB)) vm.AngleB = (HookAngle)s.AngleB;
            vm.TieSize = TieBarSize.Find(s.TieSize) ?? vm.TieSize;
            if (Enum.TryParse(s.Ends, out HookEnds ends)) vm.Ends = ends;
        }

        private static void StoreTieSettings(TieViewModel vm, Core.Settings.TieSettings s)
        {
            s.Mode = vm.Mode.ToString();
            s.AngleA = (int)vm.AngleA;
            s.AngleB = (int)vm.AngleB;
            s.TieSize = vm.TieSize.Name;
            s.Ends = vm.Ends.ToString();
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
            if (!string.IsNullOrEmpty(s.ProfileId)) vm.SelectProfileById(s.ProfileId);
            if (Enum.TryParse(s.Anchor, out InsertionAnchor anchor)) vm.Anchor = anchor;
            vm.RotationText = s.RotationDegrees.ToString(CultureInfo.InvariantCulture);
            vm.CentreAxes = false;
            vm.ProfileLabel = false;

            while (true)
            {
                Dialogs.Show(new SteelWindow(vm));
                s.Family = vm.Family.ToString();
                s.ProfileId = vm.SelectedProfile?.Id;
                s.Anchor = vm.Anchor.ToString();
                if (!double.IsNaN(vm.RotationDegrees)) s.RotationDegrees = vm.RotationDegrees;
                s.CentreAxes = false;
                s.ProfileLabel = false;
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
                        CentreAxes = false,
                        Label = false,
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

        // ------------------------------------------------------------------ Quick Draw Rectangular

        [CommandMethod("MDH", "MDH_RECT", CommandFlags.Modal)]
        public void Rectangular() => Run(RectangularFlow);

        private static void RectangularFlow(Document doc)
        {
            DrawingGuards.EnsureWritable(doc);
            var units = UnitContext.Resolve(doc);
            if (units == null) return;
            var vm = new RectangularViewModel(ProfileCache.HookPresets);
            while (true)
            {
                Dialogs.Show(new RectangularWindow(vm));
                if (vm.Action != DialogAction.Commit) return;
                var validation = vm.ValidateFinal();
                if (!validation.IsValid)
                {
                    doc.Editor.WriteMessage("\nMDraftingHeper: " + validation.Summary.Replace("\n", " "));
                    continue;
                }
                var point = doc.Editor.GetPoint(new PromptPointOptions("\nRectangular section insertion point: "));
                if (point.Status != PromptStatus.OK) continue;
                var ao = new PromptAngleOptions("\nSection rotation <0>: ")
                {
                    BasePoint = point.Value,
                    UseBasePoint = true,
                    DefaultValue = 0,
                    UseDefaultValue = true
                };
                var angle = doc.Editor.GetAngle(ao);
                if (angle.Status != PromptStatus.OK && angle.Status != PromptStatus.None) continue;
                double rotation = angle.Status == PromptStatus.None ? 0 : angle.Value;
                var layers = MdhApp.Settings.Layers;
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    DrawingGuards.EnsureLayer(tr, doc.Database, layers.Section, 7);
                    DrawingGuards.EnsureLayer(tr, doc.Database, layers.LongitudinalRebar, 1);
                    DrawingGuards.EnsureLayer(tr, doc.Database, layers.Tie, 2);
                    RectangularSectionWriter.Insert(tr, doc.Database, new RectangularInsertOptions
                    {
                        Section = vm.Section,
                        InsertionPoint = point.Value.TransformBy(doc.Editor.CurrentUserCoordinateSystem),
                        Ucs = doc.Editor.CurrentUserCoordinateSystem,
                        RotationRadians = rotation,
                        Units = units,
                        SectionLayer = layers.Section,
                        RebarLayer = layers.LongitudinalRebar,
                        TieLayer = layers.Tie
                    });
                    tr.Commit();
                }
                var audit = RestraintAuditService.Audit(vm.Section);
                doc.Editor.WriteMessage("\nRectangular section inserted as one undo operation." +
                    (audit.UnrestrainedNames.Count == 0 ? " Restraint audit complete." : " Incomplete restraint: " + string.Join(", ", audit.UnrestrainedNames) + "."));
                return;
            }
        }

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
