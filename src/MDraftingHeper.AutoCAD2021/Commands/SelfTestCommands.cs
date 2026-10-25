using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using MDraftingHeper.AutoCAD2021.App;
using MDraftingHeper.AutoCAD2021.Blocks;
using MDraftingHeper.AutoCAD2021.Drawing;
using MDraftingHeper.AutoCAD2021.Metadata;
using MDraftingHeper.AutoCAD2021.Ribbon;
using MDraftingHeper.AutoCAD2021.Selection;
using MDraftingHeper.Core.Hooks;
using MDraftingHeper.Core.Rebar;
using MDraftingHeper.Core.Rectangular;
using MDraftingHeper.Core.Steel;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace MDraftingHeper.AutoCAD2021.Commands
{
    /// <summary>
    /// Developer self-tests run inside AutoCAD (see docs/Acceptance-Checks.md). They exercise the same
    /// readers/writers as the interactive commands with programmatic geometry and print
    /// "MDH-SELFTEST PASS|FAIL" lines. Run them in a scratch drawing: they add entities.
    /// </summary>
    public sealed class SelfTestCommands
    {
        private static int _pass, _fail;
        private static ObjectId _undoBlock;

        /// <summary>Non-null while a feature group runs: its assertions report as one check.</summary>
        private static List<string> _groupFailures;

        private static void Check(string name, bool ok, string detail = "")
        {
            if (_groupFailures != null)
            {
                if (!ok) _groupFailures.Add(name + (string.IsNullOrEmpty(detail) ? "" : " (" + detail + ")"));
                return;
            }
            Report(name, ok, detail);
        }

        private static void Report(string name, bool ok, string detail)
        {
            if (ok) _pass++; else _fail++;
            var line = "MDH-SELFTEST " + (ok ? "PASS " : "FAIL ") + name + (ok || string.IsNullOrEmpty(detail) ? "" : " :: " + detail);
            AcApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage("\n" + line);
            Log.Info(line);
        }

        private static string F(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);
        private static bool Near(double a, double b, double tol = 1e-6) => Math.Abs(a - b) <= tol;

        [CommandMethod("MDH", "MDH_SELFTEST", CommandFlags.Modal)]
        public void SelfTest() => MdhCommands.Run(doc =>
        {
            _pass = _fail = 0;
            Guard("ribbon tab", () => TestRibbon());
            Guard("rebar percentage and table", () => TestRebar(doc));
            Guard("tie and closed hoop", () => TestTie(doc));
            Guard("rectangular column", () => TestRectangular(doc));
            Guard("steel insert", () => TestSteel(doc));
            Guard("wblock to another drawing", () => TestWblock(doc));
            doc.Editor.WriteMessage("\nMDH-SELFTEST SUMMARY pass=" + _pass + " fail=" + _fail);
            Log.Info("MDH-SELFTEST SUMMARY pass=" + _pass + " fail=" + _fail);
        });

        /// <summary>Runs a feature's assertions as ONE reported check; failing assertions are listed in its detail.</summary>
        private static void Guard(string name, Action a)
        {
            _groupFailures = new List<string>();
            try { a(); }
            catch (System.Exception ex) { _groupFailures.Add("threw " + ex.GetType().Name + ": " + ex.Message); Log.Error("selftest " + name, ex); }
            finally
            {
                var failures = _groupFailures;
                _groupFailures = null;
                Report(name, failures.Count == 0, string.Join("; ", failures));
            }
        }

        // ---------------------------------------------------------------- rectangular column

        private static void TestRectangular(Document doc)
        {
            var s = new RectangularSectionModel();
            s.FaceParameters.BarsOnBFace = 5;
            s.FaceParameters.BarsOnHFace = 4;
            var layout = LayoutChangeCoordinator.CommitFaceLayout(s, true);
            Check("rectangular face layout valid", layout.IsValid, layout.Summary);
            Check("rectangular unique physical-bar count", s.LongitudinalBars.Count == 14, s.LongitudinalBars.Count.ToString());
            Check("rectangular restraint audit", RestraintAuditService.Audit(s).UnrestrainedNames.Count == 0,
                RestraintAuditService.Audit(s).Validation.Summary);
            bool geometry = s.TieBars.All(t => RectangularTieGeometryBuilder.Build(s, t).Validation.IsValid);
            Check("rectangular tie geometry valid", geometry);

            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                DrawingGuards.EnsureLayer(tr, doc.Database, "MDH_SECTION", 7);
                DrawingGuards.EnsureLayer(tr, doc.Database, "MDH_REBAR", 1);
                DrawingGuards.EnsureLayer(tr, doc.Database, "MDH_TIE", 2);
                var ids = RectangularSectionWriter.Insert(tr, doc.Database, new RectangularInsertOptions
                {
                    Section = s, InsertionPoint = new Point3d(9000, 2500, 0),
                    Ucs = Matrix3d.Rotation(Math.PI / 7, Vector3d.ZAxis, Point3d.Origin),
                    RotationRadians = Math.PI / 9, Units = UnitContextForTests.Millimetres,
                    SectionLayer = "MDH_SECTION", RebarLayer = "MDH_REBAR", TieLayer = "MDH_TIE"
                });
                var entities = ids.Select(id => tr.GetObject(id, OpenMode.ForRead)).ToList();
                Check("rectangular CAD circles equal actual bars", entities.OfType<Circle>().Count() == s.LongitudinalBars.Count,
                    entities.OfType<Circle>().Count().ToString());
                Check("rectangular CAD output has true arc polylines", entities.OfType<Polyline>().Any(p =>
                    Enumerable.Range(0, p.NumberOfVertices).Any(i => Math.Abs(p.GetBulgeAt(i)) > 1e-9)));
                Check("rectangular CAD output contains geometry only",
                    !entities.Any(e => e is DBText || e is MText || e is Dimension || e is Line));
                tr.Commit();
            }
        }

        [CommandMethod("MDH", "MDH_SELFTEST_RECT", CommandFlags.Modal)]
        public void RectangularSelfTest() => MdhCommands.Run(doc => Guard("rectangular", () => TestRectangular(doc)));

        private static void TestRibbon()
        {
            if (Autodesk.Windows.ComponentManager.Ribbon == null)
            {
                AcApp.DocumentManager.MdiActiveDocument.Editor.WriteMessage("\nMDH-SELFTEST INFO ribbon not available in this session");
                return;
            }
            AcApp.DocumentManager.MdiActiveDocument.Editor.WriteMessage("\nMDH-SELFTEST INFO ribbon tabs before forcing=" + RibbonManager.TabCount());
            RibbonManager.EnsureNow();
            Check("ribbon tab present exactly once", RibbonManager.TabCount() == 1, "count=" + RibbonManager.TabCount());
            Check("ribbon has four tools including MDH_RECT", RibbonManager.ButtonCount() == 4 && RibbonManager.HasButton("MDH_RECT"),
                "buttons=" + RibbonManager.ButtonCount());
            RibbonManager.EnsureNow();
            RibbonManager.Start(); // repeated ensure/start must not add a second tab or handler
            Check("ribbon repeated ensure/start keeps one tab", RibbonManager.TabCount() == 1, "count=" + RibbonManager.TabCount());
        }

        /// <summary>Checks the tab created by the normal Idle path (run after a DELAY, nothing forced).</summary>
        [CommandMethod("MDH", "MDH_SELFTEST_RIBBON", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void RibbonCheck() => MdhCommands.Run(doc =>
        {
            if (Autodesk.Windows.ComponentManager.Ribbon == null) { doc.Editor.WriteMessage("\nMDH-SELFTEST INFO ribbon not available"); return; }
            Guard("ribbon created once at startup with four tools", () =>
            {
                Check("tab count", RibbonManager.TabCount() == 1, "count=" + RibbonManager.TabCount());
                Check("tools", RibbonManager.ButtonCount() == 4 && RibbonManager.HasButton("MDH_RECT"), "buttons=" + RibbonManager.ButtonCount());
            });
        });

        // ---------------------------------------------------------------- rebar

        private static void TestRebar(Document doc)
        {
            var db = doc.Database;
            var units = UnitContextForTests.Millimetres;
            ObjectId boundary;
            var circles = new List<ObjectId>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                var pl = new Polyline();
                var pts = new[] { new Point2d(0, 0), new Point2d(400, 0), new Point2d(400, 600), new Point2d(0, 600) };
                for (int i = 0; i < 4; i++) pl.AddVertexAt(i, pts[i], 0, 0, 0);
                pl.Closed = true;
                boundary = ms.AppendEntity(pl); tr.AddNewlyCreatedDBObject(pl, true);
                void C(double x, double y, double d, double z = 0)
                {
                    var c = new Circle(new Point3d(x, y, z), Vector3d.ZAxis, d / 2);
                    circles.Add(ms.AppendEntity(c)); tr.AddNewlyCreatedDBObject(c, true);
                }
                C(50, 50, 20); C(350, 50, 20); C(50, 550, 20); C(350, 550, 20);
                C(200, 50, 16); C(200, 550, 16);
                C(5, 300, 20);            // crossing the boundary
                C(-100, 300, 20);         // outside
                C(200, 300, 20, 25);      // not coplanar
                tr.Commit();
            }
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var sel = RebarReader.Read(tr, boundary, circles, units);
                Check("rebar non-coplanar circle skipped", sel.Bars.Count == 8 && sel.Notes.Any(n => n.Contains("coplanar")), "bars=" + sel.Bars.Count);
                var placements = sel.Bars.Select(b => sel.Boundary.Classify(b)).ToList();
                Check("rebar crossing/outside identified",
                      placements.Count(p => p == BarPlacement.Inside) == 6 && placements.Count(p => p == BarPlacement.Crossing) == 1 &&
                      placements.Count(p => p == BarPlacement.Outside) == 1);
                var inside = sel.Bars.Where(b => sel.Boundary.Classify(b) == BarPlacement.Inside).ToList();
                var r = RebarCalculator.Calculate(inside, sel.Boundary.Area, targetRhoPercent: 1.0);
                double expectedAs = 4 * Math.PI * 100 + 2 * Math.PI * 64;
                Check("rebar As and rho", Near(r.As, expectedAs, 1e-6) && Near(r.Ag, 240000) && Near(r.RhoPercent, 100 * expectedAs / 240000, 1e-9),
                      "As=" + F(r.As) + " Ag=" + F(r.Ag));

                // Table in a rotated UCS (30Â° about Z).
                var ucs = Matrix3d.Rotation(Math.PI / 6, Vector3d.ZAxis, Point3d.Origin);
                DrawingGuards.EnsureLayer(tr, db, "MDH_REBAR_TABLE", 7);
                var tid = RebarTableWriter.Insert(tr, db, r, new Point3d(600, 0, 0), ucs, MdhApp.Settings.Rebar, "MDH_REBAR_TABLE");
                var table = (Table)tr.GetObject(tid, OpenMode.ForRead);
                int expectedRows = 2 + r.Groups.Count + 3 + 3;
                Check("rebar native table rows", table.Rows.Count == expectedRows && table.Columns.Count == 4, "rows=" + table.Rows.Count);
                Check("rebar table rho cell", table.Cells[2 + r.Groups.Count + 2, 3].TextString == r.RhoPercent.ToString("0.000", CultureInfo.InvariantCulture),
                      table.Cells[2 + r.Groups.Count + 2, 3].TextString);
                Check("rebar table follows UCS", table.Direction.IsEqualTo(Vector3d.XAxis.TransformBy(ucs)), table.Direction.ToString());
                Check("rebar table layer", table.Layer == "MDH_REBAR_TABLE", table.Layer);
                tr.Commit();
            }
        }

        // ---------------------------------------------------------------- tie

        private static void TestTie(Document doc)
        {
            var db = doc.Database;
            var units = UnitContextForTests.Millimetres;
            // Bars in a tilted plane: tests OCS handling (equivalent to an arbitrary rotated UCS).
            var n = new Vector3d(0, 1, 1).GetNormal();
            var toWorld = Matrix3d.PlaneToWorld(n);
            var wa = new Point3d(100, 40, 50).TransformBy(toWorld);
            var wb = new Point3d(400, 120, 50).TransformBy(toWorld);
            ObjectId ida, idb;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                var ca = new Circle(wa, n, 10); var cb = new Circle(wb, n, 12.5);
                ida = ms.AppendEntity(ca); tr.AddNewlyCreatedDBObject(ca, true);
                idb = ms.AppendEntity(cb); tr.AddNewlyCreatedDBObject(cb, true);
                tr.Commit();
            }

            // Two representative cases; every angle/flip combination is covered by the unit tests.
            foreach (var (angle, flip) in new[] { (HookAngle.Deg135, false), (HookAngle.Deg180, true) })
            {
                string tag = "tie " + (int)angle + (flip ? " flipped" : "");
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var ca = (Circle)tr.GetObject(ida, OpenMode.ForRead);
                    var cb = (Circle)tr.GetObject(idb, OpenMode.ForRead);
                    var frame = PlaneFrame.FromCircle(ca, units);
                    Check(tag + " coplanar", frame.Contains(cb, out _));
                    var size = TieBarSize.Find("H10");
                    var input = new TieInput
                    {
                        CenterA = frame.ToCore(ca.Center), CenterB = frame.ToCore(cb.Center),
                        DiameterA = 20, DiameterB = 25, TieDiameter = size.Diameter, Angle = angle, Flip = flip,
                        BendDiameter = VisualBend.Diameter(25), TailLength = 80, Ends = HookEnds.Both // largest bar Ø + 5
                    };
                    var res = TieGeometryBuilder.Build(input);
                    Check(tag + " geometry valid", res.Validation.IsValid, res.Validation.ToString());
                    if (!res.Validation.IsValid) { tr.Abort(); continue; }
                    DrawingGuards.EnsureLayer(tr, db, "MDH_TIE", 1);
                    var pl = TieWriter.CreatePolyline(frame, res, size, "MDH_TIE");
                    TieWriter.Append(tr, db, pl);

                    Check(tag + " lightweight polyline normal", pl.Normal.IsParallelTo(n), pl.Normal.ToString());
                    Check(tag + " vertex count", pl.NumberOfVertices == 6, pl.NumberOfVertices.ToString());
                    double bulge = Math.Tan((int)angle * Math.PI / 180 / 4) * (flip ? 1 : -1);
                    Check(tag + " genuine arc bulges", Near(pl.GetBulgeAt(1), bulge, 1e-12) && Near(pl.GetBulgeAt(3), bulge, 1e-12),
                          F(pl.GetBulgeAt(1)) + "," + F(pl.GetBulgeAt(3)));
                    var arcA = pl.GetArcSegmentAt(1);
                    var arcB = pl.GetArcSegmentAt(3);
                    Check(tag + " hooks concentric with actual bar centres (WCS)", arcA.Center.IsEqualTo(wa, new Tolerance(1e-6, 1e-6)) && arcB.Center.IsEqualTo(wb, new Tolerance(1e-6, 1e-6)),
                          arcA.Center + " vs " + wa);
                    Check(tag + " both hooks use Dref = max(DA, DB)", Near(arcA.Radius, arcB.Radius, 1e-9) && Near(arcA.Radius, (25 + 5) / 2.0, 1e-9), F(arcA.Radius) + "," + F(arcB.Radius));
                    Check(tag + " lineweight H10 = 0.18 mm", pl.LineWeight == LineWeight.LineWeight018, pl.LineWeight.ToString());
                    Check(tag + " plain polyline (no metadata)", pl.ExtensionDictionary.IsNull);
                    tr.Commit();
                }
            }
            TestHoop(doc, n, toWorld);
        }

        /// <summary>Closed hoop around four bars in the same tilted plane: one polyline, EC2 drafting sizes.</summary>
        private static void TestHoop(Document doc, Vector3d n, Matrix3d toWorld)
        {
            var db = doc.Database;
            var units = UnitContextForTests.Millimetres;
            var ocs = new[] { new Point3d(1000, 0, 50), new Point3d(1300, 0, 50), new Point3d(1300, 400, 50), new Point3d(1000, 400, 50) };
            var ids = new List<ObjectId>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                foreach (var p in ocs)
                {
                    var c = new Circle(p.TransformBy(toWorld), n, 12.5);
                    ids.Add(ms.AppendEntity(c)); tr.AddNewlyCreatedDBObject(c, true);
                }
                tr.Commit();
            }
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var circles = ids.Select(id => (Circle)tr.GetObject(id, OpenMode.ForRead)).ToList();
                var frame = PlaneFrame.FromCircle(circles[0], units);
                var size = TieBarSize.Find("H10");
                var input = new HoopInput
                {
                    Bars = circles.Select(c => new HoopBar(frame.ToCore(c.Center), 2 * c.Radius)).ToList(),
                    TieDiameter = size.Diameter, AngleA = HookAngle.Deg135, AngleB = HookAngle.Deg135, ClosureBar = 0,
                    BendDiameter = VisualBend.Diameter(circles.Max(c => 2 * c.Radius)),
                    TailLengthA = Ec2DraftingRules.TailLength(HookAngle.Deg135, size.Diameter),
                    TailLengthB = Ec2DraftingRules.TailLength(HookAngle.Deg135, size.Diameter)
                };
                var res = TieGeometryBuilder.BuildHoop(input);
                Check("hoop geometry valid", res.Validation.IsValid, res.Validation.ToString());
                if (!res.Validation.IsValid) { tr.Abort(); return; }
                DrawingGuards.EnsureLayer(tr, db, "MDH_TIE", 1);
                var pl = TieWriter.CreatePolyline(frame, res, size, "MDH_TIE");
                TieWriter.Append(tr, db, pl);
                Check("hoop is one open polyline in the bar plane", !pl.Closed && pl.Normal.IsParallelTo(n));
                int arcs = Enumerable.Range(0, pl.NumberOfVertices - 1).Count(i => Math.Abs(pl.GetBulgeAt(i)) > 1e-12);
                Check("hoop has 3 corner bends + 2 hooks", arcs == 5, arcs.ToString());
                Check("hoop bend Ø = largest bar Ø + 5 (radius 15)", Near(pl.GetArcSegmentAt(1).Radius, 15, 1e-9), F(pl.GetArcSegmentAt(1).Radius));
                double minToBar = circles.Min(c => pl.GetClosestPointTo(c.Center, false).DistanceTo(c.Center));
                Check("hoop drawn 2.5 mm outside the largest bar", Near(minToBar, 15, 1e-6), F(minToBar));
                Check("hoop plain polyline (no metadata)", pl.ExtensionDictionary.IsNull);
                tr.Commit();
            }
        }

        // ---------------------------------------------------------------- steel

        private static ObjectId InsertSteel(Document doc, SteelSection s, SteelProfile p, Point3d at, InsertionAnchor anchor = InsertionAnchor.Centroid, double rot = 0)
        {
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var id = SteelBlocks.Insert(tr, doc.Database, new SteelInsertOptions
                {
                    Section = s, Profile = p, Anchor = anchor, RotationRadians = rot, CentreAxes = false, Label = false
                }, at, Matrix3d.Identity);
                tr.Commit();
                return id;
            }
        }

        private static void SetDims(Document doc, ObjectId id, params (string k, double v)[] values)
        {
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var br = (BlockReference)tr.GetObject(id, OpenMode.ForWrite);
                var props = SteelBlocks.Properties(br);
                foreach (var (k, v) in values) props[k].Value = v;
                tr.Commit();
            }
        }

        private static T Read<T>(Document doc, ObjectId id, Func<Transaction, BlockReference, T> f)
        {
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var r = f(tr, (BlockReference)tr.GetObject(id, OpenMode.ForRead));
                tr.Commit();
                return r;
            }
        }

        // Dynamic block constraints are evaluated when the command ends, so steel checks are staged:
        // MDH_SELFTEST inserts/edits, MDH_SELFTEST_STEEL2 checks and edits again (the edits are then
        // followed by the normal CommandEnded sync), MDH_SELFTEST_STEEL3 checks the result.
        private static readonly List<(SteelFamily f, SteelProfile p, ObjectId id)> _families = new List<(SteelFamily, SteelProfile, ObjectId)>();
        private static ObjectId _ubA, _ubB, _invalid, _topLeft;

        private static void TestSteel(Document doc)
        {
            var lib = ProfileCache.Library;
            Check("steel profile library loaded", lib.Profiles.Count >= 12, lib.Profiles.Count.ToString());
            _families.Clear();
            double x = 3000;
            foreach (var f in SteelFamilies.All)
            {
                var p = lib.ForFamily(f).First();
                _families.Add((f, p, InsertSteel(doc, p.Section, p, new Point3d(x, 0, 0))));
                x += 600;
            }
            var ub = lib.FindById("uk-ub-406x178x60");
            _ubA = InsertSteel(doc, ub.Section, ub, new Point3d(3000, 2000, 0));
            _ubB = InsertSteel(doc, ub.Section, ub, new Point3d(3600, 2000, 0));
            SetDims(doc, _ubA, ("tw", 9.5), ("H", 450.0));
            var rhs = lib.FindById("en-rhs-200x100x8-hf");
            _invalid = InsertSteel(doc, rhs.Section, rhs, new Point3d(3000, 4000, 0));
            _topLeft = InsertSteel(doc, rhs.Section, rhs, new Point3d(3000, 6000, 0), InsertionAnchor.TopLeft);
        }

        [CommandMethod("MDH", "MDH_SELFTEST_STEEL2", CommandFlags.Modal)]
        public void Steel2() => MdhCommands.Run(doc => Guard("steel blocks follow parameters, sync and anchor", () =>
        {
            foreach (var (f, p, id) in _families)
            {
                var ok = Read(doc, id, (tr, br) =>
                {
                    var s = SteelBlocks.ReadSection(br, f);
                    bool dims = SteelFamilies.Parameters(f).All(k => Near(s[k], p.Section[k], 1e-9));
                    var metadata = MetadataStore.Read(tr, br, SteelMetadata.Key);
                    var st = SteelSync.Evaluate(tr, br, metadata);
                    bool geometryOnly = string.IsNullOrEmpty(metadata?["axes"]) && string.IsNullOrEmpty(metadata?["label"]);
                    return (dims, st.Status, st.Match?.Id, br.IsDynamicBlock, SteelBlocks.EffectiveName(tr, br), geometryOnly);
                });
                Check("steel " + f + " inserted as dynamic block " + ok.Item5, ok.Item4 && ok.Item5 == SteelFamilies.BlockName(f));
                Check("steel " + f + " native properties hold profile dimensions", ok.dims);
                Check("steel " + f + " geometry follows parameters and matches profile", ok.Status == SteelStatus.Profile && ok.Item3 == p.Id, ok.Status + " " + ok.Item3);
                Check("steel " + f + " inserts no annotation companions", ok.geometryOnly);
            }

            var ub = ProfileCache.Library.FindById("uk-ub-406x178x60");
            var a = Read(doc, _ubA, (tr, br) => (SteelBlocks.ReadSection(br, SteelFamily.UB), MetadataStore.Read(tr, br, SteelMetadata.Key)["status"]));
            var b = Read(doc, _ubB, (tr, br) => SteelBlocks.ReadSection(br, SteelFamily.UB));
            Check("steel edited instance only changes itself", Near(a.Item1["H"], 450) && Near(b["H"], ub.Section["H"]), F(a.Item1["H"]) + "/" + F(b["H"]));
            Check("steel edit followed by sync: Custom status", a.Item2 == "custom", a.Item2);

            var tlPos = Read(doc, _topLeft, (tr, br) => br.Position);
            Check("steel top-left anchor", tlPos.IsEqualTo(new Point3d(3000 + 50, 6000 - 100, 0)), tlPos.ToString());

            // Next stage: restore UB dimensions; make the RHS impossible (t too large for B).
            SetDims(doc, _ubA, ("tw", ub.Section["tw"]), ("H", ub.Section["H"]));
            SetDims(doc, _invalid, ("t", 60.0));
        }));

        [CommandMethod("MDH", "MDH_SELFTEST_STEEL3", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void Steel3() => MdhCommands.Run(doc => Guard("steel re-match and invalid detection", () =>
        {
            var ub = ProfileCache.Library.FindById("uk-ub-406x178x60");
            var back = Read(doc, _ubA, (tr, br) => SteelSync.Evaluate(tr, br, MetadataStore.Read(tr, br, SteelMetadata.Key)));
            Check("steel restoring dimensions re-matches the profile", back.Status == SteelStatus.Profile && back.Match?.Id == ub.Id,
                back.Status + " / " + back.Match?.Id);
            var inv = Read(doc, _invalid, (tr, br) => (SteelSync.Evaluate(tr, br, MetadataStore.Read(tr, br, SteelMetadata.Key)), MetadataStore.Read(tr, br, SteelMetadata.Key)["status"]));
            Check("steel impossible t is reported invalid, not trusted", inv.Item1.Status == SteelStatus.Invalid && inv.Item2 == "invalid",
                  inv.Item1.Status + " / " + inv.Item2 + " " + inv.Item1.Problem);
        }));

        // ---------------------------------------------------------------- WBLOCK to another drawing

        private static void TestWblock(Document doc)
        {
            var rhs = ProfileCache.Library.FindById("en-rhs-250x150x10-hf");
            var id = InsertSteel(doc, rhs.Section, rhs, new Point3d(6000, 0, 0));
            SetDims(doc, id, ("B", 170.0));
            var path = Path.Combine(Path.GetTempPath(), "MDH_selftest_wblock.dwg");
            using (var side = doc.Database.Wblock(new ObjectIdCollection { id }, Point3d.Origin))
                side.SaveAs(path, DwgVersion.Current);

            using (var other = new Database(false, true))
            {
                other.ReadDwgFile(path, FileOpenMode.OpenForReadAndAllShare, true, "");
                other.CloseInput(true);
                using (var tr = other.TransactionManager.StartTransaction())
                {
                    var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(other), OpenMode.ForRead);
                    var br = ms.Cast<ObjectId>().Select(o => tr.GetObject(o, OpenMode.ForRead)).OfType<BlockReference>().FirstOrDefault();
                    Check("wblock keeps a dynamic block", br != null && br.IsDynamicBlock);
                    if (br != null)
                    {
                        var s = SteelBlocks.ReadSection(br, SteelFamily.RHS);
                        Check("wblock keeps edited parameter values", Near(s["B"], 170) && Near(s["H"], 250), s.DimensionText());
                        Check("wblock keeps metadata", MetadataStore.Read(tr, br, SteelMetadata.Key)?["family"] == "RHS");
                    }
                    tr.Commit();
                }
            }
        }

        // ---------------------------------------------------------------- undo / redo across commands

        [CommandMethod("MDH", "MDH_SELFTEST_UNDO1", CommandFlags.Modal)]
        public void Undo1() => MdhCommands.Run(doc =>
        {
            var rhs = ProfileCache.Library.FindById("en-rhs-200x100x8-hf");
            _undoBlock = InsertSteel(doc, rhs.Section, rhs, new Point3d(8000, 0, 0));
            doc.Editor.WriteMessage("\nMDH-SELFTEST INFO undo block inserted");
        });

        [CommandMethod("MDH", "MDH_SELFTEST_UNDO2", CommandFlags.Modal)]
        public void Undo2() => MdhCommands.Run(doc =>
        {
            SetDims(doc, _undoBlock, ("B", 155.0)); // 155 is not a catalogue width, so the label must turn Custom
            doc.Editor.WriteMessage("\nMDH-SELFTEST INFO undo block B set to 155");
        });

        /// <summary>Checks B and the label without creating an undo marker (so REDO still applies).</summary>
        [CommandMethod("MDH", "MDH_SELFTEST_UNDOCHECK", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void UndoCheck() => MdhCommands.Run(doc =>
        {
            var ed = doc.Editor;
            var pr = ed.GetString("\nExpected B: ");
            if (pr.Status != Autodesk.AutoCAD.EditorInput.PromptStatus.OK) return;
            double expect = double.Parse(pr.StringResult, CultureInfo.InvariantCulture);
            var r = Read(doc, _undoBlock, (tr, br) =>
            {
                var s = SteelBlocks.ReadSection(br, SteelFamily.RHS);
                var st = SteelSync.Evaluate(tr, br, MetadataStore.Read(tr, br, SteelMetadata.Key));
                return (s["B"], st.Status);
            });
            Guard("undo/redo at B=" + F(expect), () =>
            {
                Check("B", Near(r.Item1, expect), "B=" + F(r.Item1));
                Check("geometry consistent", r.Status != SteelStatus.Invalid, r.Status.ToString());
            });
        });

        [CommandMethod("MDH", "MDH_SELFTEST_REOPEN", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void Reopen() => MdhCommands.Run(doc =>
        {
            int n = 0, dyn = 0, meta = 0, invalid = 0;
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(doc.Database), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    if (!(tr.GetObject(id, OpenMode.ForRead) is BlockReference br) || !br.IsDynamicBlock) continue;
                    if (!SteelBlocks.IsTemplateName(SteelBlocks.EffectiveName(tr, br))) continue;
                    n++; dyn++;
                    var m = MetadataStore.Read(tr, br, SteelMetadata.Key);
                    if (m != null) meta++;
                    var st = SteelSync.Evaluate(tr, br, m);
                    if (st != null && st.Status == SteelStatus.Invalid) invalid++;
                }
                tr.Commit();
            }
            Guard("reopen keeps steel dynamic blocks and metadata", () =>
            {
                Check("blocks", n > 0 && dyn == n, "n=" + n);
                Check("metadata", meta == n, meta + "/" + n);
            });
            doc.Editor.WriteMessage("\nMDH-SELFTEST INFO reopen invalid-geometry instances=" + invalid);
        });
    }

    internal static class UnitContextForTests
    {
        public static UnitContext Millimetres => UnitContext.Millimetres();
    }
}
