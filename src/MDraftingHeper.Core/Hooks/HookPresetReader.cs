using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace MDraftingHeper.Core.Hooks
{
    [DataContract]
    public sealed class HookPresetFile
    {
        [DataMember(Name = "schemaVersion")] public int SchemaVersion { get; set; }
        [DataMember(Name = "presets")] public List<HookPreset> Presets { get; set; }
    }

    public static class HookPresetReader
    {
        public static HookPreset ManualFallback() => new HookPreset
        {
            Id = "manual", Name = "Manual input", IsManual = true, IsVerified = false,
            CodeBasis = "None - user-entered values", Notes = "No code check is performed."
        };

        /// <summary>Reads presets; always returns at least the Manual preset.</summary>
        public static List<HookPreset> Read(string json, out string problem)
        {
            problem = null;
            List<HookPreset> list = null;
            try
            {
                var ser = new DataContractJsonSerializer(typeof(HookPresetFile));
                using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    var file = (HookPresetFile)ser.ReadObject(ms);
                    if (file?.SchemaVersion != 1) problem = "Unsupported hook preset schema version.";
                    else list = file.Presets;
                }
            }
            catch (SerializationException ex)
            {
                problem = "Hook presets could not be read: " + ex.Message;
            }

            list = (list ?? new List<HookPreset>()).Where(p => p != null && !string.IsNullOrWhiteSpace(p.Id)).ToList();
            foreach (var p in list)
            {
                p.MandrelRules = p.MandrelRules ?? new List<MandrelRule>();
                p.TailRules = p.TailRules ?? new List<TailRule>();
            }
            if (!list.Any(p => p.IsManual)) list.Insert(0, ManualFallback());
            return list;
        }

        public static List<HookPreset> ReadFile(string path, out string problem)
        {
            if (!File.Exists(path))
            {
                problem = "Hook preset file not found: " + path;
                return new List<HookPreset> { ManualFallback() };
            }
            return Read(File.ReadAllText(path, Encoding.UTF8), out problem);
        }
    }
}
