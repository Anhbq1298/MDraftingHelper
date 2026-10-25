using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using MDraftingHeper.AutoCAD2021.App;
using MDraftingHeper.AutoCAD2021.Blocks;
using MDraftingHeper.AutoCAD2021.Drawing;
using MDraftingHeper.Core.Steel;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace MDraftingHeper.AutoCAD2021.Metadata
{
    public enum SteelStatus { Profile, Custom, Invalid }

    public sealed class SteelState
    {
        public SteelFamily Family;
        public SteelSection Section;
        public SteelProfile Match;
        public SteelStatus Status;
        public string Problem;
    }

    /// <summary>
    /// Keeps steel metadata and labels consistent with the native dynamic properties, whichever way
    /// they were changed (Ctrl+1, grips, commands). ObjectModified only records ids; updates run later
    /// at CommandEnded or Idle, outside the notification. Changes made by Undo/Redo are never followed
    /// by writes (that would break the redo chain); the label is restored by the undo itself.
    /// </summary>
    public static class SteelSync
    {
        private sealed class DocState
        {
            public readonly HashSet<ObjectId> Pending = new HashSet<ObjectId>();
            public bool InUndo;
            public bool ReconcileOnOpen = true;
        }

        private static readonly Dictionary<Document, DocState> States = new Dictionary<Document, DocState>();
        private static readonly HashSet<string> UndoCommands =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "U", "UNDO", "REDO", "MREDO", "OOPS" };
        private static bool _started, _processing, _idleHooked;

        public static void Start()
        {
            if (_started) return;
            _started = true;
            foreach (Document d in AcApp.DocumentManager) Attach(d);
            AcApp.DocumentManager.DocumentCreated += OnDocumentCreated;
            AcApp.DocumentManager.DocumentToBeDestroyed += OnDocumentToBeDestroyed;
            HookIdle();
        }

        public static void Stop()
        {
            if (!_started) return;
            _started = false;
            AcApp.DocumentManager.DocumentCreated -= OnDocumentCreated;
            AcApp.DocumentManager.DocumentToBeDestroyed -= OnDocumentToBeDestroyed;
            foreach (var d in States.Keys.ToList()) Detach(d);
            if (_idleHooked) { AcApp.Idle -= OnIdle; _idleHooked = false; }
        }

        private static void OnDocumentCreated(object s, DocumentCollectionEventArgs e) { Attach(e.Document); HookIdle(); }
        private static void OnDocumentToBeDestroyed(object s, DocumentCollectionEventArgs e) { Detach(e.Document); UnitContext.Forget(e.Document); }

        private static void Attach(Document doc)
        {
            if (doc == null || States.ContainsKey(doc)) return;
            States[doc] = new DocState();
            doc.Database.ObjectModified += OnObjectModified;
            doc.CommandWillStart += OnCommandWillStart;
            doc.CommandEnded += OnCommandEnded;
            doc.CommandCancelled += OnCommandEnded;
            doc.CommandFailed += OnCommandEnded;
        }

        private static void Detach(Document doc)
        {
            if (!States.Remove(doc)) return;
            doc.Database.ObjectModified -= OnObjectModified;
            doc.CommandWillStart -= OnCommandWillStart;
            doc.CommandEnded -= OnCommandEnded;
            doc.CommandCancelled -= OnCommandEnded;
            doc.CommandFailed -= OnCommandEnded;
        }

        private static DocState StateOf(Database db)
        {
            foreach (var kv in States) if (kv.Key.Database == db) return kv.Value;
            return null;
        }

        private static void OnObjectModified(object sender, ObjectEventArgs e)
        {
            if (_processing) return;
            if (!(e.DBObject is BlockReference br) || br.ExtensionDictionary.IsNull) return;
            var st = StateOf(e.DBObject.Database);
            if (st == null || st.InUndo) return;
            st.Pending.Add(br.ObjectId);
            HookIdle();
        }

        private static void OnCommandWillStart(object sender, CommandEventArgs e)
        {
            var doc = (Document)sender;
            if (States.TryGetValue(doc, out var st) && UndoCommands.Contains(e.GlobalCommandName)) st.InUndo = true;
        }

        private static void OnCommandEnded(object sender, CommandEventArgs e)
        {
            var doc = (Document)sender;
            if (!States.TryGetValue(doc, out var st)) return;
            if (st.InUndo)
            {
                st.InUndo = false;
                st.Pending.Clear();
                return;
            }
            if (st.Pending.Count == 0) return;
            // Follow the edit at the end of the command that made it, so the refresh belongs to the
            // same user action. Edits made outside any command are picked up at Idle instead.
            try
            {
                var ids = st.Pending.ToList();
                st.Pending.Clear();
                Process(doc, ids);
            }
            catch (System.Exception ex)
            {
                Log.Error("Steel sync at command end failed", ex);
            }
        }

        private static void HookIdle()
        {
            if (_idleHooked || !_started) return;
            _idleHooked = true;
            AcApp.Idle += OnIdle;
        }

        private static void OnIdle(object sender, EventArgs e)
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null || !States.TryGetValue(doc, out var st)) return;
            if (!string.IsNullOrEmpty(doc.CommandInProgress)) return;
            AcApp.Idle -= OnIdle;
            _idleHooked = false;
            try
            {
                if (st.ReconcileOnOpen)
                {
                    st.ReconcileOnOpen = false;
                    ReconcileAll(doc, onlyIfChanged: true);
                }
                if (st.Pending.Count > 0)
                {
                    var ids = st.Pending.ToList();
                    st.Pending.Clear();
                    Process(doc, ids);
                }
            }
            catch (System.Exception ex)
            {
                Log.Error("Steel sync failed", ex);
            }
            // Other open documents get their turn when they become active.
            if (States.Any(kv => kv.Value.Pending.Count > 0 || kv.Value.ReconcileOnOpen)) HookIdle();
        }

        /// <summary>Reconciles every MDraftingHeper steel block in the drawing; returns the count updated.</summary>
        public static int ReconcileAll(Document doc, bool onlyIfChanged)
        {
            var ids = new List<ObjectId>();
            using (var tr = doc.Database.TransactionManager.StartOpenCloseTransaction())
            {
                var bt = (BlockTable)tr.GetObject(doc.Database.BlockTableId, OpenMode.ForRead);
                foreach (var name in SteelBlocks.TemplateNames)
                {
                    if (!bt.Has(name)) continue;
                    var def = (BlockTableRecord)tr.GetObject(bt[name], OpenMode.ForRead);
                    foreach (ObjectId rid in def.GetBlockReferenceIds(true, false)) ids.Add(rid);
                    foreach (ObjectId anon in def.GetAnonymousBlockIds())
                    {
                        var a = (BlockTableRecord)tr.GetObject(anon, OpenMode.ForRead);
                        foreach (ObjectId rid in a.GetBlockReferenceIds(true, false)) ids.Add(rid);
                    }
                }
                tr.Commit();
            }
            return ids.Count == 0 ? 0 : Process(doc, ids.Distinct().ToList());
        }

        private static int Process(Document doc, List<ObjectId> ids)
        {
            if (doc.IsReadOnly) return 0;
            int updated = 0;
            _processing = true;
            try
            {
                using (doc.LockDocument())
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    foreach (var id in ids)
                    {
                        if (id.IsErased || !id.IsValid) continue;
                        if (tr.GetObject(id, OpenMode.ForRead) is BlockReference br && Update(tr, br)) updated++;
                    }
                    tr.Commit();
                }
            }
            finally
            {
                _processing = false;
            }
            return updated;
        }

        /// <summary>Evaluates the reference from its current dynamic properties and geometry.</summary>
        public static SteelState Evaluate(Transaction tr, BlockReference br, MetadataRecord meta)
        {
            if (!SteelMetadata.TryFamily(meta, out var family)) return null;
            var state = new SteelState { Family = family, Section = SteelBlocks.ReadSection(br, family) };
            var v = SteelValidator.Validate(state.Section);
            if (!v.IsValid)
            {
                state.Status = SteelStatus.Invalid;
                state.Problem = string.Join(" ", v.Errors);
                return state;
            }
            // AutoCAD keeps the last solvable geometry when a value cannot be solved, so compare
            // the displayed extents with the dimensions before trusting them.
            if (!GeometryMatches(tr, br, state.Section))
            {
                state.Status = SteelStatus.Invalid;
                state.Problem = "The block geometry does not match its parameters (AutoCAD could not solve the constraints).";
                return state;
            }
            double tol = MdhApp.Settings.Steel.MatchTolerance > 0 ? MdhApp.Settings.Steel.MatchTolerance : 0.01;
            state.Match = ProfileCache.Library.Match(state.Section, tol, meta["sourceProfileId"]);
            state.Status = state.Match != null ? SteelStatus.Profile : SteelStatus.Custom;
            return state;
        }

        private static bool GeometryMatches(Transaction tr, BlockReference br, SteelSection s)
        {
            var btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            bool any = false;
            foreach (ObjectId eid in btr)
            {
                // Only drafted curves count; constraint dimensions and the origin point are not curves.
                if (!(tr.GetObject(eid, OpenMode.ForRead) is Curve c)) continue;
                Extents3d ext;
                try { ext = c.GeometricExtents; } catch (Autodesk.AutoCAD.Runtime.Exception) { continue; }
                minX = Math.Min(minX, ext.MinPoint.X); minY = Math.Min(minY, ext.MinPoint.Y);
                maxX = Math.Max(maxX, ext.MaxPoint.X); maxY = Math.Max(maxY, ext.MaxPoint.Y);
                any = true;
            }
            if (!any) return false;
            var (hw, hh) = SteelCompanions.HalfExtents(s);
            const double tol = 1e-3;
            return Math.Abs(minX + hw) < tol && Math.Abs(maxX - hw) < tol && Math.Abs(minY + hh) < tol && Math.Abs(maxY - hh) < tol;
        }

        /// <summary>Writes metadata/label/axes only when they differ from the evaluated state.</summary>
        public static bool Update(Transaction tr, BlockReference br)
        {
            var meta = MetadataStore.Read(tr, br, SteelMetadata.Key);
            var state = Evaluate(tr, br, meta);
            if (state == null) return false;

            var next = new MetadataRecord(SteelMetadata.Schema);
            foreach (var kv in meta.Entries) next[kv.Key] = kv.Value;
            next["status"] = state.Status.ToString().ToLowerInvariant();
            next["matchedProfileId"] = state.Match?.Id ?? "";
            next["currentDims"] = SteelMetadata.DimsText(state.Section);
            next["problem"] = state.Problem ?? "";

            bool changed = !next.SameAs(meta);
            if (changed) MetadataStore.Write(tr, br, SteelMetadata.Key, next);

            if (state.Status != SteelStatus.Invalid)
            {
                var db = br.Database;
                var label = Resolve(db, meta["label"]);
                if (label.Count == 1)
                    SteelCompanions.UpdateLabel(tr, br, state.Section, label[0], SteelMetadata.LabelText(state.Section, state.Match));
                var axes = Resolve(db, meta["axes"]);
                if (axes.Count == 2) SteelCompanions.UpdateAxes(tr, br, state.Section, axes);
            }
            return changed;
        }

        private static List<ObjectId> Resolve(Database db, string handles)
        {
            var ids = new List<ObjectId>();
            if (string.IsNullOrWhiteSpace(handles)) return ids;
            foreach (var h in handles.Split(','))
            {
                try
                {
                    var id = db.GetObjectId(false, new Handle(Convert.ToInt64(h, 16)), 0);
                    if (id.IsValid && !id.IsErased) ids.Add(id);
                }
                catch (Autodesk.AutoCAD.Runtime.Exception) { /* companion was deleted by the user */ }
                catch (FormatException) { }
            }
            return ids;
        }
    }
}
