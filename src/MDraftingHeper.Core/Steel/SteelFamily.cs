using System;
using System.Collections.Generic;
using System.Linq;

namespace MDraftingHeper.Core.Steel
{
    /// <summary>
    /// UB/UC: British universal beams/columns. IH: other parallel-flange I/H series (IPE, HE A/B/M, ...);
    /// all three use the same I-section template. Taper-flange series (IPN, S, RSJ) are not supported.
    /// </summary>
    public enum SteelFamily { CHS, SHS, RHS, UB, UC, IH }

    /// <summary>
    /// Family definitions. Parameter keys are identical to the constraint parameter names in the
    /// Dynamic Block templates, so a dimension set maps 1:1 onto native Properties fields.
    /// </summary>
    public static class SteelFamilies
    {
        public const string D = "D", B = "B", H = "H", T = "t", Tw = "tw", Tf = "tf", R = "r";

        public static IReadOnlyList<string> Parameters(SteelFamily f)
        {
            switch (f)
            {
                case SteelFamily.CHS: return new[] { D, T };
                case SteelFamily.SHS: return new[] { B, T, R };
                case SteelFamily.RHS: return new[] { H, B, T, R };
                case SteelFamily.UB:
                case SteelFamily.UC:
                case SteelFamily.IH: return new[] { H, B, Tw, Tf, R };
                default: throw new ArgumentOutOfRangeException(nameof(f));
            }
        }

        /// <summary>Name of the Dynamic Block template definition used for the family.</summary>
        public static string BlockName(SteelFamily f)
        {
            switch (f)
            {
                case SteelFamily.CHS: return "MDH_CHS";
                case SteelFamily.SHS: return "MDH_SHS";
                case SteelFamily.RHS: return "MDH_RHS";
                default: return "MDH_ISECTION";
            }
        }

        public static string Describe(string parameter, SteelFamily f)
        {
            switch (parameter)
            {
                case D: return "Outside diameter D";
                case B: return IsI(f) ? "Flange width B" : "Outside width B";
                case H: return "Overall height H";
                case T: return "Wall thickness t";
                case Tw: return "Web thickness tw";
                case Tf: return "Flange thickness tf";
                case R: return IsI(f) ? "Root radius r" : "Outer corner radius r";
                default: return parameter;
            }
        }

        public static bool IsHollowRect(SteelFamily f) => f == SteelFamily.SHS || f == SteelFamily.RHS;
        public static bool IsI(SteelFamily f) => f == SteelFamily.UB || f == SteelFamily.UC || f == SteelFamily.IH;

        public static bool TryParse(string text, out SteelFamily family) =>
            Enum.TryParse((text ?? "").Trim(), true, out family) && Enum.IsDefined(typeof(SteelFamily), family);

        public static IEnumerable<SteelFamily> All => Enum.GetValues(typeof(SteelFamily)).Cast<SteelFamily>();
    }
}
