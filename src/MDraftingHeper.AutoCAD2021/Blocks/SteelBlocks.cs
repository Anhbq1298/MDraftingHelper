using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using MDraftingHeper.AutoCAD2021.App;
using MDraftingHeper.AutoCAD2021.Drawing;
using MDraftingHeper.AutoCAD2021.Metadata;
using MDraftingHeper.Core.Steel;

namespace MDraftingHeper.AutoCAD2021.Blocks
{
    public sealed class SteelInsertOptions
    {
        public SteelSection Section { get; set; }
        public SteelProfile Profile { get; set; }
        public InsertionAnchor Anchor { get; set; }
        public double RotationRadians { get; set; }
        public bool CentreAxes { get; set; }
        public bool Label { get; set; }
        public double LabelHeight { get; set; } = 2.5;
        public string BlockLayer { get; set; } = "MDH_STEEL";
        public string AxesLayer { get; set; } = "MDH_AXES";
        public string LabelLayer { get; set; } = "MDH_LABEL";
    }

    /// <summary>
    /// Imports the pre-authored Dynamic Block templates and inserts references whose numeric
    /// constraint parameters are native Properties (Ctrl+1) fields.
    /// </summary>
    public static class SteelBlocks
    {
        public static readonly string[] TemplateNames = { "MDH_CHS", "MDH_SHS", "MDH_RHS", "MDH_ISECTION" };

        public static bool IsTemplateName(string name) => TemplateNames.Contains(name, StringComparer.OrdinalIgnoreCase);

        /// <summary>Copies the family's block definition from the template DWG if the drawing lacks it.</summary>
        public static ObjectId EnsureDefinition(Transaction tr, Database db, SteelFamily family, string templatePath = null)
        {
            string name = SteelFamilies.BlockName(family);
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            if (bt.Has(name))
            {
                var existing = (BlockTableRecord)tr.GetObject(bt[name], OpenMode.ForRead);
                if (!existing.IsDynamicBlock)
                    throw new MdhUserException("The drawing already has a non-dynamic block named '" + name + "'. Rename or purge it before inserting MDraftingHeper sections.");
                return bt[name];
            }

            templatePath = templatePath ?? Paths.SteelTemplateDrawing;
            if (!File.Exists(templatePath))
                throw new MdhUserException("Steel Dynamic Block templates are missing: " + templatePath +
                                           ". Reinstall MDraftingHeper or rebuild them with tools\\DynamicBlocks\\Build-DynamicBlocks.ps1.");

            using (var src = new Database(false, true))
            {
                src.ReadDwgFile(templatePath, FileOpenMode.OpenForReadAndAllShare, true, "");
                src.CloseInput(true);
                ObjectId srcId;
                using (var str = src.TransactionManager.StartTransaction())
                {
                    var sbt = (BlockTable)str.GetObject(src.BlockTableId, OpenMode.ForRead);
                    if (!sbt.Has(name)) throw new MdhUserException("Template drawing has no block '" + name + "': " + templatePath);
                    srcId = sbt[name];
                    str.Commit();
                }
                var ids = new ObjectIdCollection { srcId };
                var map = new IdMapping();
                src.WblockCloneObjects(ids, db.BlockTableId, map, DuplicateRecordCloning.Ignore, false);
            }
            bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            if (!bt.Has(name)) throw new MdhUserException("Importing block '" + name + "' from the template failed.");
            return bt[name];
        }

        public static Dictionary<string, DynamicBlockReferenceProperty> Properties(BlockReference br)
        {
            var d = new Dictionary<string, DynamicBlockReferenceProperty>(StringComparer.Ordinal);
            if (!br.IsDynamicBlock) return d;
            foreach (DynamicBlockReferenceProperty p in br.DynamicBlockReferencePropertyCollection)
                if (p.Show && !d.ContainsKey(p.PropertyName)) d[p.PropertyName] = p;
            return d;
        }

        /// <summary>Sets every family parameter; throws if the template lacks one (wrong/old template).</summary>
        public static void SetDimensions(BlockReference br, SteelSection s)
        {
            var props = Properties(br);
            foreach (var key in SteelFamilies.Parameters(s.Family))
            {
                if (!props.TryGetValue(key, out var p))
                    throw new MdhUserException("The " + s.Family + " block template has no '" + key + "' parameter. The template drawing is outdated.");
                if (p.ReadOnly) throw new MdhUserException("Parameter '" + key + "' is read-only in the template.");
                p.Value = s[key];
            }
        }

        public static SteelSection ReadSection(BlockReference br, SteelFamily family)
        {
            var props = Properties(br);
            var dims = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var key in SteelFamilies.Parameters(family))
                dims[key] = props.TryGetValue(key, out var p) ? Convert.ToDouble(p.Value, CultureInfo.InvariantCulture) : double.NaN;
            return new SteelSection(family, dims);
        }

        public static string EffectiveName(Transaction tr, BlockReference br)
        {
            var id = br.IsDynamicBlock ? br.DynamicBlockTableRecord : br.BlockTableRecord;
            return ((BlockTableRecord)tr.GetObject(id, OpenMode.ForRead)).Name;
        }

        /// <summary>
        /// Inserts the section. <paramref name="anchorWcs"/> is the picked point; the block base point
        /// (centroid) is offset from it according to the anchor, in the plane of <paramref name="ucs"/>.
        /// Caller owns the transaction, so the insertion with axes and label is one undo step.
        /// </summary>
        public static ObjectId Insert(Transaction tr, Database db, SteelInsertOptions o, Point3d anchorWcs, Matrix3d ucs)
        {
            var v = SteelValidator.Validate(o.Section);
            if (!v.IsValid) throw new MdhUserException("Invalid section: " + string.Join(" ", v.Errors));

            var defId = EnsureDefinition(tr, db, o.Section.Family);
            var blockLayer = DrawingGuards.EnsureLayer(tr, db, o.BlockLayer, 4);

            var x = Vector3d.XAxis.TransformBy(ucs).GetNormal();
            var z = Vector3d.ZAxis.TransformBy(ucs).GetNormal();
            var y = z.CrossProduct(x);
            var off = AnchorMath.CentroidOffset(o.Section, o.Anchor);
            double c = Math.Cos(o.RotationRadians), s = Math.Sin(o.RotationRadians);
            var rotated = x * (off.X * c - off.Y * s) + y * (off.X * s + off.Y * c);
            var basePoint = anchorWcs + rotated;

            var br = new BlockReference(basePoint, defId) { LayerId = blockLayer, Normal = z };
            // Rotation is measured in the block's OCS; add the UCS x-axis angle in that plane.
            var ocsX = Vector3d.XAxis.TransformBy(Matrix3d.PlaneToWorld(z));
            br.Rotation = ocsX.GetAngleTo(x, z) + o.RotationRadians;

            var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
            var id = space.AppendEntity(br);
            tr.AddNewlyCreatedDBObject(br, true);
            SetDimensions(br, o.Section);

            var meta = SteelMetadata.Create(o.Section, o.Profile);
            if (o.CentreAxes) meta["axes"] = string.Join(",", SteelCompanions.CreateAxes(tr, db, br, o.Section, o.AxesLayer).Select(h => h.ToString()));
            if (o.Label) meta["label"] = SteelCompanions.CreateLabel(tr, db, br, o.Section, SteelMetadata.LabelText(o.Section, o.Profile), o.LabelHeight, o.LabelLayer).ToString();
            MetadataStore.Write(tr, br, SteelMetadata.Key, meta);
            return id;
        }
    }

    /// <summary>Axes and label drawn next to a section; repositioned by <see cref="SteelSync"/>.</summary>
    public static class SteelCompanions
    {
        public static (double halfW, double halfH) HalfExtents(SteelSection s)
        {
            if (s.Family == SteelFamily.CHS) return (s["D"] / 2, s["D"] / 2);
            if (s.Family == SteelFamily.SHS) return (s["B"] / 2, s["B"] / 2);
            return (s["B"] / 2, s["H"] / 2);
        }

        /// <summary>Block-local point → WCS using the reference's own transform.</summary>
        private static Point3d Local(BlockReference br, double x, double y) => new Point3d(x, y, 0).TransformBy(br.BlockTransform);

        public static List<Handle> CreateAxes(Transaction tr, Database db, BlockReference br, SteelSection s, string layer)
        {
            var layerId = DrawingGuards.EnsureLayer(tr, db, layer, 8);
            var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
            var handles = new List<Handle>();
            foreach (var line in AxisLines(br, s))
            {
                line.LayerId = layerId;
                space.AppendEntity(line);
                tr.AddNewlyCreatedDBObject(line, true);
                handles.Add(line.Handle);
            }
            return handles;
        }

        public static IEnumerable<Line> AxisLines(BlockReference br, SteelSection s)
        {
            var (hw, hh) = HalfExtents(s);
            double e = 1.15;
            yield return new Line(Local(br, -hw * e, 0), Local(br, hw * e, 0));
            yield return new Line(Local(br, 0, -hh * e), Local(br, 0, hh * e));
        }

        public static void UpdateAxes(Transaction tr, BlockReference br, SteelSection s, IList<ObjectId> ids)
        {
            var lines = AxisLines(br, s).ToList();
            for (int i = 0; i < ids.Count && i < lines.Count; i++)
            {
                if (!(tr.GetObject(ids[i], OpenMode.ForRead) is Line l)) continue;
                if (l.StartPoint.IsEqualTo(lines[i].StartPoint) && l.EndPoint.IsEqualTo(lines[i].EndPoint)) continue;
                l.UpgradeOpen();
                l.StartPoint = lines[i].StartPoint;
                l.EndPoint = lines[i].EndPoint;
            }
            foreach (var l in lines) l.Dispose();
        }

        public static Point3d LabelPoint(BlockReference br, SteelSection s, double height)
        {
            var (_, hh) = HalfExtents(s);
            return Local(br, 0, -(hh * 1.15 + 2 * height));
        }

        public static Handle CreateLabel(Transaction tr, Database db, BlockReference br, SteelSection s, string text, double height, string layer)
        {
            var layerId = DrawingGuards.EnsureLayer(tr, db, layer, 2);
            var mt = new MText
            {
                Contents = MTextEscape(text),
                TextHeight = height,
                Attachment = AttachmentPoint.TopCenter,
                Location = LabelPoint(br, s, height),
                Normal = br.Normal,
                Rotation = br.Rotation,
                LayerId = layerId
            };
            var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
            space.AppendEntity(mt);
            tr.AddNewlyCreatedDBObject(mt, true);
            return mt.Handle;
        }

        public static void UpdateLabel(Transaction tr, BlockReference br, SteelSection s, ObjectId id, string text)
        {
            if (!(tr.GetObject(id, OpenMode.ForRead) is MText mt)) return;
            var contents = MTextEscape(text);
            var location = LabelPoint(br, s, mt.TextHeight);
            if (mt.Contents == contents && mt.Location.IsEqualTo(location) && Math.Abs(mt.Rotation - br.Rotation) < 1e-9) return;
            mt.UpgradeOpen();
            mt.Contents = contents;
            mt.Location = location;
            mt.Rotation = br.Rotation;
        }

        private static string MTextEscape(string s) => (s ?? "").Replace("\\", "\\\\").Replace("{", "\\{").Replace("}", "\\}");
    }
}
