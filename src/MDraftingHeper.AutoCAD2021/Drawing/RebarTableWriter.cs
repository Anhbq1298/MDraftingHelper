using System;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using MDraftingHeper.Core.Rebar;
using MDraftingHeper.Core.Settings;

namespace MDraftingHeper.AutoCAD2021.Drawing
{
    /// <summary>Writes rebar results as a native AutoCAD Table (a snapshot, not a live schedule).</summary>
    public static class RebarTableWriter
    {
        public static List<string[]> BuildRows(RebarResult r)
        {
            string F1(double v) => v.ToString("0.0", CultureInfo.InvariantCulture);
            string F0(double v) => v.ToString("#,##0", CultureInfo.InvariantCulture);
            var rows = new List<string[]>
            {
                new[] { "REBAR PERCENTAGE (snapshot " + DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ")", "", "", "" },
                new[] { "Bar size", "Quantity", "Area per bar (mm²)", "Total area (mm²)" }
            };
            foreach (var g in r.Groups)
                rows.Add(new[] { g.Label, g.Quantity.ToString(CultureInfo.InvariantCulture), F1(g.AreaPerBar), F1(g.TotalArea) });
            rows.Add(new[] { "Total bars", r.TotalCount.ToString(CultureInfo.InvariantCulture), "As (mm²)", F1(r.As) });
            rows.Add(new[] { "", "", "Ag (mm²)", F0(r.Ag) });
            rows.Add(new[] { "", "", "ρ = As/Ag (%)", r.RhoPercent.ToString("0.000", CultureInfo.InvariantCulture) });
            if (r.TargetRhoPercent.HasValue)
            {
                rows.Add(new[] { "", "", "Target ρ (%)", r.TargetRhoPercent.Value.ToString("0.000", CultureInfo.InvariantCulture) });
                rows.Add(new[] { "", "", "As,target (mm²)", F1(r.AsTarget.Value) });
                rows.Add(new[] { "", "", "As − As,target (mm²)", F1(r.AreaDifference.Value) });
            }
            return rows;
        }

        /// <summary>
        /// Creates the table in the current space at <paramref name="insertWcs"/>, oriented to the UCS.
        /// Sizes are drawing units: text height × scale. Caller owns the transaction (one undo step).
        /// </summary>
        public static ObjectId Insert(Transaction tr, Database db, RebarResult result, Point3d insertWcs, Matrix3d ucs,
                                      RebarSettings settings, string layer)
        {
            var rows = BuildRows(result);
            double scale = settings.TableScale > 0 ? settings.TableScale : 1;
            double h = (settings.TextHeight > 0 ? settings.TextHeight : 2.5) * scale;

            var table = new Table { Layer = layer };
            var styles = (DBDictionary)tr.GetObject(db.TableStyleDictionaryId, OpenMode.ForRead);
            if (!string.IsNullOrEmpty(settings.TableStyle) && styles.Contains(settings.TableStyle))
                table.TableStyle = styles.GetAt(settings.TableStyle);
            else
                table.TableStyle = db.Tablestyle;

            table.SetSize(rows.Count, 4);
            var textStyles = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
            ObjectId textStyle = textStyles.Has(settings.TextStyle ?? "") ? textStyles[settings.TextStyle] : db.Textstyle;

            double[] widths = { 30 * h, 14 * h, 26 * h, 22 * h };
            for (int c = 0; c < 4; c++) table.Columns[c].Width = widths[c];
            for (int r = 0; r < rows.Count; r++)
            {
                table.Rows[r].Height = 2.2 * h;
                for (int c = 0; c < 4; c++)
                {
                    var cell = table.Cells[r, c];
                    cell.TextString = rows[r][c];
                    cell.TextHeight = h;
                    cell.TextStyleId = textStyle;
                    cell.Alignment = c == 0 ? CellAlignment.MiddleLeft : CellAlignment.MiddleCenter;
                }
            }
            table.MergeCells(CellRange.Create(table, 0, 0, 0, 3));
            table.Cells[0, 0].Alignment = CellAlignment.MiddleCenter;

            // Orient with the current UCS so the table reads correctly in rotated views.
            var ucsX = Vector3d.XAxis.TransformBy(ucs);
            var ucsZ = Vector3d.ZAxis.TransformBy(ucs);
            table.Position = insertWcs;
            table.Normal = ucsZ;
            table.Direction = ucsX;
            table.GenerateLayout();

            var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
            var id = space.AppendEntity(table);
            tr.AddNewlyCreatedDBObject(table, true);
            return id;
        }
    }
}
