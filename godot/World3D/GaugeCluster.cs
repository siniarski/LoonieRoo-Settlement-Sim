// A car's instruments, drawn (2026-11-11): the picture on the panel behind the steering wheel (Cockpit.cs puts this
// in a small viewport and the viewport's picture on a panel in the binnacle). What a car of the kind has:
//
//   tachometer    engine speed, x1000 r/min, a red line from 5; under it the gear the box is in: P R N D
//   speedometer   km/h to 60 (the sim's cars do 22 on a road), and the odometer in its face
//   fuel          E to F, a red mark at the bottom of the tank
//   temperature   C to H, a red mark at the top
//   lamps         OIL and BATT (red: the engine isn't turning), ENGINE (amber: it is damaged), BRAKE (red: parked),
//                 FUEL (amber: nearly empty), LIGHTS (green: the headlights are on)
//
// This only draws what it is told (the fields below); Cockpit.cs reads them off the sim's car. Nothing here reads
// or writes state.

using System;
using Godot;

namespace LoonieRoo.Game.World3D;

public partial class GaugeCluster : Node2D
{
    /// <summary>The picture's size (pixels); the panel in the car has the same shape.</summary>
    public const int W = 512, H = 224;

    public double Kmh, Rpm, Fuel, Temp, OdoKm;
    public bool Oil, Battery, Engine, Brake, LowFuel, Lights;
    public char Gear = 'P';

    static readonly Color PANEL = new(0.03f, 0.03f, 0.035f), FACE = new(0.06f, 0.065f, 0.075f), RIM = new(0.32f, 0.33f, 0.35f);
    static readonly Color MARK = new(0.93f, 0.92f, 0.86f), FAINT = new(0.5f, 0.5f, 0.48f), NEEDLE = new(0.96f, 0.32f, 0.1f);
    static readonly Color RED = new(0.9f, 0.14f, 0.1f), AMBER = new(0.98f, 0.66f, 0.1f), GREEN = new(0.2f, 0.85f, 0.35f);
    static readonly Color OFF = new(0.1f, 0.1f, 0.11f), OFF_TEXT = new(0.2f, 0.2f, 0.21f);

    /// <summary>A big dial starts at the lower left and sweeps three quarters of a turn clockwise to the lower right
    /// (degrees clockwise from east, as the screen has them); a small one sweeps over its top.</summary>
    const float BIG_FROM = 135, BIG_SWEEP = 270, SMALL_FROM = 205, SMALL_SWEEP = 130;

    static Vector2 Dir(float degrees) => new(Mathf.Cos(Mathf.DegToRad(degrees)), Mathf.Sin(Mathf.DegToRad(degrees)));

    public override void _Draw()
    {
        var font = ThemeDB.FallbackFont;
        DrawRect(new Rect2(0, 0, W, H), PANEL);

        // The tachometer: hundreds of r/min, numbered in thousands.
        var tach = new Vector2(88, 100);
        Face(tach, 78);
        DrawArc(tach, 70, Mathf.DegToRad(BIG_FROM + BIG_SWEEP * 50 / 60f), Mathf.DegToRad(BIG_FROM + BIG_SWEEP), 16, RED, 6, true);
        Ticks(font, tach, 78, BIG_FROM, BIG_SWEEP, 60, 10, 5, (v) => (v / 10).ToString(), 17);
        Text(font, tach + new Vector2(0, 28), "x1000 r/min", 10, FAINT);
        Gears(font, tach + new Vector2(0, 50));
        Needle(tach, 66, BIG_FROM + BIG_SWEEP * (float)Math.Clamp(Rpm / 6000, 0, 1));

        // The speedometer, with the odometer in its face.
        var speed = new Vector2(256, 100);
        Face(speed, 92);
        Ticks(font, speed, 92, BIG_FROM, BIG_SWEEP, 60, 10, 2, (v) => v.ToString(), 18);
        Text(font, speed + new Vector2(0, -30), "km/h", 12, FAINT);
        Odometer(font, speed + new Vector2(0, 46));
        Needle(speed, 80, BIG_FROM + BIG_SWEEP * (float)Math.Clamp(Kmh / 60, 0, 1));

        // Fuel and temperature, one over the other.
        var fuel = new Vector2(426, 84);
        Small(font, fuel, "E", "F", "FUEL", 0, 0.12f);
        Needle(fuel, 44, SMALL_FROM + SMALL_SWEEP * (float)Math.Clamp(Fuel, 0, 1), 2.5f);
        var temp = new Vector2(426, 172);
        Small(font, temp, "C", "H", "TEMP", 0.86f, 1);
        Needle(temp, 44, SMALL_FROM + SMALL_SWEEP * (float)Math.Clamp(Temp, 0, 1), 2.5f);

        // The warning lamps along the bottom.
        string[] names = { "OIL", "BATT", "ENGINE", "BRAKE", "FUEL", "LIGHTS" };
        bool[] on = { Oil, Battery, Engine, Brake, LowFuel, Lights };
        Color[] colours = { RED, RED, AMBER, RED, AMBER, GREEN };
        for (int k = 0; k < names.Length; k++)
        {
            var box = new Rect2(10 + k * 58, 200, 52, 17);
            DrawRect(box, on[k] ? colours[k] : OFF);
            Text(font, box.GetCenter() + new Vector2(0, 4.5f), names[k], 11, on[k] ? new Color(0.05f, 0.04f, 0.03f) : OFF_TEXT);
        }
    }

    void Face(Vector2 centre, float radius)
    {
        DrawCircle(centre, radius, FACE);
        DrawArc(centre, radius, 0, Mathf.Tau, 64, RIM, 2, true);
    }

    /// <summary>Marks round a dial for 0 to max: a long one and a number every `major`, a short one every `minor`.</summary>
    void Ticks(Font font, Vector2 centre, float radius, float from, float sweep, int max, int major, int minor, Func<int, string> label, int size)
    {
        for (int v = 0; v <= max; v += minor)
        {
            var d = Dir(from + sweep * v / max);
            bool big = v % major == 0;
            DrawLine(centre + d * (radius - (big ? 16 : 10)), centre + d * (radius - 4), big ? MARK : FAINT, big ? 2.5f : 1.2f, true);
            if (big) Text(font, centre + d * (radius - 30) + new Vector2(0, size * 0.36f), label(v), size, MARK);
        }
    }

    /// <summary>A small gauge: an arc over the top from one letter to the other, a red stretch of it, its name beneath.</summary>
    void Small(Font font, Vector2 centre, string low, string high, string name, float redFrom, float redTo)
    {
        float a0 = Mathf.DegToRad(SMALL_FROM), a1 = Mathf.DegToRad(SMALL_FROM + SMALL_SWEEP);
        DrawArc(centre, 48, a0, a1, 24, FAINT, 2, true);
        DrawArc(centre, 48, Mathf.DegToRad(SMALL_FROM + SMALL_SWEEP * redFrom), Mathf.DegToRad(SMALL_FROM + SMALL_SWEEP * redTo), 8, RED, 5, true);
        for (int k = 0; k <= 4; k++)
        {
            var d = Dir(SMALL_FROM + SMALL_SWEEP * k / 4f);
            DrawLine(centre + d * (k % 2 == 0 ? 38 : 42), centre + d * 48, MARK, k % 2 == 0 ? 2.5f : 1.2f, true);
        }
        Text(font, centre + Dir(SMALL_FROM) * 30 + new Vector2(0, 5), low, 14, MARK);
        Text(font, centre + Dir(SMALL_FROM + SMALL_SWEEP) * 30 + new Vector2(0, 5), high, 14, MARK);
        Text(font, centre + new Vector2(0, 20), name, 10, FAINT);
    }

    void Needle(Vector2 centre, float length, float degrees, float width = 3.5f)
    {
        var d = Dir(degrees);
        DrawLine(centre - d * 9, centre + d * length, NEEDLE, width, true);
        DrawCircle(centre, width * 2, new Color(0.14f, 0.14f, 0.15f));
    }

    /// <summary>The selector: P R N D, the one the box is in lit.</summary>
    void Gears(Font font, Vector2 at)
    {
        const string all = "PRND";
        for (int k = 0; k < all.Length; k++)
            Text(font, at + new Vector2((k - 1.5f) * 17, 0), all[k].ToString(), 14, all[k] == Gear ? (Gear == 'R' ? AMBER : GREEN) : OFF_TEXT.Lightened(0.15f));
    }

    /// <summary>Six white figures of kilometres on black, and the tenths black on white.</summary>
    void Odometer(Font font, Vector2 at)
    {
        long tenths = (long)Math.Floor(Math.Max(0, OdoKm) * 10) % 10_000_000;
        string digits = tenths.ToString("0000000");
        for (int k = 0; k < 7; k++)
        {
            var cell = new Rect2(at.X - 7 * 6.5f + k * 13, at.Y - 9, 12, 17);
            bool last = k == 6;
            DrawRect(cell, last ? MARK : new Color(0.01f, 0.01f, 0.012f));
            Text(font, cell.GetCenter() + new Vector2(0, 5), digits[k].ToString(), 13, last ? new Color(0.04f, 0.04f, 0.05f) : MARK);
        }
    }

    /// <summary>Text centred on a point (the point is on its baseline).</summary>
    void Text(Font font, Vector2 at, string text, int size, Color colour) =>
        DrawString(font, at - new Vector2(60, 0), text, HorizontalAlignment.Center, 120, size, colour);
}
