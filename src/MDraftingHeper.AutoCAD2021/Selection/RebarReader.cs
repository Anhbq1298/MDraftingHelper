using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using MDraftingHeper.AutoCAD2021.Drawing;
using MDraftingHeper.Core.Geometry;
using MDraftingHeper.Core.Rebar;

namespace MDraftingHeper.AutoCAD2021.Selection
{
    public sealed class RebarSelection
    {
        public SectionBoundary Boundary { get; set; }
        public List<RebarBar> Bars { get; } = new List<RebarBar>();
        public List<string> Notes { get; } = new List<string>();
        public PlaneFrame Frame { get; set; }
    }

    /// <summary>Converts selected CAD entities into Core inputs (millimetres, section plane coordinates).</summary>
    public static class RebarReader
    {
        public static RebarSelection Read(Transaction tr, ObjectId boundaryId, IEnumerable<ObjectId> circleIds, UnitContext units)
        {
            var sel = new RebarSelection();
            var ent = tr.GetObject(boundaryId, OpenMode.ForRead) as Entity;
            Path2 path;
            switch (ent)
            {
                case Polyline pl:
                    sel.Frame = PlaneFrame.FromPolyline(pl, units);
                    path = sel.Frame.FromPolyline(pl, out bool closed);
                    if (!closed) throw new MdhUserException("The boundary polyline is not closed. Close it (PEDIT > Close) or select another boundary.");
                    if (pl.Thickness != 0) sel.Notes.Add("Boundary thickness is ignored; only the outline is used.");
                    break;
                case Circle c:
                    sel.Frame = PlaneFrame.FromCircle(c, units);
                    var centre = sel.Frame.ToCore(c.Center);
                    double r = units.ToMm(c.Radius);
                    path = new Path2(new[]
                    {
                        new PathVertex(new Point2(centre.X + r, centre.Y), 1),
                        new PathVertex(new Point2(centre.X - r, centre.Y), 1)
                    }, true);
                    break;
                default:
                    throw new MdhUserException("The boundary must be a closed lightweight polyline (or a circle for a circular section).");
            }

            var v = SectionBoundary.TryCreate(path, out var boundary);
            if (!v.IsValid) throw new MdhUserException("Invalid boundary: " + string.Join(" ", v.Errors));
            sel.Boundary = boundary;

            int skipped = 0;
            foreach (var id in circleIds.Distinct())
            {
                if (id == boundaryId) continue;
                var c = tr.GetObject(id, OpenMode.ForRead) as Circle;
                if (c == null) continue;
                if (!sel.Frame.Contains(c, out var why))
                {
                    skipped++;
                    continue;
                }
                sel.Bars.Add(new RebarBar(c.Handle.ToString(), sel.Frame.ToCore(c.Center), units.ToMm(2 * c.Radius)));
            }
            if (skipped > 0)
                sel.Notes.Add(skipped + " circle(s) are not coplanar with the boundary and were NOT counted.");
            return sel;
        }
    }
}
