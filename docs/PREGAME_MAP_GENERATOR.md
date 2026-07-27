# Pre-game map generator — definition

Specification for a **pre-game** map creation flow: the player builds or rolls a
battlefield **before** the match starts, instead of only toggling
“Standard / Random” on the mode panel.

This is a product/design definition. Implementation should land under Phase 3
skirmish options (see [`PRODUCT_ROADMAP.md`](PRODUCT_ROADMAP.md)); core generation
already lives in [`MapGenerator`](Assets/Scripts/Core/MapGenerator.cs).

---

## Problem today

| What exists | Gap |
|-------------|-----|
| `LevelConfig.CreateStandardGame()` — fixed 24×24 baked map | No preview or authoring |
| `MapGenerator.Generate(w, h, seed)` — deterministic procedural map | Seed is opaque; size hard-coded to 24 in UI |
| Mode panel toggle: Standard vs Random | No size, biome, or objective controls; map appears only after Start |

Players cannot inspect or iterate a map before committing to a match.

---

## Goal

A **map shell** (C&C-style): the board is always visible on the left; a persistent
right **Command Dock** holds workshop controls before Start and match
stats/actions after. Chrome is industrial gunmetal with amber/green readouts,
cameo unit block, order button grid, and map bezel. Unit orders are issued only
from the dock — there is no floating menu over the map.

1. Boot into shell with a live generated preview.
2. Choose mode / size / Reroll / Standard in the dock.
3. **Start Match** — same framing; dock switches to command (status, unit stats, orders, End Turn).
4. Esc / Menu returns to the shell preview.

Constraints (unchanged project rules):

- Generation stays in `Tactix.Core`, pure and deterministic for `(spec, seed)`.
- `Rules` remains authoritative; the shell only produces a legal `GameState`
  start.
- Schema: log `mapSeed` plus structured `mapSpec` (schema v10).

---

## MapSpec (data contract)

Serializable description of what to generate. Suggested shape:

```jsonc
{
  "width": 24,                 // >= MapGenerator.MinimumSize (16)
  "height": 24,
  "seed": 123456789,           // null = roll on generate
  "symmetry": "rot180",        // rot180 | mirrorX | none (v1: rot180 only)
  "relief": 0.5,               // 0 = flat, 1 = rugged (scales noise weights)
  "forestDensity": 0.45,       // paint threshold bias
  "rockDensity": 0.2,
  "objectiveCount": 3,         // odd, centre + mirrored flanks (existing placer)
  "turnLimit": 60,
  "deploymentDepth": 3,        // flat open bands at each end
  "forceTemplate": "standard"  // standard OOB for now; later era packs
}
```

**v1 minimum:** `width`, `height`, `seed`, keep current noise/paint behaviour
(relief/forest/rock as optional sliders later).

API sketch:

```csharp
public sealed class MapSpec { /* fields above */ }

public static class MapGenerator
{
    // Existing:
    public static GameState Generate(int width, int height, int seed);

    // New:
    public static GameState Generate(MapSpec spec);           // terrain + deploy + objectives
    public static GameState GenerateTerrainOnly(MapSpec spec); // optional: preview without units
}
```

---

## Pre-game flow

```text
Main menu
   → [Skirmish / Vs Bot / Hotseat]
   → Map Workshop
        ┌─────────────────────────────────────┐
        │ Preview board (BoardRenderer)       │
        │ Seed · Size · [Reroll] [Standard] │
        │ (later: Relief / Forest / Rocks)    │
        │ [Back]              [Start Match]   │
        └─────────────────────────────────────┘
   → Match (orders clock, etc.)
```

### Workshop behaviours

| Control | Behaviour |
|---------|-----------|
| **Reroll** | New seed; regenerate; refresh preview |
| **Seed field** | Type/paste seed for shareable maps |
| **Size** | Presets 16 / 20 / 24 / 28 (square v1) |
| **Use standard** | Load baked `LevelConfig` map (no seed) |
| **Start match** | Lock spec+seed into `GameController`; begin mode |

Preview uses the same `BoardRenderer` path as the match (and later map-symbol
terrain if that polish lands). No gameplay input in workshop except camera if
needed.

---

## What the generator must guarantee

Same contracts as today’s `MapGenerator`:

1. **Reachability** — every tile reachable under the cliff rule (existing repair).
2. **Fairness** — 180° rotational symmetry for competitive skirmish (v1).
3. **Deployment** — flat open bands of `deploymentDepth` at each end.
4. **Objectives** — symmetric placement via `LevelConfig.PlaceObjectives` (or
   parameterized count).
5. **Determinism** — same `MapSpec` + seed ⇒ identical terrain and layout.
6. **Loggable** — header records enough to reproduce the opening state.

---

## Logging

Extend the game log header (schema bump when adding fields):

```jsonc
{
  "type": "header",
  "mapSeed": 123456789,
  "mapSpec": {
    "width": 24,
    "height": 24,
    "symmetry": "rot180",
    "relief": 0.5,
    "forestDensity": 0.45,
    "rockDensity": 0.2,
    "objectiveCount": 3,
    "forceTemplate": "standard"
  }
}
```

Fixed standard map: `mapSeed: null`, `mapSpec: { "source": "standard" }` (or omit
spec and keep today’s behaviour).

---

## Phased delivery

| Step | Scope | Depends on |
|------|--------|------------|
| **MG0** | `MapSpec` + `Generate(MapSpec)`; wire size+seed from UI | Core only | **Shipped** |
| **MG1** | Map Workshop / shell: preview, Reroll, Start; cartographic board | MG0 + BoardRenderer | **Shipped** |
| **Shell** | C&C dock (workshop → command); generator + topo render rewrite | MG1 | **Shipped** |
| **MG2** | Sliders: relief / forest / rock | MG1 | |
| **MG3** | Objective count, turn limit in workshop | MG1 | |
| **MG4** | Era / force templates (backlog B3) | Era packs | |
| **MG5** | Asymmetric / historical fixed maps (backlog B2) | Scenarios | |

**Recommended first ship:** MG0 + MG1 (preview + seed + size). That alone makes
“pre-game map generator” real — **and is now in the build** (plus the map shell).

---

## Non-goals (for this feature)

- In-match terrain editing
- Player-drawn maps (editor) — separate tool later
- Breaking symmetry for ranked skirmish in v1
- Server-side generation

---

## Relationship to other work

| Topic | Link |
|-------|------|
| Skirmish options | [`PRODUCT_ROADMAP.md`](PRODUCT_ROADMAP.md) Phase 3.1 |
| Cleaner map art (symbols, no tile fills) | **Shipped** — topo wash + batched contours + sparse annotations; C&C shell dock |
| Historical scenarios | Fixed `MapSpec` / baked layouts, not only procedural |
| Era packs | `forceTemplate` + terrain palettes later |

---

## Acceptance (MG1 done)

- From the main menu, player can open Map Workshop, see a generated board,
  press Reroll, change size, then Start Match on that exact map.
- Re-entering the same seed + size reproduces the preview and the match.
- Log header allows reproducing the opening terrain (`mapSpec` on schema v10).
- Board is paper + contours + map symbols (no per-tile color squares).

**Status: accepted / shipped.**
