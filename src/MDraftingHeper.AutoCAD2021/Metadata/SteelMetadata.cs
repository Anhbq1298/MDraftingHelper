using System;
using System.Globalization;
using System.Linq;
using MDraftingHeper.Core.Steel;

namespace MDraftingHeper.AutoCAD2021.Metadata
{
    /// <summary>
    /// Schema 1 metadata on each inserted steel BlockReference (extension dictionary XRecord "MDH_STEEL"):
    /// family, source profile id + dimensions at insertion, current library match and companion
    /// handles. The current geometry (dynamic properties) is always authoritative over this record.
    /// </summary>
    public static class SteelMetadata
    {
        public const string Key = "MDH_STEEL";
        public const int Schema = 1;

        public static MetadataRecord Create(SteelSection s, SteelProfile p)
        {
            var m = new MetadataRecord(Schema);
            m["family"] = s.Family.ToString();
            m["sourceProfileId"] = p?.Id ?? "";
            m["sourceProfileName"] = p?.Name ?? "";
            m["sourceDataset"] = p == null ? "" : p.Source + (string.IsNullOrEmpty(p.SourceVersion) ? "" : " / " + p.SourceVersion);
            m["sourceDims"] = DimsText(s);
            m["matchedProfileId"] = p?.Id ?? "";
            m["status"] = p == null ? "custom" : "profile";
            m["currentDims"] = DimsText(s);
            return m;
        }

        public static string DimsText(SteelSection s) =>
            string.Join(";", SteelFamilies.Parameters(s.Family).Select(k => k + ":" + s[k].ToString("R", CultureInfo.InvariantCulture)));

        public static string LabelText(SteelSection s, SteelProfile matched) =>
            matched != null ? matched.Name : "Custom " + s.Family + " " + s.DimensionText();

        public static bool TryFamily(MetadataRecord m, out SteelFamily family)
        {
            family = default(SteelFamily);
            return m != null && SteelFamilies.TryParse(m["family"], out family);
        }
    }
}
