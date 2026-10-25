using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using MDraftingHeper.Core.Geometry;
using MDraftingHeper.Core.Hooks;
using MDraftingHeper.Core.Rectangular;

namespace MDraftingHeper.AutoCAD2021.Drawing
{
    public sealed class RectangularInsertOptions
    {
        public RectangularSectionModel Section { get; set; }
        public Point3d InsertionPoint { get; set; }
        public Matrix3d Ucs { get; set; }
        public double RotationRadians { get; set; }
        public UnitContext Units { get; set; }
        public string SectionLayer { get; set; }
        public string RebarLayer { get; set; }
        public string TieLayer { get; set; }
    }

    /// <summary>Writes the same Core paths used by the WPF preview. Caller owns one transaction/undo step.</summary>
    public static class RectangularSectionWriter
    {
        public static IReadOnlyList<ObjectId> Insert(Transaction tr, Database db, RectangularInsertOptions o)
        {
            if (o?.Section == null) throw new ArgumentNullException(nameof(o));
            var normal = o.Ucs.CoordinateSystem3d.Zaxis.GetNormal();
            double elevation = o.InsertionPoint.TransformBy(Matrix3d.WorldToPlane(normal)).Z;
            var frame = new PlaneFrame(normal, elevation, o.Units);
            var basePoint = frame.ToCore(o.InsertionPoint);
            var ux = o.Ucs.CoordinateSystem3d.Xaxis.TransformBy(Matrix3d.WorldToPlane(normal));
            double angle = Math.Atan2(ux.Y, ux.X) + o.RotationRadians;
            Point2 Map(Point2 p)
            {
                double c = Math.Cos(angle), s = Math.Sin(angle);
                return new Point2(basePoint.X + p.X * c - p.Y * s, basePoint.Y + p.X * s + p.Y * c);
            }
            Path2 MapPath(Path2 path) => path.Transform(Map);

            var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
            var added = new List<ObjectId>();
            void Append(Entity e)
            {
                added.Add(space.AppendEntity(e));
                tr.AddNewlyCreatedDBObject(e, true);
            }

            double hx = o.Section.Width / 2, hy = o.Section.Height / 2;
            var boundary = new Path2(new[]
            {
                new PathVertex(new Point2(-hx, -hy)), new PathVertex(new Point2(hx, -hy)),
                new PathVertex(new Point2(hx, hy)), new PathVertex(new Point2(-hx, hy))
            }, true);
            var concrete = frame.ToPolyline(MapPath(boundary)); concrete.Layer = o.SectionLayer; Append(concrete);

            foreach (var b in o.Section.LongitudinalBars)
            {
                var circle = new Circle(frame.ToWorld(Map(b.Center)), normal, o.Units.ToDrawing(b.Diameter / 2)) { Layer = o.RebarLayer };
                Append(circle);
            }

            foreach (var tie in o.Section.TieBars)
            {
                var geometry = RectangularTieGeometryBuilder.Build(o.Section, tie);
                if (!geometry.Validation.IsValid) throw new MdhUserException(geometry.Validation.Summary);
                var size = TieBarSize.Find(tie.TieSizeName);
                foreach (var path in geometry.Paths)
                {
                    var pl = frame.ToPolyline(MapPath(path));
                    pl.Layer = o.TieLayer; pl.LineWeight = TieWriter.LineweightFor(size); Append(pl);
                }
            }
            return added;
        }
    }
}
