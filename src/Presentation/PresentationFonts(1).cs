using Godot;

namespace Cultivation.Presentation;

internal static class PresentationFonts
{
    // Own the drawing resource instead of binding C# to ThemeDB's engine-owned singleton.
    public static Font Create() => new SystemFont
    {
        FontNames = ["Microsoft JhengHei", "Noto Sans CJK TC", "Arial"]
    };
}
