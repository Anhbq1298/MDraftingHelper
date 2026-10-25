using System;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.AutoCAD.DatabaseServices;

namespace MDraftingHeper.AutoCAD2021.Metadata
{
    /// <summary>Versioned key/value metadata stored as one XRecord in an entity's extension dictionary.</summary>
    public sealed class MetadataRecord
    {
        private readonly Dictionary<string, string> _values = new Dictionary<string, string>(StringComparer.Ordinal);
        public int SchemaVersion { get; }

        public MetadataRecord(int schemaVersion) { SchemaVersion = schemaVersion; }

        public string this[string key]
        {
            get => _values.TryGetValue(key, out var v) ? v : null;
            set => _values[key] = value ?? "";
        }

        public IEnumerable<KeyValuePair<string, string>> Entries => _values;

        public double? Number(string key) =>
            double.TryParse(this[key], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : (double?)null;

        public bool SameAs(MetadataRecord other)
        {
            if (other == null || other.SchemaVersion != SchemaVersion || other._values.Count != _values.Count) return false;
            foreach (var kv in _values)
                if (other[kv.Key] != kv.Value) return false;
            return true;
        }
    }

    public static class MetadataStore
    {
        private const string SchemaKey = "schema";

        public static void Write(Transaction tr, DBObject obj, string key, MetadataRecord record)
        {
            if (obj.ExtensionDictionary.IsNull)
            {
                if (!obj.IsWriteEnabled) obj.UpgradeOpen();
                obj.CreateExtensionDictionary();
            }
            var dict = (DBDictionary)tr.GetObject(obj.ExtensionDictionary, OpenMode.ForWrite);
            var rb = new ResultBuffer(new TypedValue((int)DxfCode.Text, SchemaKey + "=" + record.SchemaVersion.ToString(CultureInfo.InvariantCulture)));
            foreach (var kv in record.Entries) rb.Add(new TypedValue((int)DxfCode.Text, kv.Key + "=" + kv.Value));

            if (dict.Contains(key))
            {
                var xr = (Xrecord)tr.GetObject(dict.GetAt(key), OpenMode.ForWrite);
                xr.Data = rb;
            }
            else
            {
                var xr = new Xrecord { Data = rb };
                dict.SetAt(key, xr);
                tr.AddNewlyCreatedDBObject(xr, true);
            }
        }

        public static MetadataRecord Read(Transaction tr, DBObject obj, string key)
        {
            if (obj.ExtensionDictionary.IsNull) return null;
            var dict = (DBDictionary)tr.GetObject(obj.ExtensionDictionary, OpenMode.ForRead);
            if (!dict.Contains(key)) return null;
            var xr = (Xrecord)tr.GetObject(dict.GetAt(key), OpenMode.ForRead);
            if (xr.Data == null) return null;
            int schema = 0;
            var pairs = new List<KeyValuePair<string, string>>();
            foreach (TypedValue tv in xr.Data)
            {
                var s = tv.Value as string;
                if (s == null) continue;
                int eq = s.IndexOf('=');
                if (eq <= 0) continue;
                var k = s.Substring(0, eq);
                var v = s.Substring(eq + 1);
                if (k == SchemaKey) int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out schema);
                else pairs.Add(new KeyValuePair<string, string>(k, v));
            }
            var rec = new MetadataRecord(schema);
            foreach (var p in pairs) rec[p.Key] = p.Value;
            return rec;
        }
    }
}
