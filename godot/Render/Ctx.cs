// A small Canvas 2D-style drawing context over a Godot CanvasItem, so the browser's drawing code
// (src/render) ports line for line: fill and stroke styles as CSS colour strings, paths, a
// transform stack, dashes, alpha, and text. Only what the renderer uses.

using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;

namespace LoonieRoo.Game.Render;

public sealed class Ctx
{
    readonly CanvasItem ci;
    Rid rid;

    public string fillStyle = "#000000";
    public string strokeStyle = "#000000";
    public double lineWidth = 1;
    public double globalAlpha = 1;
    public string textAlign = "left";
    public string textBaseline = "alphabetic";
    double[]? dash;
    int fontPx = 12;
    bool fontBold;

    Transform2D xf = Transform2D.Identity;
    readonly Stack<(Transform2D xf, string fill, string stroke, double lw, double alpha, double[]? dash)> saved = new();

    readonly List<List<Vector2>> subpaths = new();
    readonly List<bool> closed = new();
    List<Vector2>? current;

    static Font? mono;
    static Font? monoBold;

    public Ctx(CanvasItem ci) { this.ci = ci; }

    /// <summary>Call at the top of _Draw.</summary>
    public void Begin()
    {
        rid = ci.GetCanvasItem();
        xf = Transform2D.Identity;
        ci.DrawSetTransformMatrix(xf);
        saved.Clear();
        globalAlpha = 1;
        dash = null;
    }

    // --- colours ----------------------------------------------------------------------------

    static readonly Dictionary<string, Color> colours = new();

    /// <summary>A CSS colour: #rgb, #rrggbb, rgb(...) or rgba(...).</summary>
    public static Color Css(string css)
    {
        if (colours.TryGetValue(css, out var c)) return c;
        c = Parse(css);
        colours[css] = c;
        return c;
    }

    static Color Parse(string s)
    {
        s = s.Trim();
        if (s.StartsWith('#'))
        {
            var hex = s[1..];
            if (hex.Length == 3) hex = $"{hex[0]}{hex[0]}{hex[1]}{hex[1]}{hex[2]}{hex[2]}";
            return Color.FromHtml(hex);
        }
        int open = s.IndexOf('(');
        if (open > 0)
        {
            var parts = s[(open + 1)..s.LastIndexOf(')')].Split(',');
            double P(int i) => double.Parse(parts[i].Trim(), CultureInfo.InvariantCulture);
            return new Color((float)(P(0) / 255), (float)(P(1) / 255), (float)(P(2) / 255), parts.Length > 3 ? (float)P(3) : 1f);
        }
        return Colors.Magenta;
    }

    Color Fill => WithAlpha(Css(fillStyle));
    Color Stroke_ => WithAlpha(Css(strokeStyle));
    Color WithAlpha(Color c) => new(c.R, c.G, c.B, (float)(c.A * globalAlpha));

    // --- state --------------------------------------------------------------------------------

    public void save() => saved.Push((xf, fillStyle, strokeStyle, lineWidth, globalAlpha, dash));

    public void restore()
    {
        if (saved.Count == 0) return;
        (xf, fillStyle, strokeStyle, lineWidth, globalAlpha, dash) = saved.Pop();
        ci.DrawSetTransformMatrix(xf);
    }

    public void translate(double x, double y)
    {
        xf = xf * Transform2D.Identity.Translated(new Vector2((float)x, (float)y));
        ci.DrawSetTransformMatrix(xf);
    }

    public void rotate(double a)
    {
        xf = xf * new Transform2D((float)a, Vector2.Zero);
        ci.DrawSetTransformMatrix(xf);
    }

    public void setLineDash(double[] d) => dash = d.Length == 0 ? null : d;

    // --- rectangles ---------------------------------------------------------------------------

    public void fillRect(double x, double y, double w, double h)
    {
        if (w < 0) { x += w; w = -w; }
        if (h < 0) { y += h; h = -h; }
        ci.DrawRect(new Rect2((float)x, (float)y, (float)w, (float)h), Fill);
    }

    public void strokeRect(double x, double y, double w, double h)
    {
        if (dash == null)
        {
            ci.DrawRect(new Rect2((float)x, (float)y, (float)w, (float)h), Stroke_, false, (float)lineWidth);
            return;
        }
        var a = V(x, y); var b = V(x + w, y); var c = V(x + w, y + h); var d = V(x, y + h);
        StrokeLine(a, b); StrokeLine(b, c); StrokeLine(c, d); StrokeLine(d, a);
    }

    // --- paths ----------------------------------------------------------------------------------

    static Vector2 V(double x, double y) => new((float)x, (float)y);

    public void beginPath()
    {
        subpaths.Clear();
        closed.Clear();
        current = null;
    }

    public void moveTo(double x, double y)
    {
        current = new List<Vector2> { V(x, y) };
        subpaths.Add(current);
        closed.Add(false);
    }

    public void lineTo(double x, double y)
    {
        if (current == null) { moveTo(x, y); return; }
        current.Add(V(x, y));
    }

    public void closePath()
    {
        if (current == null) return;
        closed[^1] = true;
        current = null;
    }

    public void arc(double cx, double cy, double r, double a0, double a1) => ellipse(cx, cy, r, r, 0, a0, a1);

    public void ellipse(double cx, double cy, double rx, double ry, double rotation, double a0, double a1)
    {
        double sweep = a1 - a0;
        int n = Math.Clamp((int)(Math.Abs(sweep) * Math.Max(rx, ry) / 3), 8, 64);
        bool full = Math.Abs(sweep) >= Math.PI * 2 - 1e-6;
        if (current == null || full && current.Count > 0 && subpaths.Count > 0 && current.Count == 1)
        {
            // A fresh shape (canvas would join it to the last point; a lone moveTo before it is dropped).
            if (current != null && current.Count == 1) { subpaths.RemoveAt(subpaths.Count - 1); closed.RemoveAt(closed.Count - 1); }
            current = new List<Vector2>();
            subpaths.Add(current);
            closed.Add(false);
        }
        double cos = Math.Cos(rotation), sin = Math.Sin(rotation);
        int count = full ? n : n + 1;
        for (int i = 0; i < count; i++)
        {
            double a = a0 + sweep * i / n;
            double ex = Math.Cos(a) * rx, ey = Math.Sin(a) * ry;
            current.Add(V(cx + ex * cos - ey * sin, cy + ex * sin + ey * cos));
        }
        if (full)
        {
            closed[^1] = true;
            current = null;
        }
    }

    public void fill()
    {
        var colour = Fill;
        foreach (var pts in subpaths)
        {
            if (pts.Count < 3) continue;
            var arr = pts.ToArray();
            var idx = Geometry2D.TriangulatePolygon(arr);
            if (idx.Length == 0) continue;
            var cols = new Color[arr.Length];
            Array.Fill(cols, colour);
            RenderingServer.CanvasItemAddTriangleArray(rid, idx, arr, cols);
        }
    }

    public void stroke()
    {
        for (int k = 0; k < subpaths.Count; k++)
        {
            var pts = subpaths[k];
            if (pts.Count < 2) continue;
            var list = new List<Vector2>(pts);
            if (closed[k]) list.Add(pts[0]);
            if (dash == null) ci.DrawPolyline(list.ToArray(), Stroke_, (float)lineWidth);
            else for (int i = 1; i < list.Count; i++) StrokeLine(list[i - 1], list[i]);
        }
    }

    void StrokeLine(Vector2 a, Vector2 b)
    {
        if (dash == null) { ci.DrawLine(a, b, Stroke_, (float)lineWidth); return; }
        float len = a.DistanceTo(b);
        if (len <= 0) return;
        var dir = (b - a) / len;
        float t = 0;
        int k = 0;
        while (t < len)
        {
            float seg = (float)dash[k % dash.Length];
            if (k % 2 == 0) ci.DrawLine(a + dir * t, a + dir * Math.Min(len, t + seg), Stroke_, (float)lineWidth);
            t += Math.Max(0.5f, seg);
            k++;
        }
    }

    // --- text -------------------------------------------------------------------------------------

    /// <summary>Canvas's `ctx.font = '12px monospace'` (or 'bold 12px ...').</summary>
    public void font(int px, bool bold = false)
    {
        fontPx = Math.Max(1, px);
        fontBold = bold;
    }

    static Font Mono(bool bold)
    {
        if (mono == null)
        {
            var f = new SystemFont { FontNames = new[] { "Consolas", "DejaVu Sans Mono", "monospace" } };
            mono = f;
            monoBold = new FontVariation { BaseFont = f, VariationEmbolden = 0.7f };
        }
        return bold ? monoBold! : mono;
    }

    public double measureText(string text) => Mono(fontBold).GetStringSize(text, HorizontalAlignment.Left, -1, fontPx).X;

    public void fillText(string text, double x, double y)
    {
        var f = Mono(fontBold);
        double w = textAlign == "center" ? measureText(text) : 0;
        double asc = f.GetAscent(fontPx), desc = f.GetDescent(fontPx);
        double by = textBaseline switch
        {
            "top" => y + asc,
            "middle" => y + (asc - desc) / 2,
            "bottom" => y - desc,
            _ => y,
        };
        double bx = textAlign == "center" ? x - w / 2 : textAlign == "right" ? x - measureText(text) : x;
        ci.DrawString(f, V(bx, by), text, HorizontalAlignment.Left, -1, fontPx, Fill);
    }

    /// <summary>A radial glow (createRadialGradient, two stops, the outer transparent): rings of fading alpha.</summary>
    public void radialGlow(double cx, double cy, double radius, Color inner)
    {
        const int rings = 8;
        for (int i = rings; i >= 1; i--)
        {
            double r = radius * i / rings;
            var c = new Color(inner.R, inner.G, inner.B, inner.A / rings * (float)globalAlpha);
            ci.DrawCircle(V(cx, cy), (float)r, c);
        }
    }
}
