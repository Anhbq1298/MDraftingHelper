using System;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using MDraftingHeper.Core.Geometry;

namespace MDraftingHeper.AutoCAD2021.Drawing
{
    /// <summary>
    /// The plane of a planar entity in AutoCAD OCS terms (normal + elevation), with conversions
    /// between WCS points and Core 2D points in millimetres. Using OCS keeps lightweight polyline
    /// bulges and arc senses identical to AutoCAD's own convention in any UCS.
    /// </summary>
    public sealed class PlaneFrame
    {
        public const double AngleTolerance = 1e-6;
        public const double DistanceTolerance = 1e-6;

        public Vector3d Normal { get; }
        public double Elevation { get; }
        public UnitContext Units { get; }
        private readonly Matrix3d _worldToOcs;
        private readonly Matrix3d _ocsToWorld;

        public PlaneFrame(Vector3d normal, double elevation, UnitContext units)
        {
            Normal = normal.GetNormal();
            Elevation = elevation;
            Units = units;
            _worldToOcs = Matrix3d.WorldToPlane(Normal);
            _ocsToWorld = Matrix3d.PlaneToWorld(Normal);
        }

        public static PlaneFrame FromPolyline(Polyline pl, UnitContext units) => new PlaneFrame(pl.Normal, pl.Elevation, units);

        public static PlaneFrame FromCircle(Circle c, UnitContext units)
        {
            var n = c.Normal.GetNormal();
            return new PlaneFrame(n, c.Center.TransformBy(Matrix3d.WorldToPlane(n)).Z, units);
        }

        /// <summary>True when the circle lies in this plane (parallel normal, same elevation).</summary>
        public bool Contains(Circle c, out string reason)
        {
            reason = null;
            if (!c.Normal.GetNormal().IsParallelTo(Normal, new Tolerance(AngleTolerance, AngleTolerance)))
            {
                reason = "not parallel to the section plane";
                return false;
            }
            double z = c.Center.TransformBy(_worldToOcs).Z;
            if (Math.Abs(z - Elevation) > Math.Max(DistanceTolerance, Math.Abs(Elevation) * 1e-9))
            {
                reason = "not at the section plane elevation";
                return false;
            }
            return true;
        }

        public Point2 ToCore(Point3d world)
        {
            var p = world.TransformBy(_worldToOcs);
            return new Point2(Units.ToMm(p.X), Units.ToMm(p.Y));
        }

        public Point2 ToCore(Point2d ocs) => new Point2(Units.ToMm(ocs.X), Units.ToMm(ocs.Y));

        public Point2d ToOcs(Point2 mm) => new Point2d(Units.ToDrawing(mm.X), Units.ToDrawing(mm.Y));

        public Point3d ToWorld(Point2 mm) =>
            new Point3d(Units.ToDrawing(mm.X), Units.ToDrawing(mm.Y), Elevation).TransformBy(_ocsToWorld);

        /// <summary>A Core path (mm) as a lightweight polyline in this plane, keeping exact bulges.</summary>
        public Polyline ToPolyline(Path2 path)
        {
            var pl = new Polyline(path.Vertices.Count);
            for (int i = 0; i < path.Vertices.Count; i++)
                pl.AddVertexAt(i, ToOcs(path.Vertices[i].Point), path.Vertices[i].Bulge, 0, 0);
            pl.Closed = path.IsClosed;
            pl.Normal = Normal;
            pl.Elevation = Elevation;
            return pl;
        }

        /// <summary>A closed lightweight polyline as a Core path (mm). The duplicate closing vertex is dropped.</summary>
        public Path2 FromPolyline(Polyline pl, out bool closed)
        {
            int n = pl.NumberOfVertices;
            var verts = new System.Collections.Generic.List<PathVertex>(n);
            for (int i = 0; i < n; i++) verts.Add(new PathVertex(ToCore(pl.GetPoint2dAt(i)), pl.GetBulgeAt(i)));
            closed = pl.Closed;
            if (!closed && n > 2 && verts[0].Point.IsEqualTo(verts[n - 1].Point, Units.ToMm(DistanceTolerance)))
            {
                verts.RemoveAt(n - 1);
                closed = true;
            }
            return new Path2(verts, closed);
        }
    }
}
