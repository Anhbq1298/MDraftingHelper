using System;
using System.Globalization;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.GraphicsInterface;
using Autodesk.AutoCAD.Geometry;
using Polyline = Autodesk.AutoCAD.DatabaseServices.Polyline;
using MDraftingHeper.AutoCAD2021.Metadata;
using MDraftingHeper.Core.Hooks;

namespace MDraftingHeper.AutoCAD2021.Drawing
{
    public static class TieWriter
    {
        public static LineWeight LineweightFor(TieBarSize size)
        {
            switch (size.LineweightHundredthsMm)
            {
                case 18: return LineWeight.LineWeight018;
                case 20: return LineWeight.LineWeight020;
                case 25: return LineWeight.LineWeight025;
                case 30: return LineWeight.LineWeight030;
                default: throw new ArgumentOutOfRangeException(nameof(size), "No AutoCAD lineweight for " + size.LineweightHundredthsMm);
            }
        }

        /// <summary>
        /// An unsaved polyline for preview or output; geometry from the Core result. Pass a null layer
        /// for previews: the layer may not exist yet and previews must not write to the drawing.
        /// </summary>
        public static Polyline CreatePolyline(PlaneFrame frame, TieResult result, TieBarSize size, string layer)
        {
            var pl = frame.ToPolyline(result.Path);
            if (layer != null) pl.Layer = layer;
            pl.LineWeight = LineweightFor(size);
            return pl;
        }

        /// <summary>Appends the tie and its metadata. Caller owns the transaction (one undo step).</summary>
        public static ObjectId Append(Transaction tr, Database db, Polyline pl, TieInput input, TieResult result, TieBarSize size, string presetId)
        {
            var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
            var id = space.AppendEntity(pl);
            tr.AddNewlyCreatedDBObject(pl, true);
            MetadataStore.Write(tr, pl, TieMetadata.Key, TieMetadata.Create(input, result, size, presetId));
            return id;
        }
    }

    public static class TieMetadata
    {
        public const string Key = "MDH_TIE";

        public static MetadataRecord Create(TieInput i, TieResult r, TieBarSize size, string presetId)
        {
            string N(double v) => v.ToString("R", CultureInfo.InvariantCulture);
            var m = new MetadataRecord(1);
            m["tieSize"] = size.Name;
            m["angle"] = ((int)i.Angle).ToString(CultureInfo.InvariantCulture);
            m["ends"] = i.Ends.ToString();
            m["flip"] = i.Flip ? "1" : "0";
            m["preset"] = presetId ?? "";
            m["dA"] = N(i.DiameterA);
            m["dB"] = N(i.DiameterB);
            m["dRef"] = N(r.ReferenceDiameter);
            m["rCentreline"] = N(r.CentrelineRadius);
            m["insideMandrel"] = N(i.InsideMandrelDiameter);
            m["tail"] = N(i.TailLength);
            m["clearGap"] = N(i.ClearGap);
            m["note"] = "Snapshot of the hook settings; the tie does not follow moved bars.";
            return m;
        }
    }

    /// <summary>
    /// Shows the tie as transient graphics and asks Accept/Flip. No database writes happen here;
    /// every exit path erases the transient so Esc leaves nothing behind.
    /// </summary>
    public sealed class TiePreview : IDisposable
    {
        private Polyline _shown;

        public void Show(Polyline pl)
        {
            Clear();
            _shown = pl;
            TransientManager.CurrentTransientManager.AddTransient(_shown, TransientDrawingMode.DirectShortTerm, 128, new IntegerCollection());
        }

        public void Clear()
        {
            if (_shown == null) return;
            TransientManager.CurrentTransientManager.EraseTransient(_shown, new IntegerCollection());
            _shown.Dispose();
            _shown = null;
        }

        /// <summary>Loops until Accept (true) or Esc (false); <paramref name="flip"/> rebuilds the preview.</summary>
        public static bool Confirm(Editor ed, Func<Polyline> build, Action flip)
        {
            using (var preview = new TiePreview())
            {
                while (true)
                {
                    var pl = build();
                    if (pl == null) return false;
                    preview.Show(pl);
                    var ko = new PromptKeywordOptions("\nAccept tie or [Accept/Flip] <Accept>: ", "Accept Flip") { AllowNone = true };
                    var res = ed.GetKeywords(ko);
                    if (res.Status == PromptStatus.None || (res.Status == PromptStatus.OK && res.StringResult == "Accept")) return true;
                    if (res.Status == PromptStatus.OK && res.StringResult == "Flip") { flip(); continue; }
                    return false;
                }
            }
        }

        public void Dispose() => Clear();
    }
}
