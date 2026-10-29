using System.Globalization;

namespace VectorDrawableConverter;

/// <summary>
/// Parses Android color values and resolves the resource references that appear in VectorDrawable
/// files, producing SVG-compatible CSS colors.
///
/// Android colors are #AARRGGBB (alpha first); SVG/CSS uses #RRGGBB with a separate opacity. We
/// therefore split alpha out into an opacity channel and emit #RRGGBB.
/// </summary>
public static class ColorResolver
{
    /// <summary>Result of resolving a color: the CSS color and an alpha multiplier (0..1).</summary>
    public readonly record struct ResolvedColor(string CssColor, double Alpha);

    /// <summary>
    /// Resolves a VectorDrawable color value into CSS. Handles #RGB/#ARGB/#RRGGBB/#AARRGGBB and
    /// the resource/theme references that our target files use.
    /// </summary>
    public static ResolvedColor Resolve(string value)
    {
        string v = value.Trim();

        if (v.StartsWith('#'))
        {
            return FromHex(v);
        }

        // Resource reference: @android:color/white etc.
        if (v.StartsWith('@'))
        {
            return FromResourceRef(v);
        }

        // Theme attribute reference: ?attr/colorControlNormal etc.
        if (v.StartsWith('?'))
        {
            return FromAttrRef(v);
        }

        // Unknown: fall back to opaque black (matches Android's default of nothing drawn).
        return new ResolvedColor("#000000", 1.0);
    }

    private static ResolvedColor FromHex(string v)
    {
        switch (v.Length)
        {
            case 4: // #RGB -> #RRGGBB, opaque
            {
                int r = HexNibble(v[1]) * 0x11;
                int g = HexNibble(v[2]) * 0x11;
                int b = HexNibble(v[3]) * 0x11;
                return new ResolvedColor($"#{r:x2}{g:x2}{b:x2}", 1.0);
            }
            case 5: // #ARGB -> alpha nibble + #RRGGBB
            {
                int a = HexNibble(v[1]) * 0x11;
                int r = HexNibble(v[2]) * 0x11;
                int g = HexNibble(v[3]) * 0x11;
                int b = HexNibble(v[4]) * 0x11;
                return new ResolvedColor($"#{r:x2}{g:x2}{b:x2}", a / 255.0);
            }
            case 7: // #RRGGBB, opaque
                return new ResolvedColor(v.ToLowerInvariant(), 1.0);
            case 9: // #AARRGGBB
            {
                int a = HexByte(v, 1);
                int r = HexByte(v, 3);
                int g = HexByte(v, 5);
                int b = HexByte(v, 7);
                return new ResolvedColor($"#{r:x2}{g:x2}{b:x2}", a / 255.0);
            }
            default:
                return new ResolvedColor("#000000", 1.0);
        }
    }

    private static ResolvedColor FromResourceRef(string v)
    {
        // Strip @android:color/ prefix; look at the trailing color name.
        int slash = v.LastIndexOf('/');
        string name = slash >= 0 ? v[(slash + 1)..] : v;

        return name.ToLowerInvariant() switch
        {
            "white" => new ResolvedColor("#ffffff", 1.0),
            "black" => new ResolvedColor("#000000", 1.0),
            "transparent" => new ResolvedColor("#000000", 0.0),
            "red" => new ResolvedColor("#ff0000", 1.0),
            "green" => new ResolvedColor("#00ff00", 1.0),
            "blue" => new ResolvedColor("#0000ff", 1.0),
            "dkgray" or "darkgray" or "gray" or "dkgrey" => new ResolvedColor("#808080", 1.0),
            "ltgray" or "lightgray" or "ltgrey" => new ResolvedColor("#c0c0c0", 1.0),
            _ => new ResolvedColor("#000000", 1.0),
        };
    }

    private static ResolvedColor FromAttrRef(string v)
    {
        int slash = v.LastIndexOf('/');
        string name = slash >= 0 ? v[(slash + 1)..] : v;

        return name.ToLowerInvariant() switch
        {
            // Theme attribute colorControlNormal resolves to a mid gray in the light theme,
            // but our target files use it for monochrome icons; black matches the intent.
            _ => new ResolvedColor("#000000", 1.0),
        };
    }

    private static int HexNibble(char c) =>
        c switch
        {
            >= '0' and <= '9' => c - '0',
            >= 'a' and <= 'f' => c - 'a' + 10,
            >= 'A' and <= 'F' => c - 'A' + 10,
            _ => 0,
        };

    private static int HexByte(string s, int offset) => (HexNibble(s[offset]) << 4) | HexNibble(s[offset + 1]);
}
