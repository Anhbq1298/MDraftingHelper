using System;
using Autodesk.AutoCAD.DatabaseServices;
using Polyline = Autodesk.AutoCAD.DatabaseServices.Polyline;
using MDraftingHeper.Core.Hooks;

namespace MDraftingHeper.AutoCAD2021.Drawing
{
    /// <summary>Writes tie / hoop centrelines as plain 2D lightweight polylines (no extra metadata).</summary>
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
        /// An unsaved polyline with genuine arc bulges from the Core result. Pass a null layer for previews:
        /// the layer may not exist yet and previews must not write to the drawing.
        /// </summary>
        public static Polyline CreatePolyline(PlaneFrame frame, TieResult result, TieBarSize size, string layer)
        {
            var pl = frame.ToPolyline(result.Path);
            if (layer != null) pl.Layer = layer;
            pl.LineWeight = LineweightFor(size);
            return pl;
        }

        /// <summary>Appends the polyline to the current space. Caller owns the transaction (one undo step).</summary>
        public static ObjectId Append(Transaction tr, Database db, Polyline pl)
        {
            var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
            var id = space.AppendEntity(pl);
            tr.AddNewlyCreatedDBObject(pl, true);
            return id;
        }
    }
}
