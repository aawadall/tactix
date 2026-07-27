# Tactix — product roadmap (indie game)

This document is the **product** plan: how to turn the current engine + Unity
shell into a **solid indie tactics game**. For rules/ML feature designs (spotting,
fog, POW, schema bumps), see [`ROADMAP.md`](ROADMAP.md).

---

## North star

**Ship a polished single-player tactics game** (solo vs AI), with hotseat kept as
a free bonus mode.

Definition of done for v1.0 (indie):

- A stranger can finish a 30–45 minute skirmish vs a respectable AI without a
  walkthrough from the author.
- Scouting, objectives, and force composition create decisions every turn — not
  only attrition.
- Presentation is intentional (UI, audio, feedback), even if art stays
  procedural / NATO APP-6.
- Windows build is stable enough for Steam Early Access or a paid $8–15 launch.

Secondary: the JSONL log pipeline and pure `Tactix.Core` engine remain first-class
— every rules change still bumps `GameLogger.SchemaVersion` when logs change
behaviour or shape.

---

## Current baseline (already shipped)

Treat these as done unless noted:

| Area | Status |
|------|--------|
| Pure rules engine + EditMode tests | Done |
| Continuous movement, combat, support, merge/split | Done |
| Objectives, turn limit, score / elimination / rout / decapitation | Done |
| Hotseat, vs random bot, bot-vs-bot self-play + logs | Done |
| Procedural / standard maps, NATO symbols, contours, Field Manual | Done |
| Order queues, pathfinding, curved paths, unit autonomy | Done |
| Multi-unit select + group Move | **In progress / incomplete** |

The project is past a tech demo and in **internal alpha**. It is not yet a
sellable product.

---

## Phased plan

### Phase 0 — Stabilize the play loop (1–2 weeks)

**Goal:** playtests measure the game, not the bugs.

| # | Work | Exit criterion |
|---|------|----------------|
| 0.1 | Finish multi-select (Shift+click, drag-box) + group Move / Engage / Garrison / Clear | Compiles; group orders work in-game |
| 0.2 | First-game tutorial overlay (orders clock, AUTO, Hold vs Move, Shift multi-select) | New player completes one turn without verbal help |
| 0.3 | Fix UI overlap / layout (telemetry, order strip, banners) | No critical panels stacked on each other |
| 0.4 | Pacing pass: clock interval, turn limit feel, objective markers on board | Match length feels intentional |

**Phase exit:** a stranger finishes one vs-bot game with only in-game help.

---

### Phase 1 — Opponent worth fighting (3–6 weeks)

**Goal:** replace “random legal moves” as the human-facing AI.

| # | Work | Notes |
|---|------|-------|
| 1.1 | `TacticalBot` (heuristic): objective bias, focus fire, protect HQ / medics, don’t waste support | Keep `RandomBot` for self-play / logs |
| 1.2 | Difficulty: Easy / Normal / Hard (aggression, reorganise rate, advance bias) | Menu + log header field |
| 1.3 | Optional shallow lookahead or scripted openings | Only if 1.1 still feels dumb |

**Phase exit:** Normal AI wins ~40–60% vs a competent human; losses feel earned.

---

### Phase 2 — Tactical depth players feel (4–8 weeks)

**Goal:** decisions beyond “who has more HP.” Prefer player-visible depth from
[`ROADMAP.md`](ROADMAP.md), sequenced for game value:

| # | Work | Size | Schema |
|---|------|------|--------|
| 2.1 | Artillery spotting (Recon / sight matter) | S | bump |
| 2.2 | Fog of war + observation logging | L | structural |
| 2.3 | POW / capture when isolated at 0 HP | S–M | bump |
| 2.4 | Objective / score UX polish (markers, contested state, win reasons) | S | none if already logged |

**Defer:** full 3D; heavy combat RNG (keep deterministic default). Prefer
hillshade if elevation is hard to read.

**Phase exit:** every turn asks “what can I see / spot / hold?” not only “what
can I kill?”

---

### Phase 3 — Content & structure (4–6 weeks)

**Goal:** reasons to play again beyond one skirmish.

| # | Work |
|---|------|
| 3.1 | Skirmish options: map source, size, fog on/off, turn limit, difficulty — **pre-game map generator** ([`docs/PREGAME_MAP_GENERATOR.md`](docs/PREGAME_MAP_GENERATOR.md)). **MG0–MG1 shipped:** Map Workshop (preview, size, Reroll, Standard, Start Match) + cartographic board (paper + contours + symbols). Remaining: fog, difficulty, MG2+ sliders |
| 3.2 | Scenario pack (6–12): named maps, asymmetric OOB, short briefings (historical battles → backlog B2) |
| 3.3 | Debrief screen: score timeline, losses, outcome reason |
| 3.4 | Optional campaign lite (5 linked battles) — cut if schedule slips |

**Phase exit:** main menu offers Scenario / Skirmish / Hotseat (not only three
raw mode buttons).

---

### Phase 4 — Product feel (3–5 weeks)

**Goal:** intentional presentation without abandoning the map aesthetic.

| Pillar | Minimum bar |
|--------|-------------|
| UI | Clear hierarchy, keybind legend, readable fonts/contrast |
| Audio | Move / fire / heal / end-turn / objective + short music loop |
| Juice | Attack flash, HP pop, objective pulse |
| Accessibility | Colorblind-safe sides, scalable UI text |
| Meta | Settings (volume, fullscreen), confirm abandon game |

**Phase exit:** looks and sounds like a designed game, not a debug harness.

---

### Phase 5 — Ship shell (2–4 weeks)

| # | Work |
|---|------|
| 5.1 | Release Windows build + basic crash / log folder docs |
| 5.2 | Steam page draft (trailer, caps, short/long description) — private OK |
| 5.3 | External playtest (≈5 people); fix top friction bugs |
| 5.4 | Balance patch from playtests + self-play logs |

**Phase exit:** someone who isn’t the author would pay $8–15 for a weekend of
play (Early Access or 1.0).

---

## Timeline (solo / part-time)

```text
Now ── Phase 0 ── Phase 1 ── Phase 2 ── Phase 3+4 ── Phase 5
       1–2 wk     3–6 wk     4–8 wk     6–10 wk      2–4 wk
                 └──────── solid game core ────────┘
```

| Path | Estimate |
|------|----------|
| Full plan (fog + scenarios + polish) | ~4–6 months part-time |
| Ruthless cut (no fog/campaign; good AI + skirmish + polish) | ~2–3 months part-time |

---

## Explicit non-goals (until after 1.0)

- Full 3D renderer rewrite
- Online multiplayer / matchmaking
- Shipping a live RL policy as the only opponent (train offline later; ship
  heuristic or distilled policy)
- Expanding rules surface without playtests
- Mobile / console ports

---

## Extended backlog (post-1.0 / expansion)

Ideas worth keeping, but **not** on the critical path to a solid indie v1.0.
Each will need a design pass in [`ROADMAP.md`](ROADMAP.md) before implementation
(schema, ML, and rules authority).

### B1. Comms overhead (latency, noise, hijack)

Orders are currently instantaneous once queued. Model **command and control** as
a first-class friction:

| Mode | Player-facing effect (sketch) |
|------|-------------------------------|
| **Latency** | Orders take N clock ticks / turns before the unit acts; higher echelons or long range = more delay |
| **Noise** | Chance an order is garbled (wrong waypoint, dropped queue slot) under disruption or distance |
| **Hijack** | Enemy EW / recon can intercept or spoof orders in a radius; player sees contested C2 |

Ties naturally to autonomy (units act on stale or missing orders) and fog.
Likely schema bump: per-order timestamps, C2 state, disruption zones.

### B2. Historical battle scenarios

Expand Phase 3.2 beyond generic named maps into **historical (or historically
inspired) scenarios**: period OOB, briefing, victory conditions matching the
engagement, fixed or semi-fixed maps. Start with a small pack (3–5) once skirmish
scenarios work; treat authenticity vs playability as an explicit design choice
per scenario.

### B3. Era packs — units, divisions, comms, echelon

Parameterize the force model by **era** (e.g. WWII / Cold War / near-future):

- Unit types and stats appropriate to the era
- Division / formation templates (what a “side” spawns with)
- Comms model defaults (which of B1 applies, and how severely)
- Echelon ladder and amalgamation rules tuned per era

Skirmish / scenario select picks an era; Field Manual pages filter by era.
Large content + balance surface — ship one era polished before adding a second.

### B4. RL agents exchanging learned policies

Beyond offline self-play logs: train policies that can be **exported, shared, and
loaded** as opponents or co-pilots.

| Piece | Sketch |
|-------|--------|
| **Policy artifact** | Versioned weights + observation/action contract tied to `schemaVersion` |
| **Exchange** | Local files first; later optional registry / workshop-style sharing |
| **In-game use** | “Play vs downloaded policy,” hotseat vs policy, or policy as Blue advisor |
| **Safety** | Policies only propose `GameAction`s; `Rules.Apply` still rejects illegal play |

Does **not** require a realtime game server for v1 of this feature (file-based
exchange is enough). A server only matters if exchange becomes always-online
matchmaking against live agents.

Depends on: stable observation encoding (esp. after fog), and keeping logs
training-honest. Complements — does not replace — Phase 1 `TacticalBot` for
shipping a default opponent.

---

## Relationship to `ROADMAP.md`

| `ROADMAP.md` entry | Product phase |
|--------------------|---------------|
| Artillery spotting | Phase 2.1 |
| Victory conditions | **Done** — polish UX in 2.4 |
| Fog of war | Phase 2.2 |
| POW capture | Phase 2.3 |
| Combat uncertainty | Post-1.0 |
| Full 3D | Do not do; hillshade optional |
| Comms / eras / historical scenarios | Extended backlog B1–B3 |
| RL policy exchange | Extended backlog B4 |

Engine constraints still apply: rules stay authoritative in `Tactix.Core`;
schema stability remains a top value for training data.

---

## Immediate next tickets

1. Finish multi-select + group Move / Engage / Garrison.
2. First-game tutorial overlay.
3. Start `TacticalBot` (objective + focus-fire heuristics).
4. Pre-game map generator MG0–MG1 — **done** (Map Workshop + cartographic map). MG2+ next.

---

## Success metrics

| Metric | Target before calling it “solid” |
|--------|----------------------------------|
| Tutorial completion | ≥80% of new playtesters finish turn 1 unaided |
| Match length | Most skirmishes end in 20–50 minutes |
| AI fairness | Normal: human win rate 40–60% after learning curve |
| Friction bugs | Zero “can’t select / can’t issue order / UI covers board” bugs |
| Would pay? | ≥3 of 5 external playtesters say yes at $8–15 |
