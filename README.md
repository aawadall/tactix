# Tactix

A minimal turn-based tactics game built as an ML-training-data factory. The rules
engine is a pure C# library (`Assets/Scripts/Core`, assembly `Tactix.Core`, no
UnityEngine references) with serializable state, structured actions, an
authoritative legal-action function, and a JSON-lines game logger. A thin Unity
layer (`Assets/Scripts/Game`) renders it and handles input.

- Unity **6000.0.75f1 LTS**, standalone desktop target (Windows/Mac/Linux).
- v1: human-vs-human hotseat, human-vs-random-bot, and bot-vs-bot self-play.
  No ML/inference code yet — the point of v1 is validating the engine and
  generating log data.

## Running

Open the project in Unity 6000.0.75f1 and press Play (the scene is empty by
design — `Bootstrap` spawns the camera, board, and UI at runtime), or build:

```
Unity.exe -batchmode -quit -projectPath . -executeMethod Tactix.EditorTools.BuildTools.BuildWindows
```

Run tests (rules unit tests + full random self-play with log validation):

```
Unity.exe -batchmode -projectPath . -runTests -testPlatform EditMode -testResults TestResults/editmode.xml
```

Headless self-play data generation (runs N bot-vs-bot games, writes one JSONL
file each to `logs/` next to the exe, then quits):

```
Builds\Windows\Tactix.exe -batchmode -nographics -autoplay 100
```

The window is resizable and maximizable (F11 or Alt+Enter toggles fullscreen);
the board reframes itself on any resize.

In game: click a friendly unit to select it — the cyan region is everywhere it
can dash to and red rings mark enemies it can attack, both straight from the
rules engine. Click anywhere in the region to move there (a click outside it is
clamped to the furthest legal point on that heading), or a ringed enemy to
attack. Right-click deselects, End Turn passes. Clicking any unit shows its
telemetry (health, XP, position, elevation, damage, sight); **L** or the Legend
button opens the unit legend; **Esc** backs out of a game (or quits from the
menu); Quit buttons sit on the menu and win screens.

Units are drawn as NATO APP-6-style symbols (player-colored frame, company
echelon bar): infantry ⨯, mechanized ⨯+track oval, armor oval, artillery ●,
recon single slash. Relief is drawn as topographic contour lines (marching
squares over the elevation field) with spot heights on the summits; heavier
contours mark cliffs.

## Rules summary

- **Continuous space.** Unit positions are floating-point world coordinates,
  not grid cells. The terrain and elevation rasters are sampled underneath
  them: tile `(i,j)` is centred on `(i,j)` and covers `[i-0.5, i+0.5) x
  [j-0.5, j+0.5)`, so the playable area is `[-0.5, W-0.5] x [-0.5, H-0.5]`.
- **Movement is a straight-line dash**: a unit may move to *any* point within
  its move range whose connecting segment stays in bounds, never enters an
  impassable tile, and never crosses a cliff. The legal region is therefore
  star-shaped around the unit — see the architecture note below for why that
  shape matters.
- 24x24 board (hardcoded in `LevelConfig` only, authored as ASCII layers — the
  engine is board-size-agnostic). Terrain: open, forest (+1 defense to
  occupant, blocks line-of-sight), impassable. The relief was generated offline
  with fractal noise, symmetrized, and repaired for reachability, then baked in;
  the shipped map is fixed and 180°-rotationally symmetric with no runtime
  randomness.
- **Topographic layer**: every tile has an elevation (0–3), rendered as
  contour lines with spot heights on the summits:
  - *Movement*: crossing between tiles whose elevation differs by 2 or more is
    a cliff and is blocked (the standard map's slopes are all climbable;
    army-to-army reachability is test-enforced).
  - *Combat*: attacking from higher ground adds +1 damage.
  - *Line of sight*: the sight line runs from eye height (elevation + 1) to eye
    height; a crossed tile blocks iff its effective height — elevation, +1 for
    forest canopy, +3 for impassable walls — reaches the line over the
    crossing. Units on hills shoot over valley forests; hills block shots
    between valleys. On flat ground this reduces exactly to the old rule.
- 2 players × 12 units (3 Infantry, 3 Mechanized Infantry, 2 Armor, 3 Artillery,
  1 Recon). All distances are Euclidean world units (a tile is 1x1):

  | Type | Move | Range | Damage | HP | Sight | Radius | Needs LOS |
  |---|---|---|---|---|---|---|---|
  | Infantry | 3.0 | 1.2 | 2 | 5 | 4 | 0.35 | no |
  | Mech Infantry | 4.5 | 1.2 | 2 | 6 | 4 | 0.35 | no |
  | Armor | 4.0 | 1.5 | 4 | 8 | 3 | 0.40 | no |
  | Artillery | 2.0 | 5.0 | 3 | 3 | 3 | 0.35 | yes |
  | Recon | 6.0 | 1.0 | 1 | 3 | 8 | 0.30 | no |

  Units are solid bodies: two units must stay at least the sum of their radii
  apart, so a destination that would overlap someone is illegal.

  Sight is defined, displayed, and logged but has no gameplay effect yet (no
  fog of war in v1 — the field exists so fog can be added without a schema
  change). XP accrues +1 per attack and +2 more per kill; display/logging only
  for now.
- All ranges are Euclidean distance in world units; there is no grid metric and
  no notion of "adjacent tiles".
- Movement: any point within move range on a clear straight line; impassable
  tiles and cliffs block the line, and a destination overlapping another unit's
  body is illegal.
- Turn structure: the active player moves any subset of units (each at most
  once), then attacks with any units in range (each at most once). The first
  attack of a turn ends the movement phase for **all** units (`turnPhase`
  flips from `move` to `attack`). End turn is an explicit action.
- Line of sight: cast between the two unit positions; a tile whose interior the
  segment crosses blocks when its effective height reaches the sight line
  (see the topographic rules above). The shooter's and target's own tiles never
  block, corner-touches don't block, and the test is symmetric by construction.
- Damage = attacker power + (1 from higher ground) − (1 if defender stands in
  forest), floored at 0; no counterattacks. A unit at 0 HP is removed;
  eliminating all enemy units wins.

## Data schemas (schemaVersion 4)

Everything below is produced by `Tactix.Core` via Newtonsoft.Json and is the
contract for the future imitation-learning pipeline. **Schema stability matters
more than anything else here** — change nothing without bumping
`GameLogger.SchemaVersion`.

### GameState

```json
{
  "terrain": [[0,0,1,2,0,0,0,0], ...],
  "elevation": [[0,0,1,2,2,1,0,0], ...],
  "units": [
    {"id":0,"owner":0,"type":"infantry","x":6.0,"y":2.0,"hp":5,"xp":0,"hasMoved":false,"hasAttacked":false},
    {"id":9,"owner":0,"type":"artillery","x":9.5,"y":0.0,"hp":3,"xp":0,"hasMoved":false,"hasAttacked":false}
  ],
  "currentPlayer": 0,
  "turnPhase": "move",
  "turnNumber": 1,
  "winner": null
}
```

| Field | Meaning |
|---|---|
| `terrain` | rows of columns, `terrain[y][x]`; codes: `0` open, `1` forest, `2` impassable. Board size is implied by the array — never fixed. |
| `elevation` | rows of columns, same shape as `terrain`; whole levels (0–3 on the standard map). Drives cliffs, high-ground bonus, and 3D line of sight. |
| `units` | variable-length entity list; `x`/`y` are **floating-point world coordinates**; `type` is `"infantry"` \| `"mechInfantry"` \| `"armor"` \| `"artillery"` \| `"recon"`; `xp` is +1 per attack, +2 more per kill (no gameplay effect yet); ids are stable for the whole game; dead units are removed from the list. Per-type constants (range, radius, …) live in `UnitStats`, not in the state. |
| `currentPlayer` | `0` or `1` |
| `turnPhase` | `"move"` \| `"attack"` — first attack of a turn switches it; movement is only legal in `"move"`. |
| `turnNumber` | 1-based ply counter, +1 on every end-turn. |
| `winner` | `null` while in progress, else `0` or `1`. |

### Actions

Pointer-based (unit ids) with continuous targets, never a fixed board-position
enumeration, so the action space is board-size-agnostic. Discriminated by
`actionType`:

```json
{"actionType":"move","unitId":0,"targetX":7.35,"targetY":4.125}
{"actionType":"attack","unitId":2,"targetUnitId":6}
{"actionType":"endTurn"}
```

**The move action space is continuous; the attack space stays discrete.** That
split drives the whole rules API:

| Concern | Movement (continuous) | Attacks / end turn (discrete) |
|---|---|---|
| Authority | `Rules.IsLegalMoveTarget(state, unitId, x, y)` — exact predicate | `Rules.GetLegalAttacks(state, unitId)` — exhaustive list |
| For a policy | `Rules.ProjectMove(...)` clamps a raw `(x, y)` onto the legal region (constraint projection replaces hard masking) | hard action masking as before |
| For UI / sampling | `Rules.GetMoveRegion(...)` returns the reachable distance along 72 rays (the star-shaped region drawn in game); `Rules.SampleLegalMoves(...)` draws area-uniform samples from it | — |

`Rules.Apply` re-validates every action against the same predicate and throws on
anything illegal, so no illegal action is constructible outside the rules
engine. Note that `Rules.GetAllLegalActions` is *not* exhaustive any more: it
returns all attacks, `endTurn`, and a **sample** of legal moves per unit.
Everything it returns is legal, but a continuous space cannot be enumerated —
use the predicate or the projection when you need authority.

### Game logs (`logs/*.jsonl`)

One file per game — `logs/` sits next to the project root in the editor and
next to the executable in builds. One JSON object per line, in order:

1. **header** (first line):
   `{"type":"header","schemaVersion":4,"createdUtc":"<ISO-8601>","mode":"hotseat|vsBot|botVsBot","initialState":<GameState>}`
   Version history — v4 moved to continuous positions (float `x`/`y`, 24x24
   map, 12 units/side); v3 was the last grid-based schema (integer tile
   coordinates, 16x16, 8 units/side); v2 lacked `elevation`; v1 also lacked
   `xp` and used a two-type roster with `"ranged"`. **Grid-era logs (v1–v3) are
   not compatible with continuous-era models** — the coordinate semantics
   differ, so filter on `schemaVersion` before training.
2. **step** (one per applied action):
   `{"type":"step","stepIndex":<0-based>,"player":<actor>,"stateBefore":<GameState>,"action":<Action>,"stateAfter":<GameState>}`
   — steps chain: `stateAfter` of step *n* equals `stateBefore` of step *n+1*.
3. **result** (last line, exactly once):
   `{"type":"result","winner":0|1|null,"completed":true|false,"totalSteps":<n>}`
   — `completed:false` (with `winner:null`) marks a game abandoned mid-way
   (e.g. app closed); the logger writes it automatically on dispose.

Lines are flushed as written, so a crash loses at most the current line.

## Architecture notes for the next pass

- `Tactix.Core` compiles without Unity (asmdef has `noEngineReferences: true`;
  the only dependency is Newtonsoft.Json), so it can be reused server-side or
  next to ONNX inference as-is.
- `RandomBot` only plays actions the rules engine produced (sampled moves,
  enumerated attacks) — any future agent should do the same. Its moves carry an
  *advance bias* toward the nearest enemy: pure uniform sampling wanders
  indefinitely in continuous space, and the bias is what makes self-play
  terminate and produce useful logs. Set `advanceBias: 0` for pure random.
- A continuous policy head (Gaussian/SAC-style for the move target, categorical
  over units and attack targets) fits this API directly: sample a raw `(x, y)`,
  run it through `ProjectMove`, and log the projected action — the log's
  `targetX`/`targetY` are always already-legal points.
- Tests: `Assets/Tests/EditMode` — rules unit tests plus seeded random
  self-play that validates termination, legality, projection soundness, and the
  JSONL schema end-to-end.
