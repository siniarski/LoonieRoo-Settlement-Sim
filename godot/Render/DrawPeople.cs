// Drawing people and creatures (src/render/drawPeople.ts): survivors, zombies, wanderers, herds,
// task marks, and the debug paths. Reads sim state only.

using System;
using System.Collections.Generic;
using System.Linq;
using LoonieRoo.Sim;
using static LoonieRoo.Game.Render.CameraMath;

namespace LoonieRoo.Game.Render;

public sealed partial class Painter
{
    /// <summary>How long a swing's arc stays on screen, in ticks.</summary>
    const double SWING_SHOWN = 3;

    /// <summary>Debug: a line from each worker to their job's tile, so reservations are visible.</summary>
    void drawJobLink(SimState state, Survivor s, double alpha)
    {
        var job = s.jobId != null ? Jobs.getJob(state, s.jobId) : null;
        if (job == null) return;
        var w = worldPos(s, alpha);
        var from = ToScreen(w.x, w.y);
        var to = ToScreen(job.x + 0.5, job.y + 0.5);
        var r = tileRect(job.x, job.y);
        ctx.strokeStyle = COLOURS.jobLine;
        ctx.lineWidth = 1.5;
        ctx.beginPath();
        ctx.moveTo(from.x, from.y);
        ctx.lineTo(to.x, to.y);
        ctx.stroke();
        ctx.strokeRect(r.x + 0.5, r.y + 0.5, r.w - 1, r.h - 1);
    }

    /// <summary>A herd on the move: a red haze with its size, drawn even in the fog once heard.</summary>
    void drawHerd(Herd h, double size)
    {
        var p = ToScreen(h.x + 0.5, h.y + 0.5);
        double r = Math.Max(6, size * 1.2);
        ctx.fillStyle = "rgba(255, 90, 74, 0.25)";
        ctx.beginPath();
        ctx.arc(p.x, p.y, r, 0, Math.PI * 2);
        ctx.fill();
        ctx.strokeStyle = "rgba(255, 90, 74, 0.8)";
        ctx.lineWidth = 1.5;
        ctx.stroke();
        ctx.fillStyle = "#ffd0c8";
        ctx.font((int)Math.Round(Math.Max(10, size * 0.5)), bold: true);
        ctx.textAlign = "center";
        ctx.textBaseline = "middle";
        ctx.fillText(h.size.ToString(), p.x, p.y);
    }

    /// <summary>A greyed survivor with a "?" above: waiting to be taken in.</summary>
    void drawWanderer(double x, double y, string name, double size, string mark = "?", bool enemy = false)
    {
        var p = ToScreen(x + 0.5, y + 0.5);
        double radius = Math.Max(3, size * 0.32);
        ctx.fillStyle = enemy ? COLOURS.enemy : COLOURS.wanderer;
        ctx.strokeStyle = COLOURS.survivorOutline;
        ctx.lineWidth = 2;
        ctx.setLineDash(new double[] { 3, 2 });
        ctx.beginPath();
        ctx.arc(p.x, p.y, radius, 0, Math.PI * 2);
        ctx.fill();
        ctx.stroke();
        ctx.setLineDash(NoDash);
        ctx.fillStyle = COLOURS.wandererMark;
        ctx.font((int)Math.Round(Math.Max(10, size * 0.5)), bold: true);
        ctx.textAlign = "center";
        ctx.textBaseline = "middle";
        ctx.fillText(mark, p.x, p.y);
        if (size >= MIN_LABEL_PX)
        {
            ctx.fillStyle = COLOURS.label;
            ctx.font((int)Math.Round(Math.Min(14, size * 0.45)));
            ctx.textBaseline = "bottom";
            ctx.fillText(name, p.x, p.y - radius - 3);
        }
    }

    static readonly Dictionary<string, string> VARIANT_COLOURS = new()
    {
        ["walker"] = COLOURS.zombie, ["runner"] = "#8fbf5a", ["brute"] = "#4f6b3a", ["screamer"] = "#9a7ab8", ["crawler"] = "#6a7a5a",
    };

    /// <summary>A sickly green blob with red eyes; health bar once hurt; debug line to its target.</summary>
    void drawZombie(SimState state, Zombie z, DrawOptions options, double size)
    {
        var world = worldPos(z, options.alpha);
        var p = ToScreen(world.x, world.y);
        // Brutes are big, runners and crawlers small.
        double scale = z.variant == "brute" ? 1.35 : z.variant is "walker" or "screamer" ? 1 : 0.8;
        double radius = Math.Max(3, size * 0.34 * scale);
        if (options.showDebug && z.targetId != null)
        {
            var target = state.survivors.FirstOrDefault((s) => s.id == z.targetId);
            if (target != null)
            {
                var tw = worldPos(target, options.alpha);
                var tp = ToScreen(tw.x, tw.y);
                ctx.strokeStyle = COLOURS.zombieChase;
                ctx.lineWidth = 1.5;
                ctx.setLineDash(new double[] { 2, 3 });
                ctx.beginPath();
                ctx.moveTo(p.x, p.y);
                ctx.lineTo(tp.x, tp.y);
                ctx.stroke();
                ctx.setLineDash(NoDash);
            }
        }
        // Winding up a bite: a red ring closing in. Just hit: a pale flash. Reeling: a grey outline.
        if (z.windup > 0)
        {
            double t = Math.Min(1, (Zombies.WINDUP_TICKS - z.windup + options.alpha) / Zombies.WINDUP_TICKS);
            ctx.strokeStyle = COLOURS.windup;
            ctx.lineWidth = 1.5 + 2 * t;
            ctx.beginPath();
            ctx.arc(p.x, p.y, radius + 2 + 6 * (1 - t), 0, Math.PI * 2);
            ctx.stroke();
        }
        bool flash = state.tick - z.hurtTick < 2;
        ctx.fillStyle = flash ? COLOURS.hitFlash : z.kind == "raider" ? "#9a5a4a" : VARIANT_COLOURS.TryGetValue(z.variant, out var vc) ? vc : COLOURS.zombie;
        ctx.strokeStyle = state.tick < z.staggerUntil ? COLOURS.staggered : COLOURS.zombieOutline;
        ctx.lineWidth = 2;
        ctx.beginPath();
        ctx.arc(p.x, p.y, radius, 0, Math.PI * 2);
        ctx.fill();
        ctx.stroke();
        double eye = Math.Max(1, radius * 0.18);
        ctx.fillStyle = COLOURS.zombieEye;
        ctx.fillRect(p.x - radius * 0.4 - eye / 2, p.y - radius * 0.2, eye, eye);
        ctx.fillRect(p.x + radius * 0.4 - eye / 2, p.y - radius * 0.2, eye, eye);
        if (z.health < z.maxHealth)
        {
            double w = Math.Max(10, size * 0.8);
            double h = Math.Max(2, Math.Round(size * 0.1));
            ctx.fillStyle = "rgba(0, 0, 0, 0.65)";
            ctx.fillRect(p.x - w / 2, p.y + radius + 3, w, h);
            ctx.fillStyle = COLOURS.needLow;
            ctx.fillRect(p.x - w / 2, p.y + radius + 3, w * z.health / z.maxHealth, h);
        }
    }

    static readonly Dictionary<string, string> TASK_LABELS = new()
    {
        ["home"] = "old home", ["bury"] = "the dead", ["pet"] = "the dog", ["burn"] = "burn this", ["radio"] = "radio call", ["medicine"] = "medicine here",
    };

    /// <summary>A task's spot: a gold diamond (shown through fog, like a heard herd), labelled once zoomed in.</summary>
    void drawTask(Task t, double size)
    {
        var p = ToScreen(t.x + 0.5, t.y + 0.5);
        double r = Math.Max(4, size * 0.4);
        ctx.strokeStyle = COLOURS.controlled;
        ctx.lineWidth = 2;
        ctx.beginPath();
        ctx.moveTo(p.x, p.y - r);
        ctx.lineTo(p.x + r, p.y);
        ctx.lineTo(p.x, p.y + r);
        ctx.lineTo(p.x - r, p.y);
        ctx.closePath();
        ctx.stroke();
        if (size < MIN_LABEL_PX) return;
        ctx.fillStyle = COLOURS.controlled;
        ctx.font((int)Math.Round(Math.Min(13, size * 0.42)));
        ctx.textAlign = "center";
        ctx.textBaseline = "bottom";
        ctx.fillText(TASK_LABELS.TryGetValue(t.kind, out var l) ? l : t.kind, p.x, p.y - r - 2);
    }

    void drawSurvivor(SimState state, Survivor s, DrawOptions options, double size)
    {
        var world = worldPos(s, options.alpha);
        var p = ToScreen(world.x, world.y);
        // Children are smaller.
        double radius = Math.Max(3, size * (s.childUntil != null ? 0.22 : 0.32));

        if (s.id == options.selectedId)
        {
            ctx.strokeStyle = COLOURS.selected;
            ctx.lineWidth = 2;
            ctx.beginPath();
            ctx.arc(p.x, p.y, radius + 3, 0, Math.PI * 2);
            ctx.stroke();
        }
        var control = state.control?.survivorId == s.id ? state.control : null;
        if (control != null)
        {
            // You: a gold ring, a tick showing which way you face, and a bar while searching or eating.
            ctx.strokeStyle = COLOURS.controlled;
            ctx.lineWidth = 2;
            ctx.beginPath();
            ctx.arc(p.x, p.y, radius + 5, 0, Math.PI * 2);
            ctx.stroke();
            // Where you look: the mouse aim if there is one, else the way you face.
            var look = control.aim ?? control.facing;
            double len = Math.Sqrt(look.x * look.x + look.y * look.y);
            double fx = len > 0 ? look.x / len : 0, fy = len > 0 ? look.y / len : 1;
            ctx.lineWidth = 3;
            ctx.beginPath();
            ctx.moveTo(p.x + fx * (radius + 2), p.y + fy * (radius + 2));
            ctx.lineTo(p.x + fx * (radius + size * 0.35), p.y + fy * (radius + size * 0.35));
            ctx.stroke();
            // A swing: the arc it covers (reach, 60 degrees each side of facing), fading over SWING_SHOWN ticks.
            double age = state.tick + options.alpha - (control.swungTick ?? -99);
            if (age >= 0 && age < SWING_SHOWN)
            {
                double facing = Math.Atan2(fy, fx);
                double reach = (double)Motion.SWING_REACH / Motion.SUB * size;
                ctx.save();
                ctx.globalAlpha = 0.55 * (1 - age / SWING_SHOWN);
                ctx.fillStyle = COLOURS.swing;
                ctx.beginPath();
                ctx.moveTo(p.x, p.y);
                ctx.arc(p.x, p.y, reach, facing - Math.PI / 3, facing + Math.PI / 3);
                ctx.closePath();
                ctx.fill();
                ctx.restore();
            }
            if (control.busy != null)
            {
                double bw = Math.Max(12, size);
                ctx.fillStyle = "rgba(0, 0, 0, 0.65)";
                ctx.fillRect(p.x - bw / 2, p.y - radius - 9, bw, 4);
                ctx.fillStyle = COLOURS.controlled;
                ctx.fillRect(p.x - bw / 2, p.y - radius - 9, bw * control.busy.progress / control.busy.duration, 4);
            }
        }
        bool asleep = s.sleep?.asleep == true;
        // Clothing shows as the outline: brown for a coat, grey for armour; boots as a dark bar below.
        var body = s.body != null ? state.items.FirstOrDefault((i) => i.id == s.body) : null;
        ctx.fillStyle = asleep ? COLOURS.survivorAsleep : COLOURS.survivor;
        ctx.strokeStyle = body != null ? (COLOURS.GLYPHS.TryGetValue(body.kind, out var bg) ? bg.colour : COLOURS.survivorOutline) : COLOURS.survivorOutline;
        ctx.lineWidth = body != null ? 3 : 2;
        ctx.beginPath();
        ctx.arc(p.x, p.y, radius, 0, Math.PI * 2);
        ctx.fill();
        ctx.stroke();
        if (s.feet != null)
        {
            ctx.fillStyle = COLOURS.GLYPHS["boots"].colour;
            ctx.fillRect(p.x - radius * 0.6, p.y + radius - 1, radius * 1.2, Math.Max(2, radius * 0.3));
        }
        var carried = s.carrying != null ? state.items.FirstOrDefault((i) => i.id == s.carrying) : null;
        if (carried != null) drawItem(carried.kind, world.x + 0.3, world.y + 0.25, size * 0.6);
        if (s.weapon != null) drawWeapon(world.x - 0.35, world.y + 0.05, size * 0.85);

        // Need bars under the survivor once they matter: rest (blue) below half or
        // while sleeping, food (orange) below half or while eating. Red when urgent.
        var bars = new List<(double value, double max, string colour)>();
        if (s.health < Health.HEALTH_MAX) bars.Add((s.health, Health.HEALTH_MAX, s.health < Health.HEALTH_LOW ? COLOURS.needLow : COLOURS.health));
        if (asleep || s.rest < Rest.REST_MAX / 2.0) bars.Add((s.rest, Rest.REST_MAX, s.rest < Rest.TIRED ? COLOURS.needLow : COLOURS.rest));
        if (s.meal != null || s.satiety < Food.SATIETY_MAX / 2.0) bars.Add((s.satiety, Food.SATIETY_MAX, s.satiety < Food.HUNGRY ? COLOURS.needLow : COLOURS.satiety));
        double w = Math.Max(10, size * 0.8);
        double h = Math.Max(2, Math.Round(size * 0.1));
        for (int i = 0; i < bars.Count; i++)
        {
            double y = p.y + radius + 3 + i * (h + 1);
            ctx.fillStyle = "rgba(0, 0, 0, 0.65)";
            ctx.fillRect(p.x - w / 2, y, w, h);
            ctx.fillStyle = bars[i].colour;
            ctx.fillRect(p.x - w / 2, y, w * bars[i].value / bars[i].max, h);
        }

        if (size >= MIN_LABEL_PX)
        {
            int px = (int)Math.Round(Math.Min(14, size * 0.45));
            ctx.fillStyle = COLOURS.label;
            ctx.font(px);
            ctx.textAlign = "center";
            ctx.textBaseline = "bottom";
            ctx.fillText(asleep ? $"{s.name} z" : s.breakdown != null ? $"{s.name} !" : s.name, p.x, p.y - radius - 3);
            if (s.wish != null)
            {
                ctx.fillStyle = s.wish.promised ? COLOURS.controlled : COLOURS.label;
                ctx.fillText(s.wish.promised ? "◆" : "◇", p.x, p.y - radius - 3 - px);
            }
        }
    }

    /// <summary>Debug path preview: from the survivor's drawn position through every remaining tile.</summary>
    void drawPath(Survivor s, double alpha)
    {
        var points = new List<P> { worldPos(s, alpha) };
        if (s.move != null) points.Add(new P(s.move.to.x + 0.5, s.move.to.y + 0.5));
        foreach (var q in s.path) points.Add(new P(q.x + 0.5, q.y + 0.5));
        if (points.Count < 2) return;
        ctx.strokeStyle = COLOURS.path;
        ctx.lineWidth = 2;
        ctx.setLineDash(new double[] { 4, 4 });
        ctx.beginPath();
        for (int i = 0; i < points.Count; i++)
        {
            var p = ToScreen(points[i].x, points[i].y);
            if (i == 0) ctx.moveTo(p.x, p.y);
            else ctx.lineTo(p.x, p.y);
        }
        ctx.stroke();
        ctx.setLineDash(NoDash);
    }

    void drawGoal(Point goal, double size)
    {
        var p = ToScreen(goal.x + 0.5, goal.y + 0.5);
        double r = Math.Max(3, size * 0.25);
        ctx.strokeStyle = COLOURS.goal;
        ctx.lineWidth = 2;
        ctx.beginPath();
        ctx.moveTo(p.x - r, p.y - r);
        ctx.lineTo(p.x + r, p.y + r);
        ctx.moveTo(p.x + r, p.y - r);
        ctx.lineTo(p.x - r, p.y + r);
        ctx.stroke();
    }
}
