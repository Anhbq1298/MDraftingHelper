using System;

namespace MDraftingHeper.Core.Hooks
{
    /// <summary>
    /// Built-in bend and hook-tail sizing for drafting ties and hoops, following the EN 1992-1-1
    /// detailing values commonly applied to links:
    ///  - minimum inside mandrel diameter: 4 φ for φ ≤ 16 mm, 7 φ for φ &gt; 16 mm;
    ///  - straight tail after a hook: max(10 φ, 70 mm) for bends up to 150°, max(5 φ, 50 mm) above 150°.
    /// These are drafting rules for drawing realistic geometry. They are not a design or compliance
    /// check, and the drawn result must not be presented as a verified EC2 design.
    /// </summary>
    public static class Ec2DraftingRules
    {
        public const string Description =
            "EC2 drafting rules: mandrel 4φ (φ ≤ 16) / 7φ (φ > 16); tail max(10φ, 70) up to 150°, max(5φ, 50) above 150°. Not a design check.";

        public static double InsideMandrelDiameter(double tieDiameter)
        {
            if (!(tieDiameter > 0)) throw new ArgumentOutOfRangeException(nameof(tieDiameter));
            return (tieDiameter <= 16 + 1e-9 ? 4 : 7) * tieDiameter;
        }

        public static double TailLength(HookAngle angle, double tieDiameter)
        {
            if (!(tieDiameter > 0)) throw new ArgumentOutOfRangeException(nameof(tieDiameter));
            return (int)angle > 150
                ? Math.Max(5 * tieDiameter, 50)
                : Math.Max(10 * tieDiameter, 70);
        }
    }
}
