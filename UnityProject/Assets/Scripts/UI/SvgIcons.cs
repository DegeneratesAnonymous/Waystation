// SvgIcons.cs
// Inline SVG icon markup (20×20) for the side-panel tab strip.
//
// Each constant is an SVG string using USS colour variable names where the
// fill/stroke values are expected.  In UI Toolkit the actual rendering uses
// VectorImage assets; these strings serve as the canonical source reference
// and as a documentation anchor until the asset pipeline is complete.
//
// Colour conventions (matching WaystationComponents.uss):
//   Primary stroke/fill → var(--ws-text-mid)   (icon tint applied via USS)
//   Accent highlight    → var(--ws-acc-bright)

namespace Waystation.UI
{
    /// <summary>
    /// Canonical 20×20 inline SVG icon paths for the seven side-panel tabs.
    /// </summary>
    internal static class SvgIcons
    {
        // ── Station ───────────────────────────────────────────────────────────
        // Hexagon outline representing the station structure.
        public const string Station =
            "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 20 20' width='20' height='20'>" +
            "<polygon points='10,1 18,5.5 18,14.5 10,19 2,14.5 2,5.5' " +
            "  fill='none' stroke='currentColor' stroke-width='1.5'/>" +
            "<circle cx='10' cy='10' r='2.5' fill='currentColor'/>" +
            "</svg>";

        // ── Crew ──────────────────────────────────────────────────────────────
        // Two overlapping person silhouettes.
        public const string Crew =
            "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 20 20' width='20' height='20'>" +
            "<circle cx='7' cy='6' r='2.5' fill='currentColor'/>" +
            "<path d='M2 16 Q2 11 7 11 Q12 11 12 16' fill='currentColor'/>" +
            "<circle cx='13' cy='5' r='2' fill='currentColor' opacity='0.65'/>" +
            "<path d='M9 15.5 Q9.5 12 13 12 Q17 12 17 15.5' fill='currentColor' opacity='0.65'/>" +
            "</svg>";

        // ── World ─────────────────────────────────────────────────────────────
        // Globe with latitude/longitude lines.
        public const string World =
            "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 20 20' width='20' height='20'>" +
            "<circle cx='10' cy='10' r='8' fill='none' stroke='currentColor' stroke-width='1.5'/>" +
            "<ellipse cx='10' cy='10' rx='4' ry='8' fill='none' stroke='currentColor' stroke-width='1'/>" +
            "<line x1='2' y1='10' x2='18' y2='10' stroke='currentColor' stroke-width='1'/>" +
            "<line x1='3.5' y1='5.5' x2='16.5' y2='5.5' stroke='currentColor' stroke-width='0.8'/>" +
            "<line x1='3.5' y1='14.5' x2='16.5' y2='14.5' stroke='currentColor' stroke-width='0.8'/>" +
            "</svg>";

        // ── Research ──────────────────────────────────────────────────────────
        // Flask / beaker outline.
        public const string Research =
            "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 20 20' width='20' height='20'>" +
            "<path d='M7 2 L7 9 L3 16 Q2 18 5 18 L15 18 Q18 18 17 16 L13 9 L13 2 Z' " +
            "  fill='none' stroke='currentColor' stroke-width='1.5' stroke-linejoin='round'/>" +
            "<line x1='7' y1='2' x2='13' y2='2' stroke='currentColor' stroke-width='1.5'/>" +
            "<circle cx='8' cy='14' r='1.5' fill='currentColor' opacity='0.8'/>" +
            "<circle cx='12' cy='12' r='1' fill='currentColor' opacity='0.6'/>" +
            "</svg>";

        // ── Map ───────────────────────────────────────────────────────────────
        // Folded map outline with a location pin.
        public const string Map =
            "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 20 20' width='20' height='20'>" +
            "<path d='M1 4 L7 2 L13 5 L19 3 L19 16 L13 18 L7 15 L1 17 Z' " +
            "  fill='none' stroke='currentColor' stroke-width='1.5' stroke-linejoin='round'/>" +
            "<line x1='7' y1='2' x2='7' y2='15' stroke='currentColor' stroke-width='1'/>" +
            "<line x1='13' y1='5' x2='13' y2='18' stroke='currentColor' stroke-width='1'/>" +
            "</svg>";

        // ── Fleet ─────────────────────────────────────────────────────────────
        // Stylised spacecraft / ship silhouette.
        public const string Fleet =
            "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 20 20' width='20' height='20'>" +
            "<path d='M10 2 L16 12 L13 11 L13 17 L7 17 L7 11 L4 12 Z' " +
            "  fill='currentColor' opacity='0.85'/>" +
            "<line x1='10' y1='2' x2='10' y2='12' stroke='currentColor' stroke-width='1'/>" +
            "</svg>";

        // ── Settings ──────────────────────────────────────────────────────────
        // Gear / cog outline with 6 teeth.
        public const string Settings =
            "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 20 20' width='20' height='20'>" +
            "<circle cx='10' cy='10' r='3' fill='none' stroke='currentColor' stroke-width='1.5'/>" +
            "<path d='M10 1 L11.5 4 L14.5 2.8 L15.2 6 L18.2 6.7 L17 9.5 L19 11 " +
            "  L17 12.5 L18.2 15.3 L15.2 16 L14.5 19.2 L11.5 18 L10 21 " +
            "  L8.5 18 L5.5 19.2 L4.8 16 L1.8 15.3 L3 12.5 L1 11 " +
            "  L3 9.5 L1.8 6.7 L4.8 6 L5.5 2.8 L8.5 4 Z' " +
            "  fill='none' stroke='currentColor' stroke-width='1.2' stroke-linejoin='round'/>" +
            "</svg>";
    }
}
