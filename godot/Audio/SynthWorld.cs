// The world's sounds, made by code (2026-11-09, the second sound session): your footsteps and swing,
// gunshots, glass, thunder, and the loops (wind, rain, the night's insects, an engine, a generator,
// an alarm bell, a helicopter). Same rules as Synth.cs: pure arithmetic over float samples, each
// variant from its own seed, the same every run.
// A loop must join its end to its start without a click: events (drops, chirps, strikes, firings)
// are laid on a circle (a tail running past the end comes round to the start), steady tones have a
// whole number of cycles in the loop, and noise is made a little long and its end faded into its start.

using System;
using static LoonieRoo.Game.Audio.Synth;

namespace LoonieRoo.Game.Audio;

public static class SynthWorld
{
    /// <summary>Partials above this (Hz) are left out: past half the sample rate they'd fold back as false tones.</summary>
    const double NYQUIST = RATE * 0.45;

    /// <summary>A two-pole band-pass (RBJ's cookbook), retunable while it runs.</summary>
    struct Band
    {
        double b0, b2, a1, a2, x1, x2, y1, y2;
        public void Tune(double f, double q)
        {
            double w = 2 * Math.PI * Math.Min(f, RATE * 0.45) / RATE, alpha = Math.Sin(w) / (2 * q), a0 = 1 + alpha;
            b0 = alpha / a0; b2 = -alpha / a0; a1 = -2 * Math.Cos(w) / a0; a2 = (1 - alpha) / a0;
        }
        public double Run(double x)
        {
            double y = b0 * x + b2 * x2 - a1 * y1 - a2 * y2;
            x2 = x1; x1 = x; y2 = y1; y1 = y;
            return y;
        }
    }

    /// <summary>Adds `v` at sample i of a loop, round the circle.</summary>
    static void Lay(float[] s, long i, double v) => s[(int)(((i % s.Length) + s.Length) % s.Length)] += (float)v;

    /// <summary>Noise for a loop of n samples: made F longer, its end faded into its start (equal power), so it joins.</summary>
    static double[] LoopNoise(Lcg r, int n, Func<double, double> filter)
    {
        int fade = Math.Min(n / 4, RATE / 4);
        var raw = new double[n + fade];
        for (int i = 0; i < raw.Length; i++) raw[i] = filter(r.Next() * 2 - 1);
        var outp = new double[n];
        for (int i = 0; i < n; i++)
        {
            if (i < fade)
            {
                double w = (double)i / fade;
                outp[i] = raw[i] * Math.Sin(w * Math.PI / 2) + raw[n + i] * Math.Cos(w * Math.PI / 2);
            }
            else outp[i] = raw[i];
        }
        return outp;
    }

    static float[] Floats(double[] d)
    {
        var f = new float[d.Length];
        for (int i = 0; i < d.Length; i++) f[i] = (float)d[i];
        return f;
    }

    /// <summary>Scales a loop to `peak` (no end fade: it joins its start).</summary>
    static float[] Level(float[] s, float peak)
    {
        float max = 0;
        foreach (var v in s) max = Math.Max(max, Math.Abs(v));
        if (max > 0) for (int i = 0; i < s.Length; i++) s[i] *= peak / max;
        return s;
    }

    // ---- One-shots ----

    /// <summary>A footstep. `surface`: 0 soft ground (grass, dirt, field: a muffled crunch), 1 hard (road, paving,
    /// concrete: a heel's click and a scuff), 2 boards (a hollow knock).</summary>
    public static float[] Step(int seed, int surface)
    {
        var r = new Lcg((uint)(seed * 31337 + surface * 7717 + 3));
        int n = (int)(0.2 * RATE);
        var outp = new float[n];
        var crunch = new Band(); crunch.Tune(r.Range(900, 1500), 0.9);
        var scuff = new Band(); scuff.Tune(r.Range(1800, 2600), 1.4);
        var knock = new Band(); knock.Tune(r.Range(170, 230), 7);
        var knock2 = new Band(); knock2.Tune(r.Range(420, 520), 9);
        double thumpF = surface == 2 ? r.Range(110, 140) : r.Range(65, 90), phase = 0, lp = 0, prev = 0, grain = 0;
        double heel = surface == 1 ? r.Range(0.6, 0.9) : surface == 2 ? 0.25 : 0.08;
        for (int i = 0; i < n; i++)
        {
            double s = (double)i / RATE;
            double white = r.Next() * 2 - 1;
            phase += 2 * Math.PI * thumpF / RATE;
            double thump = Math.Sin(phase) * Math.Exp(-s / (surface == 2 ? 0.05 : 0.025));
            double hp = white - prev; prev = white;
            double click = hp * heel * Math.Exp(-s / 0.003);
            double v;
            if (surface == 0)
            {
                // Grainy: a crackle that comes and goes as the sole rolls.
                if ((i & 31) == 0) grain = r.Next() < 0.55 ? r.Range(0.3, 1) : 0.1;
                lp += (white - lp) * 0.5;
                v = thump * 0.7 + crunch.Run(lp) * 3.2 * grain * Math.Exp(-s / 0.05) * Smooth(Math.Min(1, s / 0.006)) + click;
            }
            else if (surface == 1)
                v = thump * 0.6 + click + scuff.Run(white) * 1.6 * Math.Exp(-s / 0.03);
            else
                v = thump * 0.8 + (knock.Run(white) * 5 + knock2.Run(white) * 3) * Math.Exp(-s / 0.07) + click;
            outp[i] = (float)v;
        }
        return Normalise(outp, 0.8f);
    }

    /// <summary>A swing through the air: noise through a band that sweeps up and back down.</summary>
    public static float[] Swing(int seed)
    {
        var r = new Lcg((uint)(seed * 92821 + 11));
        double dur = r.Range(0.24, 0.32);
        int n = (int)(dur * RATE);
        var outp = new float[n];
        var band = new Band();
        double lo = r.Range(300, 450), hi = r.Range(1100, 1600);
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / n;
            if ((i & 31) == 0) band.Tune(lo + (hi - lo) * Math.Sin(Math.PI * Math.Pow(t, 0.8)), 1.6);
            double env = Math.Pow(Math.Sin(Math.PI * Math.Pow(t, 0.7)), 2);
            outp[i] = (float)(band.Run(r.Next() * 2 - 1) * env);
        }
        return Normalise(outp, 0.7f);
    }

    /// <summary>A gunshot: a crack, a low boom, and the shot coming back off what's round it.</summary>
    public static float[] Gunshot(int seed)
    {
        var r = new Lcg((uint)(seed * 49157 + 5));
        int n = (int)(1.1 * RATE);
        var outp = new float[n];
        double phase = 0, lp = 0, tail = 0, prev = 0;
        double f0 = r.Range(55, 70);
        int echo = (int)(r.Range(0.08, 0.15) * RATE);
        for (int i = 0; i < n; i++)
        {
            double s = (double)i / RATE;
            double white = r.Next() * 2 - 1;
            double hp = white - prev; prev = white;
            double crack = (white * 0.6 + hp * 0.6) * Math.Exp(-s / 0.004);
            phase += 2 * Math.PI * (35 + (f0 - 35) * Math.Exp(-s / 0.06)) / RATE;
            double boom = Math.Sin(phase) * Math.Exp(-s / 0.09);
            lp += (white - lp) * 0.08;
            double body = lp * 3 * Math.Exp(-s / 0.045);
            tail += (white - tail) * 0.04;
            double rumble = tail * 2.2 * Math.Exp(-s / 0.3) * Smooth(Math.Min(1, s / 0.02));
            outp[i] = (float)(crack + boom * 0.9 + body + rumble * 0.5);
        }
        // The slap back: a darker copy, later and quieter.
        var copy = (float[])outp.Clone();
        double d = 0;
        for (int i = echo; i < n; i++) { d += (copy[i - echo] - d) * 0.3; outp[i] += (float)(d * 0.35); }
        return Normalise(outp, 0.98f);
    }

    /// <summary>Glass going: a burst, then shards ringing high and falling, fewer and quieter as they settle.</summary>
    public static float[] Glass(int seed)
    {
        var r = new Lcg((uint)(seed * 65537 + 13));
        int n = (int)(0.9 * RATE);
        var outp = new float[n];
        double prev = 0;
        for (int i = 0; i < (int)(0.05 * RATE); i++)
        {
            double s = (double)i / RATE, white = r.Next() * 2 - 1, hp = white - prev; prev = white;
            outp[i] += (float)((hp * 0.8 + white * 0.3) * Math.Exp(-s / 0.012));
        }
        int shards = 45 + (int)(r.Next() * 30);
        for (int k = 0; k < shards; k++)
        {
            double start = 0.6 * Math.Pow(r.Next(), 2.2), f = r.Range(2200, 7500), f2 = f * r.Range(2.1, 2.7);
            double tau = r.Range(0.012, 0.06), amp = r.Range(0.2, 0.7) * (1 - start * 1.1);
            int i0 = (int)(start * RATE), len = (int)(tau * 5 * RATE);
            for (int j = 0; j < len && i0 + j < n; j++)
            {
                double s = (double)j / RATE, e = Math.Exp(-s / tau);
                outp[i0 + j] += (float)(amp * e * (Math.Sin(2 * Math.PI * f * s) + (f2 < NYQUIST ? 0.4 * Math.Sin(2 * Math.PI * f2 * s) : 0)));
            }
        }
        return Normalise(outp, 0.85f);
    }

    /// <summary>Thunder: a split crack, then a long roll that swells and falls away.</summary>
    public static float[] Thunder(int seed)
    {
        var r = new Lcg((uint)(seed * 104651 + 17));
        int n = (int)(5.5 * RATE);
        var outp = new float[n];
        double brown = 0, lp = 0, lp2 = 0;
        var swells = new (double at, double width, double amp)[4];
        for (int k = 0; k < swells.Length; k++) swells[k] = (r.Range(0.3, 2.8), r.Range(0.2, 0.7), r.Range(0.4, 1));
        double crackAt = r.Range(0, 0.15);
        for (int i = 0; i < n; i++)
        {
            double s = (double)i / RATE, white = r.Next() * 2 - 1;
            brown = brown * 0.995 + white * 0.1;
            lp += (brown - lp) * 0.05;
            lp2 += (white - lp2) * 0.3;
            double env = Smooth(Math.Min(1, s / 0.25)) * Math.Exp(-s / 1.6) * 0.5;
            foreach (var w in swells) env += w.amp * Math.Exp(-Math.Pow((s - w.at) / w.width, 2)) * Math.Exp(-s / 2.5);
            double crack = s > crackAt ? lp2 * 1.2 * Math.Exp(-(s - crackAt) / 0.08) : 0;
            outp[i] = (float)(lp * 6 * env + crack);
        }
        return Normalise(outp, 0.95f);
    }

    // ---- Loops ----

    /// <summary>Wind: a broad band of noise whose centre and strength rise and fall in gusts, and a faint whistle.</summary>
    public static float[] Wind(int seed)
    {
        var r = new Lcg((uint)(seed * 7907 + 19));
        int n = 10 * RATE;
        var raw = LoopNoise(r, n, (x) => x);
        var outp = new float[n];
        var band = new Band();
        var whistle = new Band();
        // Gusts: slow sines with a whole number of cycles in the loop, so they join too.
        var g = new (int k, double ph, double a)[] { (1, r.Range(0, 6.3), 0.5), (2, r.Range(0, 6.3), 0.3), (5, r.Range(0, 6.3), 0.2) };
        // Two passes round the circle: the first only warms the filters, so the second starts where it ends.
        double pink = 0, dark = 0;
        for (int pass = 0; pass < 2; pass++)
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / n, gust = 0;
                foreach (var (k, ph, a) in g) gust += a * Math.Sin(2 * Math.PI * k * t + ph);
                gust = 0.5 + 0.5 * gust; // 0..1-ish
                if ((i & 31) == 0)
                {
                    band.Tune(220 + 380 * gust, 0.8);
                    whistle.Tune(650 + 250 * gust, 25);
                }
                pink += (raw[i] - pink) * 0.25;
                dark += (pink - dark) * 0.3;
                outp[i] = (float)(band.Run(dark) * (0.35 + 0.65 * gust) + whistle.Run(raw[i]) * 0.6 * gust * gust);
            }
        return Level(outp, 0.8f);
    }

    /// <summary>Rain: a steady hiss, a low patter, and drops landing all round.</summary>
    public static float[] Rain(int seed)
    {
        var r = new Lcg((uint)(seed * 6563 + 23));
        int n = 4 * RATE;
        double hpPrev = 0, hpOut = 0, lp = 0, patter = 0;
        var hiss = LoopNoise(r, n, (x) =>
        {
            hpOut = 0.92 * (hpOut + x - hpPrev); hpPrev = x; // a gentle high-pass
            lp += (hpOut - lp) * 0.6;
            return lp;
        });
        var low = LoopNoise(r, n, (x) => patter += (x - patter) * 0.04);
        var outp = new float[n];
        for (int i = 0; i < n; i++) outp[i] = (float)(hiss[i] * 0.35 + low[i] * 1.2);
        int drops = 4 * 260;
        for (int k = 0; k < drops; k++)
        {
            long at = (long)(r.Next() * n);
            double f = r.Range(1400, 5200), tau = r.Range(0.003, 0.009), amp = r.Range(0.05, 0.4);
            int len = (int)(tau * 5 * RATE);
            for (int j = 0; j < len; j++) Lay(outp, at + j, amp * Math.Exp(-(double)j / RATE / tau) * Math.Sin(2 * Math.PI * f * j / RATE));
        }
        return Level(outp, 0.8f);
    }

    /// <summary>A night's insects: two or three crickets chirping at their own pace, high and thin.</summary>
    public static float[] Night(int seed)
    {
        var r = new Lcg((uint)(seed * 5501 + 29));
        int n = 8 * RATE;
        var outp = new float[n];
        int crickets = 3;
        for (int c = 0; c < crickets; c++)
        {
            double f = r.Range(3900, 4900), amp = c == 0 ? 1 : r.Range(0.25, 0.6), period = r.Range(0.55, 0.95);
            int pulses = 3 + (int)(r.Next() * 2);
            double pulse = r.Range(0.010, 0.016);
            for (double t = r.Range(0, period); t < 8; t += period * r.Range(0.93, 1.07))
            {
                for (int p = 0; p < pulses; p++)
                {
                    long at = (long)((t + p * pulse * 2) * RATE);
                    int len = (int)(pulse * RATE);
                    for (int j = 0; j < len; j++)
                    {
                        double e = Math.Sin(Math.PI * j / len);
                        Lay(outp, at + j, amp * e * e * Math.Sin(2 * Math.PI * f * j / RATE));
                    }
                }
            }
        }
        return Level(outp, 0.6f);
    }

    /// <summary>An engine idling: firings at 30 a second (a whole number in the loop), each a low knock with a rasp.
    /// Played faster as the car speeds up.</summary>
    public static float[] Engine(int seed)
    {
        var r = new Lcg((uint)(seed * 4421 + 31));
        int n = RATE;
        var outp = new float[n];
        const int FIRINGS = 30;
        var rasp = new Band(); rasp.Tune(900, 1.2);
        var noise = LoopNoise(r, n, (x) => rasp.Run(x));
        for (int k = 0; k < FIRINGS; k++)
        {
            long at = (long)k * n / FIRINGS;
            double amp = r.Range(0.75, 1), f = r.Range(55, 65);
            int len = (int)(0.06 * RATE);
            for (int j = 0; j < len; j++)
            {
                double s = (double)j / RATE, e = Math.Exp(-s / 0.014);
                Lay(outp, at + j, amp * e * (Math.Sin(2 * Math.PI * f * s) + 0.5 * Math.Sin(2 * Math.PI * 2 * f * s) + 0.6 * noise[(int)((at + j) % n)]));
            }
        }
        for (int i = 0; i < n; i++) outp[i] += (float)(0.15 * Math.Sin(2 * Math.PI * 30.0 * i / n) + 0.25 * noise[i]);
        return Level(outp, 0.8f);
    }

    /// <summary>A generator: a small engine's buzz at 50 firings a second, a rattle, and an alternator's whine.</summary>
    public static float[] Generator(int seed)
    {
        var r = new Lcg((uint)(seed * 3307 + 37));
        int n = RATE;
        var outp = new float[n];
        var band = new Band(); band.Tune(1500, 2);
        var noise = LoopNoise(r, n, (x) => band.Run(x));
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / RATE;
            double ph = (50 * t) % 1; // the firing's place in its cycle
            double buzz = Math.Exp(-ph * 9) * (Math.Sin(2 * Math.PI * 100 * t) + 0.6 * Math.Sin(2 * Math.PI * 150 * t) + 0.4 * Math.Sin(2 * Math.PI * 250 * t));
            double rattle = noise[i] * (0.3 + 0.7 * Math.Exp(-ph * 6));
            double whine = 0.08 * Math.Sin(2 * Math.PI * 400 * t);
            outp[i] = (float)(buzz * 0.8 + rattle * 0.9 + whine);
        }
        return Level(outp, 0.7f);
    }

    /// <summary>An alarm bell: a clapper striking 16 times a second, each strike ringing a bell's partials.</summary>
    public static float[] Alarm(int seed)
    {
        var r = new Lcg((uint)(seed * 2203 + 41));
        int n = RATE;
        var outp = new float[n];
        double f = r.Range(1150, 1350);
        var partials = new (double m, double tau, double a)[] { (1, 0.09, 1), (2.76, 0.05, 0.5), (5.40, 0.03, 0.3), (8.93, 0.02, 0.15) };
        const int STRIKES = 16;
        for (int k = 0; k < STRIKES; k++)
        {
            long at = (long)k * n / STRIKES;
            double amp = r.Range(0.8, 1);
            int len = (int)(0.4 * RATE);
            for (int j = 0; j < len; j++)
            {
                double s = (double)j / RATE, v = 0;
                foreach (var (m, tau, a) in partials) if (f * m < NYQUIST) v += a * Math.Exp(-s / tau) * Math.Sin(2 * Math.PI * f * m * s);
                Lay(outp, at + j, amp * v);
            }
        }
        return Level(outp, 0.75f);
    }

    /// <summary>A helicopter: the rotor's blade slaps (11 a second), a low beat, and the turbine's whine.</summary>
    public static float[] Helicopter(int seed)
    {
        var r = new Lcg((uint)(seed * 1709 + 43));
        int n = 2 * RATE;
        var outp = new float[n];
        var band = new Band(); band.Tune(500, 1.3);
        var noise = LoopNoise(r, n, (x) => band.Run(x));
        double lp = 0;
        var rumble = LoopNoise(r, n, (x) => lp += (x - lp) * 0.03);
        const int SLAPS = 22;
        for (int k = 0; k < SLAPS; k++)
        {
            long at = (long)k * n / SLAPS;
            int len = (int)(0.08 * RATE);
            double amp = r.Range(0.8, 1);
            for (int j = 0; j < len; j++)
            {
                double s = (double)j / RATE, e = Math.Exp(-s / 0.022);
                Lay(outp, at + j, amp * e * (noise[(int)((at + j) % n)] * 3 + Math.Sin(2 * Math.PI * 45 * s) * 0.8));
            }
        }
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / RATE;
            outp[i] += (float)(rumble[i] * 3 + 0.05 * Math.Sin(2 * Math.PI * 2800 * t) + 0.03 * Math.Sin(2 * Math.PI * 4150 * t));
        }
        return Level(outp, 0.8f);
    }
}
