using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Colors;

namespace MDraftingHeper.AutoCAD2021.Drawing
{
    /// <summary>A user-facing failure: shown as a concise message, not logged as a crash.</summary>
    public sealed class MdhUserException : Exception
    {
        public MdhUserException(string message) : base(message) { }
    }

    /// <summary>
    /// How drawing units relate to the millimetres used by all MDraftingHeper geometry.
    /// INSUNITS is read, never changed. Non-mm drawings need an explicit choice per document.
    /// </summary>
    public sealed class UnitContext
    {
        public double MmPerUnit { get; }
        public string Description { get; }

        private UnitContext(double mmPerUnit, string description) { MmPerUnit = mmPerUnit; Description = description; }

        public double ToMm(double drawingValue) => drawingValue * MmPerUnit;
        public double ToDrawing(double mm) => mm / MmPerUnit;
        public bool IsMillimetres => Math.Abs(MmPerUnit - 1) < 1e-12;

        private static readonly Dictionary<Document, UnitContext> Choices = new Dictionary<Document, UnitContext>();

        /// <summary>Millimetre interpretation without prompting (self-tests build millimetre geometry).</summary>
        internal static UnitContext Millimetres() => new UnitContext(1, "Drawing units: millimetres.");

        public static void Forget(Document doc) => Choices.Remove(doc);

        private static bool TryKnown(int insunits, out double mmPerUnit, out string name)
        {
            switch (insunits)
            {
                case 4: mmPerUnit = 1; name = "millimetres"; return true;
                case 5: mmPerUnit = 10; name = "centimetres"; return true;
                case 6: mmPerUnit = 1000; name = "metres"; return true;
                case 1: mmPerUnit = 25.4; name = "inches"; return true;
                case 2: mmPerUnit = 304.8; name = "feet"; return true;
                default: mmPerUnit = double.NaN; name = null; return false;
            }
        }

        /// <summary>
        /// Returns the unit interpretation, prompting when INSUNITS is not millimetres.
        /// Returns null when the user cancels or the units are unsupported.
        /// <paramref name="mmOnly"/> restricts the tool to drawings interpreted as millimetres.
        /// </summary>
        public static UnitContext Resolve(Document doc, bool mmOnly = false)
        {
            int ins = Convert.ToInt32(doc.Database.Insunits);
            if (ins == 4) return new UnitContext(1, "Drawing units: millimetres.");
            if (Choices.TryGetValue(doc, out var known) && (!mmOnly || known.IsMillimetres)) return known;

            var ed = doc.Editor;
            if (ins == 0)
            {
                var po = new PromptKeywordOptions("\nDrawing units are not set (INSUNITS=0). Interpret 1 drawing unit as [Millimetre/Cancel]: ", "Millimetre Cancel");
                po.AllowNone = false;
                var r = ed.GetKeywords(po);
                if (r.Status != PromptStatus.OK || r.StringResult != "Millimetre") return null;
                return Choices[doc] = new UnitContext(1, "Drawing units unset (INSUNITS=0); interpreted as millimetres by user choice.");
            }

            if (!TryKnown(ins, out double factor, out string unitName))
            {
                ed.WriteMessage("\nMDraftingHeper supports millimetre, centimetre, metre, inch and foot drawings. INSUNITS=" + ins + " is not supported.");
                return null;
            }
            if (mmOnly)
            {
                ed.WriteMessage("\nThis tool inserts millimetre Dynamic Blocks and requires a millimetre drawing (INSUNITS=" + ins + ", " + unitName +
                                "). Use a millimetre drawing; INSUNITS is not changed automatically.");
                return null;
            }
            var ko = new PromptKeywordOptions("\nDrawing units are " + unitName + " (INSUNITS=" + ins + "). Convert to/from millimetres for this drawing [Convert/Cancel]: ", "Convert Cancel");
            ko.AllowNone = false;
            var kr = ed.GetKeywords(ko);
            if (kr.Status != PromptStatus.OK || kr.StringResult != "Convert") return null;
            return Choices[doc] = new UnitContext(factor, "Drawing units: " + unitName + " (converted, 1 unit = " + factor + " mm).");
        }
    }

    public static class DrawingGuards
    {
        public static void EnsureWritable(Document doc)
        {
            if (doc.IsReadOnly)
                throw new MdhUserException("This drawing is open read-only. Reopen it with write access before adding MDraftingHeper objects.");
        }

        /// <summary>Creates the layer if missing; refuses locked or frozen layers with an actionable message.</summary>
        public static ObjectId EnsureLayer(Transaction tr, Database db, string name, short colorIndex, LineWeight? lw = null)
        {
            if (string.IsNullOrWhiteSpace(name)) name = "0";
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(name))
            {
                var id = lt[name];
                var ltr = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead);
                if (ltr.IsLocked)
                    throw new MdhUserException("Layer '" + name + "' is locked. Unlock it, or change the MDraftingHeper layer setting, and try again.");
                if (ltr.IsFrozen)
                    throw new MdhUserException("Layer '" + name + "' is frozen. Thaw it, or change the MDraftingHeper layer setting, and try again.");
                return id;
            }
            SymbolUtilityServices.ValidateSymbolName(name, false);
            lt.UpgradeOpen();
            var rec = new LayerTableRecord { Name = name, Color = Color.FromColorIndex(ColorMethod.ByAci, colorIndex) };
            if (lw.HasValue) rec.LineWeight = lw.Value;
            var newId = lt.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
            return newId;
        }
    }
}
