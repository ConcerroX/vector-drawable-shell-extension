using System.Globalization;
using System.Text;

namespace VectorDrawableConverter;

/// <summary>
/// Parses an Android <see href="https://developer.android.com/reference/android/graphics/drawable/VectorDrawable">VectorDrawable</see>
/// <c>pathData</c> string and re-emits it as an SVG <c>d</c> attribute.
///
/// The grammar is shared with SVG (https://www.w3.org/TR/SVG/paths.html#PathDataBNF), with one
/// difference: for arc commands Android's parser treats the two flag values as ordinary floats
/// (so old files write "1.0"/"0.0"), whereas SVG requires the bare digits "1"/"0". This is the
/// only transformation we apply; everything else is a whitespace-normalized round-trip.
/// </summary>
public static class PathData
{
    // Command -> parameter count (same table as VdPath.COMMAND_STEP_MAP).
    private static readonly Dictionary<char, int> Arity = new()
    {
        ['z'] = 0, ['Z'] = 0,
        ['m'] = 2, ['M'] = 2,
        ['l'] = 2, ['L'] = 2,
        ['t'] = 2, ['T'] = 2,
        ['h'] = 1, ['H'] = 1,
        ['v'] = 1, ['V'] = 1,
        ['c'] = 6, ['C'] = 6,
        ['s'] = 4, ['S'] = 4,
        ['q'] = 4, ['Q'] = 4,
        ['a'] = 7, ['A'] = 7,
    };

    /// <summary>Converts Android <c>pathData</c> to SVG <c>d</c>.</summary>
    public static string ToSvgPathData(string pathData)
    {
        if (string.IsNullOrWhiteSpace(pathData))
        {
            return string.Empty;
        }

        string s = pathData.Trim();
        var sb = new StringBuilder(s.Length + 16);
        int i = 0;
        int n = s.Length;

        while (i < n)
        {
            // Locate the next command letter (skipping 'e'/'E' which can appear in exponents).
            while (i < n && !IsCommand(s[i]))
            {
                i++;
            }

            if (i >= n)
            {
                break;
            }

            char cmd = s[i];
            if (!Arity.TryGetValue(cmd, out int arity))
            {
                // Unknown command: copy it verbatim and move on defensively.
                sb.Append(cmd);
                i++;
                continue;
            }

            i++; // consume command letter
            sb.Append(cmd);

            if (arity == 0)
            {
                // 'z' / 'Z' close-path; no parameters.
                continue;
            }

            // Read `arity` numbers per repetition.
            var numbers = new List<string>(arity);
            bool firstGroup = true;
            while (true)
            {
                numbers.Clear();
                bool gotAny = false;
                for (int k = 0; k < arity; k++)
                {
                    SkipSeparators(s, ref i);
                    string? num = ReadNumber(s, ref i);
                    if (num is null)
                    {
                        break;
                    }

                    numbers.Add(num);
                    gotAny = true;
                }

                if (!gotAny)
                {
                    break;
                }

                for (int k = 0; k < numbers.Count; k++)
                {
                    if (!firstGroup || k > 0)
                    {
                        sb.Append(' ');
                    }

                    string v = numbers[k];

                    // SVG arc flags must be bare '0'/'1'. Android allows "1.0"/"0.0".
                    if (cmd is 'a' or 'A' && k is 3 or 4)
                    {
                        sb.Append(ParseFlag(v));
                        continue;
                    }

                    sb.Append(v);
                }

                firstGroup = false;
            }
        }

        return sb.ToString().Trim();
    }

    private static string ParseFlag(string v)
    {
        // After trimming trailing '.', '0' the flag should be "0" or "1" (possibly "-0").
        if (v is "0" or "1")
        {
            return v;
        }

        if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
        {
            return d == 0 ? "0" : "1";
        }

        return v;
    }

    private static bool IsCommand(char c) =>
        (c >= 'A' && c <= 'Z' && c != 'E') || (c >= 'a' && c <= 'z' && c != 'e');

    private static void SkipSeparators(string s, ref int i)
    {
        while (i < s.Length && (s[i] == ' ' || s[i] == ',' || s[i] == '\t' || s[i] == '\r' || s[i] == '\n'))
        {
            i++;
        }
    }

    private static string? ReadNumber(string s, ref int i)
    {
        int start = i;
        int n = s.Length;
        bool sawDot = false;
        bool sawExp = false;

        while (i < n)
        {
            char c = s[i];
            if (c == '-' || c == '+')
            {
                // A sign starts a new number only if the previous char is not part of an exponent.
                if (i > start && s[i - 1] != 'e' && s[i - 1] != 'E')
                {
                    break;
                }
            }
            else if (c == '.')
            {
                if (sawDot)
                {
                    break; // second dot -> implicit separator (e.g. "1.2.3")
                }

                sawDot = true;
            }
            else if (c == 'e' || c == 'E')
            {
                if (sawExp)
                {
                    break;
                }

                sawExp = true;
                // An exponent must be followed by digits/sign; trust the next loop iteration.
            }
            else if (c >= '0' && c <= '9')
            {
                // part of the number
            }
            else
            {
                break;
            }

            i++;
        }

        return i == start ? null : s[start..i];
    }
}
