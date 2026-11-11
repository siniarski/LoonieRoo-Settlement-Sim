// The HUD's colony panels (part of Hud; Hud.cs has the rest): what you are asked or offered as the colony's
// leader, each a panel that shows only while there is something to decide: the helicopter (who goes), succession,
// a survivor's story, prisoners, neighbours (gifts, requests), traders, expeditions, crafting, wanderers, and the
// survivor table with everyone's job priorities. Each reads state and sends commands; none decides a rule.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;
using Control = Godot.Control;
using LoonieRoo.Game.Render;
using LoonieRoo.Sim;
using Time = LoonieRoo.Sim.Time;

namespace LoonieRoo.Game.Ui;

public sealed partial class Hud
{
    /// <summary>The helicopter is down: who goes?</summary>
    void UpdateHelicopter(SimState state)
    {
        bool shown = state.endgame.helicopter;
        helicopter.Show(shown, $"{state.survivors.Count}|{state.control?.survivorId}", (r) =>
        {
            r.Text($"The helicopter is down until dusk. {(state.endgame.escaped > 0 ? $"({state.endgame.escaped} flown out so far.) " : "")}Who goes?", Kit.Title);
            r.Button("Everyone (a new start elsewhere)", () => files.NewGamePlus(), tip: "A new game+: a fresh world with the same settings, and you in it alone, with what you carry, your skills and your story.");
            if (state.control != null && state.survivors.Count > 1) r.Button("The others; I stay", () => Command(new Command { type = "escape", who = "others" }), tip: "Everyone but you flies out. You stay, and can call again for anyone else you find.");
            r.Button("Wave it off", () => Command(new Command { type = "escape", who = "none" }), tip: "Nobody goes. You can call again.");
        });
    }

    /// <summary>When the one you controlled dies: who carries on? The game waits for the choice (Game.AwaitingHeir).</summary>
    void UpdateSuccession(SimState state)
    {
        var s = state.succession;
        var ready = s != null ? SuccessionMod.heirs(state) : new List<Survivor>();
        string key = s != null ? J(new object[] { s.name, s.cause, ready.Select((h) => $"{h.id}:{h.resolve}") }) : "";
        succession.Show(s != null, key, (r) =>
        {
            r.Text($"{s!.name} died of {s.cause}. Who carries on?", Kit.Title);
            void choose(string label, string choice, string about) => r.Button(label, () =>
            {
                Command(new Command { type = "succeed", choice = choice });
                view.selectedId = state.control?.survivorId ?? view.selectedId;
            }, tip: about);
            foreach (var h in ready) choose($"{h.name} (resolve {h.resolve})", h.id, $"{h.name} takes over: you play as them from here.");
            if (ready.Count == 0 && state.survivors.Count > 0) r.Text($"Nobody left has the resolve to step up ({SuccessionMod.STEP_UP}+).", Ctx.Css("#dddddd"));
            choose("Start anew, far from here", "anew", "A new survivor, alone, somewhere else in this world, with a weapon and a bandage.");
            if (state.survivors.Count > 0) choose("Just watch the colony", "none", "Nobody under your control; the colony runs itself.");
        });
    }

    /// <summary>The selected survivor's story so far, what they've asked of you, and a Talk button.</summary>
    void UpdateStory(SimState state)
    {
        var s = state.survivors.FirstOrDefault((x) => x.id == view.selectedId && x.id != state.control?.survivorId);
        var told = s != null ? StoryMod.storyLines(s.story).Take((int)s.story.told).ToList() : new List<string>();
        var asks = s != null ? Talk.describeTasks(state, s) : new List<string>();
        string wish = s?.wish != null ? $"{Wishes.describeWish(state, s.wish)}|{s.wish.promised}|{Math.Ceiling((s.wish.until - state.tick) / Time.TICKS_PER_HOUR)}" : "";
        story.Show(s != null, J(new object?[] { s?.id, told.Count, asks, wish }), (r) =>
        {
            r.Text($"{s!.name}'s story", Kit.Title);
            foreach (var line in told.Count > 0 ? told.Select((l) => $"\"{l}\"") : new[] { "You don't know much about them yet." }) r.Text(line, Ctx.Css("#dddddd"), 420);
            foreach (var ask in asks) r.Text($"{s.name} {ask}", Kit.Gold, 420);
            if (s.wish != null)
            {
                var row = r.Row();
                double hours = Math.Ceiling((s.wish.until - state.tick) / Time.TICKS_PER_HOUR);
                row.Text($"Wants {Wishes.describeWish(state, s.wish)} ({hours}h left{(s.wish.promised ? ", promised" : "")})", s.wish.promised ? Kit.Gold : null);
                if (!s.wish.promised) row.Button("Promise", () => Command(new Command { type = "promiseWish", survivorId = s.id }), tip: "See it done for a bigger lift, and they think better of you. Let it run out and they are let down.");
            }
            r.Button($"Talk to {s.name}", () => Command(new Command { type = "talk", survivorId = s.id }), tip: "Once every couple of hours. While you control someone, they need to be next to them (or press T).");
        });
    }

    /// <summary>Prisoners in the cells: talk them round, or let them go.</summary>
    void UpdatePrisoners(SimState state, bool solo)
    {
        var rows = state.prisoners.Select((p) => (p, ready: state.tick - p.since >= Prisoners.RECRUIT_DAYS * Time.DAY_LENGTH)).ToList();
        prisoners.Show(rows.Count > 0 && !solo, J(rows.Select((x) => $"{x.p.id}:{x.ready}")), (r) =>
        {
            r.Text("Prisoners (each eats a food a day)", Kit.Title);
            foreach (var (p, ready) in rows)
            {
                var row = r.Row();
                row.Text($"{p.name} at ({p.x}, {p.y})");
                row.Button(ready ? "Recruit" : "Recruit (not yet)", () => Command(new Command { type = "recruitPrisoner", prisonerId = p.id }), disabled: !ready);
                row.Button("Let go", () => Command(new Command { type = "releasePrisoner", prisonerId = p.id }));
            }
        });
    }

    /// <summary>" · allies: X · feuding: Y" for a settlement, or "".</summary>
    static string standing(SimState state, Settlement n)
    {
        var others = state.settlements.Where((o) => o != n && !Colonies.isFallen(o)).ToList();
        var allies = others.Where((o) => Settlements.relation(n, o) == "allies").Select((o) => o.name).ToList();
        var feuds = others.Where((o) => Settlements.relation(n, o) == "feud").Select((o) => o.name).ToList();
        return $"{(allies.Count > 0 ? $" · allies: {string.Join(", ", allies)}" : "")}{(feuds.Count > 0 ? $" · feuding: {string.Join(", ", feuds)}" : "")}";
    }

    readonly Dictionary<string, ulong> raidArmed = new();

    /// <summary>Neighbouring settlements: what they think of you, and a gift button.</summary>
    void UpdateNeighbours(SimState state, bool solo)
    {
        string key = J(new object[] { state.settlements.Select((n) => $"{n.id}{n.reputation}{n.people}{n.food}{n.defence}{J(n.ties)}"), state.requests.Count, state.items.Count, Errands.errandsOf(state).Count, Time.dayOf(state.tick), raidArmed.Count });
        neighbours.Show(state.settlements.Count > 0 && !solo, key, (r) =>
        {
            r.Text("Neighbours", Kit.Title);
            int food = state.items.Count((i) => i.kind == "food" && i.pos != null && i.reservedBy == null);
            foreach (var n in state.settlements)
            {
                var row = r.Row();
                string mood = n.reputation <= Settlements.HOSTILE ? "hostile" : n.reputation >= Settlements.FRIENDLY ? "friendly" : "wary";
                row.Text(Colonies.isFallen(n)
                    ? $"{n.name} ({n.x}, {n.y}): fallen, nobody left"
                    : $"{n.name} ({n.x}, {n.y}): {mood} {n.reputation} · {n.people} people, {Colonies.hungerOf(n)}, walls {n.defence}/{Colonies.MAX_DEFENCE}{standing(state, n)}");
                if (Colonies.isFallen(n)) continue;
                row.Button($"Gift {Settlements.GIFT_FOOD} food", () => Command(new Command { type = "giftSettlement", settlementId = n.id }), disabled: food < Settlements.GIFT_FOOD);
                // The browser asks to confirm; here the first click arms it, a second within 3 s raids.
                bool armed = raidArmed.TryGetValue(n.id, out var t) && Godot.Time.GetTicksMsec() < t;
                row.Button(armed ? "Raid: sure?" : "Raid", () =>
                {
                    if (armed) { raidArmed.Remove(n.id); Command(new Command { type = "raidSettlement", settlementId = n.id }); }
                    else raidArmed[n.id] = Godot.Time.GetTicksMsec() + 3000;
                    neighbours.Invalidate();
                }, tip: "Send everyone armed to take their food. They will count you an enemy, win or lose.");
            }
            foreach (var e in Errands.errandsOf(state))
            {
                var n = state.settlements.FirstOrDefault((s) => s.id == e.settlementId);
                double days = Math.Max(0, Math.Ceiling((e.until - state.tick) / Time.DAY_LENGTH));
                r.Text($"{n?.name ?? "?"} asks: {(e.kind == "nest" ? "clear the dead" : $"find {e.who}")} near ({e.x}, {e.y}), {days} day{(days == 1 ? "" : "s")} left (on your map)");
            }
            foreach (var q in state.requests)
            {
                var n = state.settlements.FirstOrDefault((s) => s.id == q.settlementId);
                int have = state.items.Count((i) => i.kind == q.kind && i.pos != null && i.reservedBy == null);
                var row = r.Row();
                row.Text($"{n?.name ?? "?"} asks for {q.count} {q.kind} (you have {have})");
                row.Button("Send", () => Command(new Command { type = "fulfilRequest", requestId = q.id }), disabled: have < q.count);
            }
        });
    }

    /// <summary>A trader's offers, each with a Trade button.</summary>
    void UpdateTraders(SimState state, bool solo)
    {
        traders.Show(state.traders.Count > 0 && !solo, J(new object[] { state.traders.Select((t) => $"{t.id}{t.traded}{t.offers.Count}"), state.items.Count }), (r) =>
        {
            foreach (var t in state.traders)
            {
                var from = state.settlements.FirstOrDefault((n) => n.id == t.from);
                r.Text($"Trader{(from != null ? $" from {from.name}" : "")} at ({t.x}, {t.y})", Kit.Title);
                for (int i = 0; i < t.offers.Count; i++)
                {
                    var o = t.offers[i];
                    int have = state.items.Count((it) => it.kind == o.want && it.pos != null && it.reservedBy == null);
                    var row = r.Row();
                    row.Text($"{o.giveCount} {o.give} for {o.wantCount} {o.want} (you have {have})");
                    int index = i;
                    row.Button("Trade", () => Command(new Command { type = "trade", traderId = t.id, offer = index }), disabled: have < o.wantCount);
                }
            }
        });
    }

    /// <summary>Known ruins to send the selected survivor to, and who's away.</summary>
    void UpdateExpeditions(SimState state, bool solo)
    {
        var selected = state.survivors.FirstOrDefault((s) => s.id == view.selectedId);
        var away = state.expeditions.Select((x) => (x, hours: Math.Ceiling((x.returnTick - state.tick) / Time.TICKS_PER_HOUR))).ToList();
        string key = J(new object?[] { selected?.id, state.pointsOfInterest.Select((p) => p.id), away.Select((a) => $"{a.x.id}:{a.hours}") });
        expeditions.Show((state.pointsOfInterest.Count > 0 || away.Count > 0) && !solo, key, (r) =>
        {
            r.Text("Expeditions", Kit.Title);
            foreach (var p in state.pointsOfInterest.Take(5))
            {
                var row = r.Row();
                row.Text($"{p.label} at ({p.x}, {p.y})");
                row.Button(selected != null ? $"Send {selected.name}" : "Select someone to send", () => { if (selected != null) Command(new Command { type = "sendExpedition", survivorIds = new() { selected.id }, poiId = p.id }); }, disabled: selected == null);
                row.Button("Outpost", () => { if (selected != null) Command(new Command { type = "sendExpedition", survivorIds = new() { selected.id }, poiId = p.id, outpost = true }); }, disabled: selected == null, tip: "Send them to stay there: supplies every dawn, at some risk");
            }
            foreach (var (x, hours) in away)
            {
                var row = r.Row();
                row.Text($"{string.Join(", ", x.members.Select((s) => s.name))} → {x.target.label} ({x.target.x}, {x.target.y}){(x.outpost ? ", holding it as an outpost" : $", back in about {hours}h")}");
                if (x.outpost) row.Button("Recall", () => Command(new Command { type = "recallOutpost", expeditionId = x.id }));
            }
        });
    }

    /// <summary>Stock targets for the workbenches: shown once there is a workbench (or a plan for one).</summary>
    void UpdateCrafting(SimState state, bool solo)
    {
        int benches = state.furniture.Count((f) => f.kind == "workbench");
        bool planned = state.blueprints.Any((b) => b.target == "workbench");
        var rows = Crafting.ORDERABLE.Select((k) => (k, have: Crafting.craftStock(state, k), want: state.craftTargets[k])).ToList();
        crafting.Show((benches > 0 || planned) && !solo, $"{benches}|{planned}|{J(rows.Select((x) => $"{x.k}{x.have}/{x.want}"))}", (r) =>
        {
            r.Text(benches == 0 ? "Workbench orders (once the bench is built)" : $"Workbench orders · {benches} bench{(benches == 1 ? "" : "es")}", Kit.Title);
            foreach (var (k, have, want) in rows)
            {
                var recipe = Crafting.RECIPES[k];
                var row = r.Row();
                row.Text($"{recipe.label} ({recipe.count} {recipe.input}{(recipe.workshopOnly == true ? ", workshop" : "")}): have {have}, keep");
                row.Button("−", () => Command(new Command { type = "setCraftTarget", kind = k, count = want - 1 }), disabled: want == 0);
                row.Text($"{want}", Kit.Title);
                row.Button("+", () => Command(new Command { type = "setCraftTarget", kind = k, count = want + 1 }), disabled: want >= Crafting.MAX_CRAFT_TARGET);
            }
        });
    }

    /// <summary>People waiting at the edge, each with Take in / Turn away.</summary>
    void UpdateWanderers(SimState state, bool solo)
    {
        double spare = Wanderers.spareBeds(state);
        var rows = state.wanderers.Select((w) => (w, left: (int)(Math.Ceiling(Wanderers.patienceLeft(state, w) / (Time.TICKS_PER_HOUR / 6.0)) * 10))).ToList();
        wanderers.Show(rows.Count > 0 && !solo, $"{spare}|{string.Join(",", rows.Select((x) => $"{x.w.id}:{x.left}"))}", (r) =>
        {
            r.Text($"Wanderers waiting · {(spare > 0 ? $"{spare} spare bed{(spare == 1 ? "" : "s")}" : "no spare beds")}", Kit.Title);
            foreach (var (w, left) in rows)
            {
                var row = r.Row();
                string traits = w.traits.Count > 0 ? $" ({string.Join(", ", w.traits.Select((t) => Skills.TRAIT_INFO[t].label))})" : "";
                row.Text($"{w.name}{traits} at ({w.x}, {w.y}) · leaves in {left / 60}h {left % 60:00}m");
                row.Button("Take in", () => Command(new Command { type = "acceptWanderer", wandererId = w.id }));
                row.Button("Turn away", () => Command(new Command { type = "dismissWanderer", wandererId = w.id }));
            }
        });
    }

    /// <summary>Click cycles 1 → 2 → 3 → 4 → off → 1; right-click goes the other way.</summary>
    public static double cyclePriority(double p, bool backwards)
    {
        double[] order = { 1, 2, 3, 4, 0 };
        int i = Array.IndexOf(order, p);
        return order[(i + (backwards ? order.Length - 1 : 1)) % order.Length];
    }

    void UpdatePriorities(SimState state, bool solo)
    {
        // Build mode takes the bottom of the screen: the panel steps aside until it closes.
        bool shown = view.showPriorities && !view.buildOpen && !solo;
        string key = shown ? J(new object?[] { view.selectedId, view.roster, state.survivors.Select((s) => J(new object?[] { s.id, s.name, s.resolve, s.social, s.comfort, s.shift, s.order?.kind, J(s.priorities), Card.activity(state, s), Card.moodTitle(state, s) })) }) : "";
        priorities.Show(shown, key, (r) =>
        {
            bool full = view.roster == "full";
            var head = r.Row();
            head.Text($"Survivors ({state.survivors.Count})", Ctx.Css("#c9cfc9"));
            if (view.roster != "folded") head.Button(full ? "Less" : "Jobs & needs", () => view.roster = full ? "short" : "full");
            head.Button(view.roster == "folded" ? "▸" : "▾", () => view.roster = view.roster == "folded" ? "short" : "folded", tip: view.roster == "folded" ? "Show the list" : "Fold the list away");
            if (view.roster == "folded") return;
            var heads = new List<string> { "Survivor", "Mood" };
            if (full) heads.AddRange(new[] { "Resolve", "Social", "Comfort", "Shift" });
            heads.Add("Order");
            if (full) heads.AddRange(JobTypes.JOB_TYPES.Select((t) => JOB_LABELS[t]));
            heads.Add("Doing");
            var grid = r.Add(new GridContainer { Columns = heads.Count, MouseFilter = MouseFilterEnum.Ignore });
            grid.AddThemeConstantOverride("h_separation", 6);
            var cells = new Rows(grid, 12);
            foreach (var h in heads) cells.Text(h, Kit.Dim);
            Color tone(bool low, bool high) => low ? Ctx.Css("#ff8a7a") : high ? Ctx.Css("#8cf0a4") : Kit.Text;
            foreach (var s in state.survivors)
            {
                var name = cells.Button(s.name, () => view.selectedId = s.id, active: s.id == view.selectedId,
                    tip: string.Join(" · ", new[] { $"Select · {Skills.describeSkills(s)}", Clothing.describeClothing(state, s), Relationships.describeRelations(state, s) }.Where((x) => x != "")));
                name.Flat = s.id != view.selectedId;
                double m = Mood.mood(state, s);
                cells.Text($"{m}", tone(Mood.isMiserable(state, s), m >= 60)).TooltipText = Card.moodTitle(state, s);
                if (full)
                {
                    cells.Text($"{s.resolve}", tone(Resolve.isBreaking(s), Resolve.isSteeled(s)));
                    cells.Text($"{s.social}", tone(s.social < Wellbeing.LOW, s.social > Wellbeing.HIGH));
                    cells.Text($"{s.comfort}", tone(s.comfort < Wellbeing.LOW, s.comfort > Wellbeing.HIGH));
                    cells.Button(s.shift == "night" ? "☾" : "☀", () => Command(new Command { type = "setShift", survivorId = s.id, shift = s.shift == "night" ? "day" : "night" }),
                        tip: $"{s.name} works the {s.shift} shift (sleeps {(s.shift == "night" ? "by day" : "at night")}). Click to switch.");
                }
                string orderGlyph = s.order == null ? "⌂" : s.order.kind == "follow" ? "→" : s.order.kind == "loot" ? "⚲" : "■";
                string next = s.order == null ? "follow" : s.order.kind == "follow" ? "hold" : "settle";
                cells.Button(orderGlyph, () => Command(new Command { type = "setOrder", survivorId = s.id, order = next }), tip: $"{s.name} is {Orders.describeOrder(s.order)}. Click: settle (colony jobs) → follow you → hold where they stand.");
                if (full)
                    foreach (var type in JobTypes.JOB_TYPES)
                    {
                        double p = s.priorities[type];
                        var b = cells.Button(p == 0 ? "–" : $"{p}", () => Command(new Command { type = "setPriority", survivorId = s.id, jobType = type, priority = cyclePriority(p, Input.IsKeyPressed(Key.Shift)) }),
                            tip: $"{JOB_LABELS[type]} priority for {s.name}. Click: higher number, Shift-click: back. – is off.");
                        b.AddThemeColorOverride("font_color", Ctx.Css(p == 1 ? "#8cf0a4" : p == 2 ? "#cfe8a0" : p == 3 ? "#e8d9a8" : p == 4 ? "#b0a890" : "#666e68"));
                    }
                cells.Text(Card.activity(state, s), Kit.Dim);
            }
        });
    }
}
