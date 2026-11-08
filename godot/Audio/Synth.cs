// The game's sounds, made by code at startup (2026-11-09: no downloads, no audio files in git).
// A moan is a voice: a pulse at a low, sliding pitch, built harmonic by harmonic and shaped by a
// throat's formants (the resonances that make a vowel), with breath noise, a slow wobble and a
// rattle (vocal fry: every other pulse weaker). A blow landing is a low thump, a dull slap of
// noise and a short crack. Each variant comes from its own seed, so the same sounds every run.
// Pure arithmetic over float samples; Sounds.cs turns them into Godot streams.

using System;
using System.Collections.Generic;

namespace LoonieRoo.Game.Audio;

public static class Synth
{
    public const int RATE = 22050;

    /// <summary>A small seeded generator (the renderer's own; the sim's RNG is never touched).</summary>
    internal sealed class Lcg
    {
        uint s;
        public Lcg(uint seed) { s = seed * 2654435761u + 1; }
        public double Next() { s = s * 1664525u + 1013904223u; return (s >> 8) / 16777216.0; }
        public double Range(double a, double b) => a + (b - a) * Next();
    }

    /// <summary>Vowel formants (Hz, bandwidth Hz), a large throat's: "oo", "uh", "ah", "aw".</summary>
    static readonly (double f, double b)[][] VOWELS =
    {
        new[] { (290.0, 70.0), (780.0, 90.0), (2150.0, 160.0) },
        new[] { (500.0, 80.0), (1100.0, 100.0), (2300.0, 170.0) },
        new[] { (680.0, 90.0), (1050.0, 110.0), (2350.0, 180.0) },
        new[] { (560.0, 80.0), (860.0, 100.0), (2250.0, 170.0) },
    };

    /// <summary>A moan. `angry`: the snarl of one that has seen you (higher, harsher, sudden); else a low idle groan.</summary>
    public static float[] Moan(int seed, bool angry)
    {
        var r = new Lcg((uint)(seed * 7919 + (angry ? 104729 : 0)));
        double dur = angry ? r.Range(0.9, 1.5) : r.Range(1.4, 2.4);
        int n = (int)(dur * RATE);
        double f0Start = angry ? r.Range(120, 160) : r.Range(85, 115);
        double f0Peak = f0Start * (angry ? r.Range(1.05, 1.2) : r.Range(1.0, 1.08));
        double f0End = angry ? r.Range(70, 90) : r.Range(55, 72);
        double attack = angry ? r.Range(0.04, 0.1) : r.Range(0.25, 0.45);
        double fry = angry ? r.Range(0.35, 0.55) : r.Range(0.2, 0.4);
        double breath = angry ? 0.12 : 0.07;
        double vibRate = r.Range(3.5, 6), vibDepth = r.Range(0.01, 0.025);
        int vFrom = (int)(r.Next() * VOWELS.Length), vTo = (vFrom + 1 + (int)(r.Next() * (VOWELS.Length - 1))) % VOWELS.Length;
        double throat = r.Range(0.85, 0.97); // a bigger or smaller throat: all formants scaled

        var outp = Sing(r, n, f0Start, f0Peak, f0End, attack, fry, breath, vibRate, vibDepth, VOWELS[vFrom], VOWELS[vTo], throat, 4500);
        return Normalise(outp, angry ? 0.9f : 0.75f);
    }

    /// <summary>A screamer's shriek: the same voice, high and strained ("ee" opening to "ah"), long, all breath and rattle at the end.</summary>
    public static float[] Scream(int seed)
    {
        var r = new Lcg((uint)(seed * 104723 + 7));
        double dur = r.Range(1.6, 2.2);
        int n = (int)(dur * RATE);
        double f0Start = r.Range(330, 420);
        var ee = new[] { (360.0, 90.0), (2300.0, 160.0), (3000.0, 220.0) };
        var outp = Sing(r, n, f0Start, f0Start * r.Range(1.25, 1.4), r.Range(180, 230), 0.05, r.Range(0.45, 0.6), 0.1,
            r.Range(6, 8), r.Range(0.03, 0.05), ee, VOWELS[2], r.Range(1.0, 1.1), 7000);
        return Normalise(outp, 0.95f);
    }

    /// <summary>A voice: a pulse at a sliding pitch (up to a peak in the first fifth, then sagging), built harmonic by
    /// harmonic up to maxHz and shaped by three formants drifting from one vowel to another; vocal fry, breath, an envelope.</summary>
    static float[] Sing(Lcg r, int n, double f0Start, double f0Peak, double f0End, double attack, double fry, double breath,
        double vibRate, double vibDepth, (double f, double b)[] from, (double f, double b)[] to, double throat, double maxHz)
    {
        const int MAXH = 64;
        var phase = new double[MAXH + 1];
        var amp = new double[MAXH + 1];
        var outp = new float[n];
        double jitter = 0, half = 0, noiseLp = 0, noiseBp = 0, noiseBp2 = 0;
        int harmonics = 1;
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / n;
            // Pitch: up to a peak in the first fifth, then sagging to the end; a wandering jitter and a slow wobble.
            double f0 = t < 0.2 ? f0Start + (f0Peak - f0Start) * (t / 0.2) : f0Peak + (f0End - f0Peak) * Math.Pow((t - 0.2) / 0.8, 0.8);
            if ((i & 63) == 0) jitter = jitter * 0.9 + (r.Next() - 0.5) * 0.02;
            f0 *= 1 + jitter + vibDepth * Math.Sin(2 * Math.PI * vibRate * i / RATE);
            // The harmonics' strengths: recompute every 64 samples (the vowel drifts slowly).
            if ((i & 63) == 0)
            {
                double w = t * t * (3 - 2 * t);
                harmonics = Math.Min(MAXH, (int)(maxHz / f0));
                for (int k = 1; k <= harmonics; k++)
                {
                    double f = k * f0, g = 0.015;
                    for (int j = 0; j < 3; j++)
                    {
                        double ff = throat * (from[j].f * (1 - w) + to[j].f * w);
                        double bw = from[j].b * (1 - w) + to[j].b * w;
                        double x = (f - ff) / (bw / 2);
                        g += (j == 0 ? 1 : j == 1 ? 0.6 : 0.25) / (1 + x * x);
                    }
                    amp[k] = g / Math.Pow(k, 0.65);
                }
            }
            double v = 0;
            for (int k = 1; k <= harmonics; k++)
            {
                phase[k] += 2 * Math.PI * k * f0 / RATE;
                if (phase[k] > 2 * Math.PI) phase[k] -= 2 * Math.PI;
                v += amp[k] * Math.Sin(phase[k]);
            }
            // The rattle: amplitude swung at half the pitch, so pulses alternate strong and weak.
            half += Math.PI * f0 / RATE;
            v *= 1 - fry * (0.5 + 0.5 * Math.Sin(half));
            // Breath: noise, low-passed then band-passed round the first formant region.
            double white = r.Next() * 2 - 1;
            noiseLp += (white - noiseLp) * 0.35;
            noiseBp += (noiseLp - noiseBp) * 0.08;
            noiseBp2 = noiseLp - noiseBp;
            double breathEnv = 0.5 + 0.5 * Math.Abs(1 - 2 * t); // more at the start and the end
            v += breath * breathEnv * noiseBp2 * 3;
            // The envelope: a rise, a hold, a long fall.
            double s = (double)i / RATE;
            double env = s < attack ? Smooth(s / attack) : 1;
            if (t > 0.55) env *= Math.Pow(1 - (t - 0.55) / 0.45, 1.6);
            outp[i] = (float)(v * env);
        }
        return outp;
    }

    /// <summary>A blow landing on a body: a thump, a slap and a crack. `heavy`: the killing blow (lower, longer).</summary>
    public static float[] Hit(int seed, bool heavy)
    {
        var r = new Lcg((uint)(seed * 15485863 + (heavy ? 32452843 : 0)));
        double dur = heavy ? 0.45 : 0.3;
        int n = (int)(dur * RATE);
        var outp = new float[n];
        double f0 = heavy ? r.Range(70, 85) : r.Range(90, 120), fEnd = heavy ? 38 : 50;
        double phase = 0, lp = 0, lp2 = 0, hp = 0, prev = 0;
        double thumpTau = heavy ? 0.11 : 0.07, slapTau = heavy ? 0.04 : 0.028;
        double crack = r.Range(0.2, 0.45);
        for (int i = 0; i < n; i++)
        {
            double s = (double)i / RATE;
            double f = fEnd + (f0 - fEnd) * Math.Exp(-s / 0.05);
            phase += 2 * Math.PI * f / RATE;
            double thump = Math.Sin(phase) * Math.Exp(-s / thumpTau);
            double white = r.Next() * 2 - 1;
            lp += (white - lp) * 0.12;
            lp2 += (lp - lp2) * 0.25;
            double slap = lp2 * 4 * Math.Exp(-s / slapTau);
            hp = white - prev; prev = white;
            double crk = hp * crack * Math.Exp(-s / 0.004);
            double attack = Smooth(Math.Min(1, s / 0.002));
            outp[i] = (float)((thump * 0.9 + slap * 0.6 + crk) * attack);
        }
        return Normalise(outp, heavy ? 0.95f : 0.85f);
    }

    internal static double Smooth(double x) => x * x * (3 - 2 * x);

    internal static float[] Normalise(float[] s, float peak)
    {
        float max = 0;
        foreach (var v in s) max = Math.Max(max, Math.Abs(v));
        if (max > 0) for (int i = 0; i < s.Length; i++) s[i] *= peak / max;
        // A few ms of fade at the end, so nothing clicks off.
        int fade = Math.Min(s.Length, RATE / 200);
        for (int i = 0; i < fade; i++) s[s.Length - 1 - i] *= (float)i / fade;
        return s;
    }

    /// <summary>16-bit little-endian PCM, as AudioStreamWav wants it.</summary>
    public static byte[] Pcm16(float[] s)
    {
        var b = new byte[s.Length * 2];
        for (int i = 0; i < s.Length; i++)
        {
            short v = (short)Math.Clamp((int)Math.Round(s[i] * 32767), -32768, 32767);
            b[2 * i] = (byte)(v & 0xff);
            b[2 * i + 1] = (byte)((v >> 8) & 0xff);
        }
        return b;
    }

    /// <summary>Root-mean-square level (the self-test checks a sound isn't silent).</summary>
    public static double Rms(IReadOnlyList<float> s)
    {
        double sum = 0;
        foreach (var v in s) sum += v * v;
        return s.Count == 0 ? 0 : Math.Sqrt(sum / s.Count);
    }
}
