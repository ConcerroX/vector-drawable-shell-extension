using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace VectorDrawableConverter;

/// <summary>
/// Converts an Android VectorDrawable XML document into an equivalent SVG string.
///
/// Attribute mapping is derived from the AOSP tools/base vectordrawable package
/// (VdTree/VdPath/VdGroup/PathParser/VdUtil), the same code Android Studio uses for its
/// vector preview and SVG import. See:
/// https://android.googlesource.com/platform/tools/base/+/refs/heads/main/sdk-common/src/main/java/com/android/ide/common/vectordrawable/
/// </summary>
public sealed class VectorDrawableToSvgConverter
{
    private static readonly XNamespace AndroidNs = "http://schemas.android.com/apk/res/android";
    private static readonly XNamespace SvgNs = "http://www.w3.org/2000/svg";
    private static readonly XNamespace AaptNs = "http://schemas.android.com/aapt";

    public string Convert(string vectorXml)
    {
        var doc = XDocument.Parse(vectorXml, LoadOptions.None);
        var root = doc.Root;
        if (root is null || root.Name.LocalName != "vector")
        {
            throw new InvalidDataException("Not a VectorDrawable: missing <vector> root element.");
        }

        // --- Root <vector> attributes ---
        double viewportW = ParseDimension(Attr(root, "viewportWidth")) ?? 24.0;
        double viewportH = ParseDimension(Attr(root, "viewportHeight")) ?? 24.0;
        double rootAlpha = ParseDimension(Attr(root, "alpha")) ?? 1.0;
        string? tint = Attr(root, "tint");

        // Resolve the tint up front. Android applies tint as a PorterDuff SRC_IN over the whole
        // drawable — every non-transparent pixel is recolored to the tint. For the monochrome
        // icons this extension targets, that is equivalent to replacing each path's fill/stroke
        // color with the tint (retaining each path's alpha). A null tint leaves colors untouched.
        ColorResolver.ResolvedColor? tintColor = null;
        if (!string.IsNullOrWhiteSpace(tint))
        {
            var t = ColorResolver.Resolve(tint);
            if (t.Alpha > 0.0)
            {
                tintColor = t;
            }
        }

        // The SVG root element. Match PowerToys' SvgPreviewHandlerHelper.AddStyleSVG: no fixed
        // width/height (so the icon scales down in small windows), capped at a reasonable preview
        // size (VectorDrawable viewports are ~24dp, far too small at 1:1), and centered with an
        // absolute transform. The thumbnail EXE overrides the inline style to fill its fixed
        // canvas, so this centering only affects the preview pane.
        const double previewSize = 512.0;
        var svg = new XElement(
            SvgNs + "svg",
            new XAttribute("xmlns", SvgNs.NamespaceName),
            new XAttribute("viewBox", $"{Format(0)} {Format(0)} {Format(viewportW)} {Format(viewportH)}"),
            new XAttribute("style",
                $"max-width:{Format(previewSize)}px;max-height:{Format(previewSize)}px;" +
                "position:absolute;top:50%;left:50%;transform:translate(-50%,-50%);"));

        // The content is grouped so a root alpha can be applied uniformly.
        var contentGroup = new XElement(SvgNs + "g");
        ConvertChildren(root, contentGroup, tintColor);

        // Apply root alpha.
        if (rootAlpha < 1.0)
        {
            contentGroup.SetAttributeValue("opacity", Format(rootAlpha));
        }

        svg.Add(contentGroup);

        return svg.ToString(SaveOptions.DisableFormatting);
    }

    private void ConvertChildren(XElement parent, XElement svgParent, ColorResolver.ResolvedColor? tint)
    {
        foreach (var child in parent.Elements())
        {
            switch (child.Name.LocalName)
            {
                case "group":
                    ConvertGroup(child, svgParent, tint);
                    break;
                case "path":
                    svgParent.Add(ConvertPath(child, tint));
                    break;
                case "clip-path":
                    svgParent.Add(ConvertClipPath(child));
                    break;
                default:
                    // Ignore unknown elements (defensive).
                    break;
            }
        }
    }

    private void ConvertGroup(XElement group, XElement svgParent, ColorResolver.ResolvedColor? tint)
    {
        var g = new XElement(SvgNs + "g");

        double rotation = ParseDimension(Attr(group, "rotation")) ?? 0.0;
        double pivotX = ParseDimension(Attr(group, "pivotX")) ?? 0.0;
        double pivotY = ParseDimension(Attr(group, "pivotY")) ?? 0.0;
        double scaleX = ParseDimension(Attr(group, "scaleX")) ?? 1.0;
        double scaleY = ParseDimension(Attr(group, "scaleY")) ?? 1.0;
        double translateX = ParseDimension(Attr(group, "translateX")) ?? 0.0;
        double translateY = ParseDimension(Attr(group, "translateY")) ?? 0.0;

        // Android applies: translate(-pivot) · scale · rotate · translate(translate + pivot).
        // SVG transform lists apply left-to-right as matrix pre-multiplication, matching
        // Android's postConcatenate order when listed in reverse. We compose the SVG list so the
        // effective matrix is identical.
        //   SVG: T(tx+px, ty+py) R(deg) S(sx, sy) T(-px, -py)
        // which is exactly the Android matrix (see VdGroup.updateLocalMatrix).
        var transforms = new List<string>(4);
        if (Math.Abs(translateX) > 1e-9 || Math.Abs(translateY) > 1e-9 || Math.Abs(pivotX) > 1e-9 || Math.Abs(pivotY) > 1e-9)
        {
            transforms.Add($"translate({Format(translateX + pivotX)} {Format(translateY + pivotY)})");
        }

        if (Math.Abs(rotation) > 1e-9)
        {
            transforms.Add($"rotate({Format(rotation)})");
        }

        if (Math.Abs(scaleX - 1.0) > 1e-9 || Math.Abs(scaleY - 1.0) > 1e-9)
        {
            transforms.Add($"scale({Format(scaleX)} {Format(scaleY)})");
        }

        if (Math.Abs(pivotX) > 1e-9 || Math.Abs(pivotY) > 1e-9)
        {
            transforms.Add($"translate({Format(-pivotX)} {Format(-pivotY)})");
        }

        if (transforms.Count > 0)
        {
            g.SetAttributeValue("transform", string.Join(' ', transforms));
        }

        ConvertChildren(group, g, tint);
        svgParent.Add(g);
    }

    private XElement ConvertPath(XElement path, ColorResolver.ResolvedColor? tint)
    {
        var el = new XElement(SvgNs + "path");

        string? pathData = Attr(path, "pathData");
        if (pathData is not null)
        {
            el.SetAttributeValue("d", PathData.ToSvgPathData(pathData));
        }

        string? name = Attr(path, "name");
        if (name is not null)
        {
            el.SetAttributeValue("id", name);
        }

        string? fillColor = Attr(path, "fillColor");
        double fillAlpha = ParseDimension(Attr(path, "fillAlpha")) ?? 1.0;

        // A fillColor of "#00000000" (or resolved transparent) means no fill. Also, absence of a
        // fillColor attribute implies the path is unfilled (stroke only), matching Android.
        if (fillColor is not null)
        {
            var fill = tint ?? ColorResolver.Resolve(fillColor);
            double alpha = fill.Alpha * fillAlpha;
            if (alpha <= 0.0)
            {
                el.SetAttributeValue("fill", "none");
            }
            else
            {
                el.SetAttributeValue("fill", fill.CssColor);
                if (alpha < 1.0)
                {
                    el.SetAttributeValue("fill-opacity", Format(alpha));
                }
            }
        }
        else
        {
            el.SetAttributeValue("fill", "none");
        }

        string? strokeColor = Attr(path, "strokeColor");
        double strokeAlpha = ParseDimension(Attr(path, "strokeAlpha")) ?? 1.0;
        double strokeWidth = ParseDimension(Attr(path, "strokeWidth")) ?? 0.0;

        if (strokeColor is not null)
        {
            var stroke = tint ?? ColorResolver.Resolve(strokeColor);
            double alpha = stroke.Alpha * strokeAlpha;
            if (alpha <= 0.0 || strokeWidth <= 0.0)
            {
                el.SetAttributeValue("stroke", "none");
            }
            else
            {
                el.SetAttributeValue("stroke", stroke.CssColor);
                el.SetAttributeValue("stroke-width", Format(strokeWidth));
                if (alpha < 1.0)
                {
                    el.SetAttributeValue("stroke-opacity", Format(alpha));
                }
            }
        }

        string? fillType = Attr(path, "fillType");
        if (!string.IsNullOrEmpty(fillType))
        {
            el.SetAttributeValue("fill-rule", fillType.Equals("evenOdd", StringComparison.OrdinalIgnoreCase) ? "evenodd" : "nonzero");
        }

        string? lineCap = Attr(path, "strokeLineCap");
        if (lineCap is not null)
        {
            el.SetAttributeValue("stroke-linecap", lineCap.ToLowerInvariant());
        }

        string? lineJoin = Attr(path, "strokeLineJoin");
        if (lineJoin is not null)
        {
            el.SetAttributeValue("stroke-linejoin", lineJoin.ToLowerInvariant());
        }

        string? miterLimit = Attr(path, "strokeMiterLimit");
        if (miterLimit is not null)
        {
            el.SetAttributeValue("stroke-miterlimit", miterLimit);
        }

        return el;
    }

    private XElement ConvertClipPath(XElement clipPath)
    {
        var defs = new XElement(SvgNs + "clipPath");

        string? name = Attr(clipPath, "name");
        if (name is not null)
        {
            defs.SetAttributeValue("id", name);
        }

        // A clip-path's own path draws the clip region (fill is irrelevant; only the geometry).
        var pathEl = ConvertPath(clipPath, tint: null);
        pathEl.RemoveAttributes();
        pathEl.SetAttributeValue("d", PathData.ToSvgPathData(Attr(clipPath, "pathData") ?? string.Empty));

        // Respect fillType (evenOdd) for the clip region.
        string? fillType = Attr(clipPath, "fillType");
        if (!string.IsNullOrEmpty(fillType))
        {
            pathEl.SetAttributeValue("clip-rule", fillType.Equals("evenOdd", StringComparison.OrdinalIgnoreCase) ? "evenodd" : "nonzero");
        }

        defs.Add(pathEl);
        return defs;
    }

    private static string? Attr(XElement el, string localName) =>
        el.Attribute(AndroidNs + localName)?.Value ?? el.Attribute(localName)?.Value;

    private static double? ParseDimension(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        // Strip a trailing "dp"/"px"/"dip"/"sp"/"pt" unit. Android's own parser treats all
        // dimension units as "dp" (see VdTree.parseSize TODO).
        string v = value.Trim();
        int i = v.Length;
        while (i > 0 && !char.IsDigit(v[i - 1]) && v[i - 1] != '.' && v[i - 1] != '-')
        {
            i--;
        }

        if (i == 0)
        {
            return null;
        }

        return double.TryParse(v[..i], NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : null;
    }

    private static string Format(double d)
    {
        // Trim trailing zeros, keep invariant decimal point.
        string s = d.ToString("0.0#####", CultureInfo.InvariantCulture);
        return s == "-0" ? "0" : s;
    }
}
