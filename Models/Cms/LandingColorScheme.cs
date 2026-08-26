using System.Globalization;

namespace CourtBookingSystem.Models.Cms
{
    public sealed record LandingColorScheme(
        string Key,
        string Name,
        string Description,
        string Primary,
        string PrimaryDark,
        string Accent,
        string BackgroundSoft,
        string TextDark)
    {
        public string PrimaryRgb => ToRgb(Primary);
        public string PrimaryDarkRgb => ToRgb(PrimaryDark);
        public string AccentRgb => ToRgb(Accent);

        private static string ToRgb(string hex)
        {
            var clean = hex.TrimStart('#');
            if (clean.Length != 6) return "15, 76, 129";

            var r = int.Parse(clean[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            var g = int.Parse(clean[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            var b = int.Parse(clean[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return $"{r}, {g}, {b}";
        }
    }

    public static class LandingColorSchemeCatalog
    {
        public const string DefaultKey = "royal-blue";

        public static IReadOnlyList<LandingColorScheme> All { get; } =
        [
            new("royal-blue", "Sport Blue", "Deep blue with warm field-gold accents.", "#0f4c81", "#0a3a64", "#ffb627", "#f4f6f9", "#1c2434"),
            new("emerald-court", "Emerald Field", "Fresh green with a bright lime highlight.", "#047857", "#065f46", "#a3e635", "#f0fdf4", "#17251f"),
            new("midnight-gold", "Midnight Gold", "Premium navy with polished gold accents.", "#111827", "#030712", "#f59e0b", "#f8fafc", "#111827"),
            new("sunrise-clay", "Sunrise Clay", "Warm terracotta with clean sky-blue contrast.", "#b45309", "#7c2d12", "#38bdf8", "#fff7ed", "#2f241c"),
            new("ocean-teal", "Ocean Teal", "Cool teal with a crisp coral action color.", "#0f766e", "#134e4a", "#fb7185", "#f0fdfa", "#17252a"),
            new("crimson-match", "Crimson Match", "Sporty red balanced with a blue accent.", "#be123c", "#881337", "#2563eb", "#fff1f2", "#2d1720"),
            new("forest-lime", "Forest Lime", "Grounded forest green with energetic lime.", "#166534", "#14532d", "#84cc16", "#f7fee7", "#15251a"),
            new("slate-copper", "Slate Copper", "Modern slate with warm copper accents.", "#334155", "#1e293b", "#f97316", "#f8fafc", "#1f2937"),
            new("violet-rally", "Violet Rally", "Confident violet paired with amber.", "#6d28d9", "#4c1d95", "#fbbf24", "#f5f3ff", "#26153f"),
            new("coral-mint", "Coral Mint", "Friendly coral with mint highlights.", "#e11d48", "#9f1239", "#2dd4bf", "#fff5f7", "#332022"),
            new("charcoal-cyan", "Charcoal Cyan", "Sharp charcoal with electric cyan.", "#1f2937", "#111827", "#06b6d4", "#f3f4f6", "#111827"),
            new("classic-green", "Classic Green", "Traditional sports green with gold.", "#15803d", "#166534", "#facc15", "#f0fdf4", "#14251b"),
            new("peach-navy", "Peach Navy", "Soft peach accents over a steady navy base.", "#1d4ed8", "#1e3a8a", "#fb923c", "#eff6ff", "#172033"),
            new("sky-amber", "Sky Amber", "Bright sky blue with sunny amber.", "#0284c7", "#075985", "#f59e0b", "#f0f9ff", "#102a3a"),
            new("black-lime", "Black Lime", "Bold dark theme accents with vivid lime.", "#18181b", "#09090b", "#a3e635", "#f4f4f5", "#18181b"),
            new("plum-rose", "Plum Rose", "Elegant plum with warm rose accents.", "#7e22ce", "#581c87", "#fb7185", "#faf5ff", "#2b1638")
        ];

        public static LandingColorScheme GetByKey(string? key)
        {
            return All.FirstOrDefault(s => string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase))
                ?? All.First(s => s.Key == DefaultKey);
        }

        public static bool IsValid(string? key)
        {
            return All.Any(s => string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase));
        }
    }
}
