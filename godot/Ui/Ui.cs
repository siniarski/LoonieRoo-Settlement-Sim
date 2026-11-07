// A small UI kit over Godot Controls, so the browser's DOM panels (src/ui) port compactly: a theme
// matching index.html's CSS, and Panel, a box rebuilt from rows (text, buttons) only when its key
// (what it shows) changes, like the TypeScript panels' `if (key === last) return`.

using System;
using System.Collections.Generic;
using Godot;
using Control = Godot.Control;
using LoonieRoo.Game.Render;

namespace LoonieRoo.Game.Ui;

public static class Kit
{
    public static readonly Color Text = Ctx.Css("#d8dcd6");
    public static readonly Color Dim = Ctx.Css("#9aa39b");
    public static readonly Color Title = Ctx.Css("#e8d9a8");
    public static readonly Color Gold = Ctx.Css("#f0c850");

    static Font? font;
    public static Font Font => font ??= new SystemFont { FontNames = new[] { "Consolas", "DejaVu Sans Mono", "monospace" } };

    public static StyleBoxFlat Box(string bg, string border, int radius = 4, int pad = 6)
    {
        var s = new StyleBoxFlat { BgColor = Ctx.Css(bg), BorderColor = Ctx.Css(border) };
        s.SetBorderWidthAll(1);
        s.SetCornerRadiusAll(radius);
        s.ContentMarginLeft = s.ContentMarginRight = pad + 2;
        s.ContentMarginTop = s.ContentMarginBottom = pad;
        return s;
    }

    /// <summary>The theme every HUD control inherits: dark panels, flat buttons, a monospace font.</summary>
    public static Theme MakeTheme()
    {
        var t = new Theme { DefaultFont = Font, DefaultFontSize = 13 };
        t.SetColor("font_color", "Label", Text);
        t.SetColor("font_color", "Button", Text);
        t.SetColor("font_hover_color", "Button", Ctx.Css("#ffffff"));
        t.SetColor("font_pressed_color", "Button", Ctx.Css("#ffffff"));
        t.SetColor("font_disabled_color", "Button", Ctx.Css("#666e68"));
        t.SetStylebox("normal", "Button", Box("#222824", "#3a423c", 4, 3));
        t.SetStylebox("hover", "Button", Box("#2c332e", "#4a544c", 4, 3));
        t.SetStylebox("pressed", "Button", Box("#3d5a46", "#6c9a78", 4, 3));
        t.SetStylebox("disabled", "Button", Box("#1a1e1b", "#2a302c", 4, 3));
        t.SetStylebox("focus", "Button", new StyleBoxEmpty());
        t.SetStylebox("panel", "PanelContainer", Box("rgba(20, 23, 21, 0.9)", "#333a35", 6));
        t.SetConstant("separation", "VBoxContainer", 3);
        t.SetConstant("separation", "HBoxContainer", 4);
        return t;
    }

    /// <summary>The look of an `active` button (a toggle that's on).</summary>
    public static void SetActive(Button b, bool on)
    {
        if (on)
        {
            b.AddThemeStyleboxOverride("normal", Box("#3d5a46", "#6c9a78", 4, 3));
            b.AddThemeStyleboxOverride("hover", Box("#47684f", "#7cab88", 4, 3));
        }
        else
        {
            b.RemoveThemeStyleboxOverride("normal");
            b.RemoveThemeStyleboxOverride("hover");
        }
    }

    public static Label Label(string text, Color? colour = null, int size = 0, int wrapWidth = 0)
    {
        var l = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        if (colour is { } c) l.AddThemeColorOverride("font_color", c);
        if (size > 0) l.AddThemeFontSizeOverride("font_size", size);
        // Wrap only text wider than the width (a monospace character is about 0.6 of the font size).
        if (wrapWidth > 0 && text.Length * (size > 0 ? size : 13) * 0.6 > wrapWidth)
        {
            l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            l.CustomMinimumSize = new Vector2(wrapWidth, 0);
        }
        return l;
    }

    public static Button Button(string text, Action onClick, bool disabled = false, bool active = false, string? tip = null, int size = 0)
    {
        var b = new Button { Text = text, Disabled = disabled, TooltipText = tip ?? "", FocusMode = Control.FocusModeEnum.None, MouseFilter = Control.MouseFilterEnum.Stop };
        if (size > 0) b.AddThemeFontSizeOverride("font_size", size);
        b.Pressed += onClick;
        SetActive(b, active);
        return b;
    }
}

/// <summary>Where a Panel's rows go: text lines, rows of buttons, nested rows.</summary>
public sealed class Rows
{
    readonly Container into;
    public readonly int fontSize;
    public Rows(Container into, int fontSize) { this.into = into; this.fontSize = fontSize; }

    public Label Text(string text, Color? colour = null, int wrapWidth = 0)
    {
        var l = Kit.Label(text, colour, fontSize, wrapWidth);
        into.AddChild(l);
        return l;
    }

    public Button Button(string text, Action onClick, bool disabled = false, bool active = false, string? tip = null)
    {
        var b = Kit.Button(text, onClick, disabled, active, tip, fontSize);
        into.AddChild(b);
        return b;
    }

    /// <summary>A horizontal row (a label and its buttons).</summary>
    public Rows Row()
    {
        var h = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        into.AddChild(h);
        return new Rows(h, fontSize);
    }

    public T Add<T>(T node) where T : Node
    {
        into.AddChild(node);
        return node;
    }
}

/// <summary>A boxed panel of rows, rebuilt only when its key changes.</summary>
public sealed partial class UiPanel : PanelContainer
{
    readonly VBoxContainer box = new() { MouseFilter = MouseFilterEnum.Ignore };
    string key = "\0";
    readonly int fontSize;

    public UiPanel() : this(null) { }

    public UiPanel(string? border, string bg = "rgba(20, 23, 21, 0.9)", int fontSize = 12)
    {
        this.fontSize = fontSize;
        if (border != null) AddThemeStyleboxOverride("panel", Kit.Box(bg, border, 4, 5));
        MouseFilter = MouseFilterEnum.Stop;
        AddChild(box);
    }

    /// <summary>Hide, or rebuild from `build` if `newKey` differs from what's shown.</summary>
    public void Show(bool shown, string newKey, Action<Rows> build)
    {
        Visible = shown;
        if (!shown || newKey == key) return;
        key = newKey;
        foreach (var c in box.GetChildren()) c.QueueFree();
        build(new Rows(box, fontSize));
    }

    /// <summary>Forget what's shown, so the next Show rebuilds.</summary>
    public void Invalidate() => key = "\0";
}
