// Footsteps by surface, made by code (2026-11-09, the playtest: "each ground its own steps"). Same rules as
// Synth.cs: pure arithmetic over float samples, each variant from its own seed, the same every run.
// A step is a heel and then the ball of the foot (a second, smaller impact 40-70 ms on), each exciting what
// that ground does:
//   grass     a soft thud, the blades brushing (a high hiss that comes and goes)
//   dirt      a thud and a gritty crunch
//   field     soft tilled earth (a low crunch, a deeper thud) and stalks rustling
//   forest    leaf litter: a dry crackle of tiny clicks, every other variant a twig snapping
//   road      tarmac: the heel's click and a scuff of grit
//   paving    slabs: a click, a scuff, and a slab rocking (a short stony knock)
//   concrete  a sharp dry click and a sandy scrape
//   tiles     a hard bright click, a short ring, and the room giving it back
//   boards    a hollow knock (a floor over a void), every third variant a creak
//   bridge    heavy planks over water: a deep hollow boom and a loose plank's clack
//   water     shallow water: a splash, bubbles, a trickle running off

using System;
using static LoonieRoo.Game.Audio.Synth;
using Band = LoonieRoo.Game.Audio.SynthWorld.Band;

namespace LoonieRoo.Game.Audio;

public static class SynthSteps
{
    /// <summary>The surfaces a foot can come down on: the grid's grounds (Grids.GROUNDS) and shallow water.</summary>
    public static readonly string[] SURFACES = { "grass", "dirt", "road", "forest", "bridge", "field", "paving", "boards", "tiles", "concrete", "water" };

    /// <summary>A footstep on `surface` (one of SURFACES), variant `seed`.</summary>
    public static float[] Step(int seed, string surface)
    {
        int k = Array.IndexOf(SURFACES, surface);
        var r = new Lcg((uint)(seed * 31337 + k * 7717 + 3));
        double dur = surface switch { "bridge" => 0.36, "water" => 0.42, "tiles" => 0.26, "boards" => 0.28, _ => 0.22 };
        int n = (int)(dur * RATE);
        var outp = new double[n];
        double toeAt = r.Range(0.04, 0.07), toeAmp = r.Range(0.4, 0.65);
        // The two impacts' envelope with decay d (s): the heel at 0, the ball of the foot at toeAt.
        double Hit(double s, double d) => Math.Exp(-s / d) + (s >= toeAt ? toeAmp * Math.Exp(-(s - toeAt) / d) : 0);

        // The layers, set per surface (an amplitude of 0: not there).
        double thumpF = r.Range(65, 90), thumpD = 0.025, thumpA = 0.6, heel = 0.08, heelD = 0.003;
        double crunchF = 1200, crunchQ = 0.9, crunchA = 0, crunchD = 0.05, grainP = 0.55;
        double scuffF = 2200, scuffQ = 1.4, scuffA = 0, scuffD = 0.03;
        double hissF = 3000, hissQ = 0.8, hissA = 0, hissD = 0.08;
        double ring1F = 0, ring2F = 0, ringQ = 7, ring1A = 0, ring2A = 0, ringD = 0.05;
        double echo = 0, echoAt = 0.03, level = 0.8;
        int crackles = 0;
        bool snap = false, creak = false, splash = false, rattle = false;
        switch (surface)
        {
            case "grass":
                thumpF = r.Range(55, 75); thumpD = 0.03; thumpA = 0.5; heel = 0.03;
                crunchF = 450; crunchQ = 0.8; crunchA = 1.0; crunchD = 0.04; grainP = 0.8;
                hissF = r.Range(2400, 3200); hissA = 1.4; hissD = 0.07; level = 0.6; break;
            case "dirt":
                thumpF = r.Range(70, 95); heel = 0.1;
                crunchF = r.Range(950, 1300); crunchA = 3.0; crunchD = 0.045; scuffA = 0.5; scuffD = 0.02; level = 0.7; break;
            case "field":
                thumpF = r.Range(55, 75); thumpD = 0.035; thumpA = 0.8; heel = 0.02;
                crunchF = r.Range(600, 800); crunchQ = 0.8; crunchA = 2.0; grainP = 0.4;
                hissF = r.Range(3200, 4000); hissQ = 1; hissA = 1.0; hissD = 0.1; level = 0.6; break;
            case "forest":
                thumpF = r.Range(65, 85); thumpD = 0.03; thumpA = 0.55; heel = 0.05;
                crunchF = r.Range(1600, 2000); crunchQ = 0.8; crunchA = 1.2; crunchD = 0.08; grainP = 0.9;
                crackles = 8 + (int)(r.Next() * 8); snap = seed % 2 == 1; level = 0.7; break;
            case "road":
                thumpF = r.Range(70, 90); thumpD = 0.02; heel = 0.7;
                scuffF = r.Range(2200, 2700); scuffQ = 1.2; scuffA = 1.4; crunchF = 1500; crunchA = 0.6; crunchD = 0.02; grainP = 0.4; break;
            case "paving":
                thumpF = r.Range(70, 90); thumpD = 0.02; heel = 0.8; scuffF = 2000; scuffA = 1.0; scuffD = 0.025;
                ring1F = r.Range(380, 450); ringQ = 6; ring1A = 2.0; ringD = 0.03; break;
            case "concrete":
                thumpF = r.Range(80, 100); thumpD = 0.015; thumpA = 0.5; heel = 1.0; heelD = 0.0025;
                scuffF = r.Range(2800, 3300); scuffQ = 1.6; scuffA = 1.6; scuffD = 0.035;
                ring1F = r.Range(1500, 1700); ringQ = 10; ring1A = 0.5; ringD = 0.02; level = 0.85; break;
            case "tiles":
                thumpF = r.Range(90, 120); thumpD = 0.012; thumpA = 0.5; heel = 0.6; heelD = 0.002; scuffA = 0.3; scuffD = 0.015;
                ring1F = r.Range(2600, 3200); ring2F = r.Range(4200, 4800); ringQ = 14; ring1A = 7; ring2A = 4; ringD = 0.04;
                echo = 0.25; echoAt = r.Range(0.025, 0.04); level = 0.85; break;
            case "boards":
                thumpF = r.Range(110, 140); thumpD = 0.05; thumpA = 0.8; heel = 0.25;
                ring1F = r.Range(170, 230); ring2F = r.Range(420, 520); ringQ = 7; ring1A = 5; ring2A = 3; ringD = 0.07;
                creak = seed % 3 == 2; level = 0.85; break;
            case "bridge":
                thumpF = r.Range(70, 90); thumpD = 0.09; thumpA = 1.0; heel = 0.3;
                ring1F = r.Range(95, 125); ring2F = r.Range(260, 320); ringQ = 5; ring1A = 5; ring2A = 2.5; ringD = 0.12;
                rattle = true; level = 0.9; break;
            case "water":
                thumpF = r.Range(45, 60); thumpD = 0.04; thumpA = 0.4; heel = 0; splash = true;
                hissF = r.Range(4000, 5000); hissQ = 0.9; hissA = 0.5; hissD = 0.15; grainP = 0.7; level = 0.75; break;
        }
        var crunch = new Band(); crunch.Tune(crunchF, crunchQ);
        var scuff = new Band(); scuff.Tune(scuffF, scuffQ);
        var hiss = new Band(); hiss.Tune(hissF, hissQ);
        var ring1 = new Band(); ring1.Tune(Math.Max(ring1F, 50), ringQ);
        var ring2 = new Band(); ring2.Tune(Math.Max(ring2F, 50), ringQ * 1.2);
        var splashBand = new Band(); splashBand.Tune(r.Range(800, 1300), 0.7);
        // Leaf litter: tiny clicks over the first 120 ms. A twig: one crack with a short ring.
        var clicks = new (double at, double amp)[crackles];
        for (int c = 0; c < crackles; c++) clicks[c] = (r.Range(0, 0.12), r.Range(0.3, 1));
        double snapAt = r.Range(0.03, 0.08);
        var twig = new Band(); twig.Tune(r.Range(1100, 1500), 8);
        // Water: bubbles, each a sine whose pitch rises as it dies away.
        int nb = splash ? 4 + (int)(r.Next() * 6) : 0;
        var bubbles = new (double at, double f, double d, double amp)[nb];
        for (int b = 0; b < nb; b++) bubbles[b] = (r.Range(0.01, 0.25), r.Range(400, 1400), r.Range(0.01, 0.025), r.Range(0.2, 0.5));
        double creakF = r.Range(90, 130), rattleAt = r.Range(0.03, 0.05);
        var rattleBand = new Band(); rattleBand.Tune(r.Range(650, 800), 4);

        double phase = 0, lp = 0, prev = 0, grain = 1, creakPh = 0;
        for (int i = 0; i < n; i++)
        {
            double s = (double)i / RATE;
            double white = r.Next() * 2 - 1;
            double hp = white - prev; prev = white;
            lp += (white - lp) * 0.5;
            if ((i & 31) == 0) grain = r.Next() < grainP ? r.Range(0.3, 1) : 0.1;
            // A 2 ms rise on the noisy layers, so the heel isn't a step in the noise.
            double atk = Smooth(Math.Min(1, s / 0.002));
            phase += 2 * Math.PI * thumpF / RATE;
            double v = Math.Sin(phase) * Hit(s, thumpD) * thumpA;
            v += hp * heel * Hit(s, heelD);
            if (crunchA > 0) v += crunch.Run(lp) * crunchA * grain * Hit(s, crunchD) * atk;
            if (scuffA > 0) v += scuff.Run(white) * scuffA * Hit(s, scuffD) * atk;
            if (hissA > 0) v += hiss.Run(white) * hissA * grain * Hit(s, hissD) * atk;
            // Resonances (a slab, a board, a tile): noise through a narrow band, dying with the material's ring.
            double ex = white * Hit(s, ringD) * atk;
            if (ring1A > 0) v += ring1.Run(ex) * ring1A;
            if (ring2A > 0) v += ring2.Run(ex) * ring2A;
            foreach (var (at, amp) in clicks)
                if (s >= at && s < at + 0.004) v += hp * amp * Math.Exp(-(s - at) / 0.0008);
            if (snap && s >= snapAt)
            {
                bool crack = s < snapAt + 0.002;
                v += (crack ? hp * 1.5 : 0) + twig.Run(crack ? white : 0) * 2.5;
            }
            if (creak && s > 0.06 && s < 0.22)
            {
                // A stick-slip buzz: a pulse train at the creak's pitch, rising a little, swelling and fading.
                double u = (s - 0.06) / 0.16;
                creakPh += 2 * Math.PI * creakF * (1 + 0.3 * u) / RATE;
                v += (Math.Sin(creakPh) > 0.9 ? 1 : 0) * 0.35 * Math.Sin(Math.PI * u) * (0.6 + 0.4 * r.Next());
            }
            if (rattle && s >= rattleAt) v += rattleBand.Run(s < rattleAt + 0.003 ? white : 0) * 4;
            if (splash)
            {
                double sw = Smooth(Math.Min(1, s / 0.012)) * (Math.Exp(-s / 0.07) + 0.5 * (s >= toeAt ? Math.Exp(-(s - toeAt) / 0.06) : 0));
                v += splashBand.Run(lp) * 2.5 * sw * (0.5 + 0.5 * grain);
                foreach (var (at, f, d, amp) in bubbles)
                {
                    double t = s - at;
                    if (t < 0 || t > d * 6) continue;
                    v += Math.Sin(2 * Math.PI * f * (t + t * t / (d * 4))) * amp * Math.Exp(-t / d) * Smooth(Math.Min(1, t / 0.001));
                }
            }
            outp[i] = v;
        }
        // Indoors (tiles): the room gives it back, a little later and duller.
        if (echo > 0)
        {
            int at = (int)(echoAt * RATE);
            double e = 0;
            var dry = (double[])outp.Clone();
            for (int i = at; i < n; i++) { e += (dry[i - at] - e) * 0.35; outp[i] += e * echo; }
        }
        return Normalise(SynthWorld.Floats(outp), (float)level);
    }
}
