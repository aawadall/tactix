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

In game: click a friendly unit to select it (cyan = legal moves, red = legal
attack targets, both computed by the rules engine), click a highlighted tile to
move or a highlighted enemy to attack, End Turn button to pass. Right-click
deselects.

## Rules summary

- 8x8 board (hardcoded in `LevelConfig` only — the engine is board-size-agnostic).
  Terrain: open, forest (+1 defense to occupant, blocks line-of-sight),
  impassable. The layout is fixed and 180°-rotationally symmetric.
- 2 players × (2 Infantry + 2 Ranged).
  - Infantry: move 2, range 1, power 2, HP 5.
  - Ranged: move 1, range 3 (needs line-of-sight), power 3, HP 3.
- **Diagonals count**: movement is 8-directional (each step cost 1), ranges are
  Chebyshev distance, "adjacent" = 8 neighbors.
- Movement: BFS up to move range; impassable tiles and enemy units block paths;
  friendly units can be passed through but not ended on.
- Turn structure: the active player moves any subset of units (each at most
  once), then attacks with any units in range (each at most once). The first
  attack of a turn ends the movement phase for **all** units (`turnPhase`
  flips from `move` to `attack`). End turn is an explicit action.
- Line of sight: blocked by forest/impassable tiles whose interior is crossed by
  the segment between tile centers; endpoints excluded (a target standing in a
  forest is visible); corner-touches don't block. Symmetric by construction.
- Damage = attacker power − (1 if defender stands in forest), no counterattacks.
  A unit at 0 HP is removed; eliminating all enemy units wins.

## Data schemas (schemaVersion 1)

Everything below is produced by `Tactix.Core` via Newtonsoft.Json and is the
contract for the future imitation-learning pipeline. **Schema stability matters
more than anything else here** — change nothing without bumping
`GameLogger.SchemaVersion`.

### GameState

```json
{
  "terrain": [[0,0,1,2,0,0,0,0], ...],
  "units": [
    {"id":0,"owner":0,"type":"infantry","x":3,"y":1,"hp":5,"hasMoved":false,"hasAttacked":false},
    {"id":2,"owner":0,"type":"ranged","x":2,"y":0,"hp":3,"hasMoved":false,"hasAttacked":false}
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
| `units` | variable-length entity list; `type` is `"infantry"` \| `"ranged"`; ids are stable for the whole game; dead units are removed from the list. |
| `currentPlayer` | `0` or `1` |
| `turnPhase` | `"move"` \| `"attack"` — first attack of a turn switches it; movement is only legal in `"move"`. |
| `turnNumber` | 1-based ply counter, +1 on every end-turn. |
| `winner` | `null` while in progress, else `0` or `1`. |

### Actions

Pointer-based (unit ids / coordinates), never a fixed board-position
enumeration, so the action space is board-size-agnostic. Discriminated by
`actionType`:

```json
{"actionType":"move","unitId":0,"targetX":3,"targetY":2}
{"actionType":"attack","unitId":2,"targetUnitId":6}
{"actionType":"endTurn"}
```

Legality is decided solely by `Rules.GetLegalMoves` / `Rules.GetLegalAttacks` /
`Rules.GetAllLegalActions` (pure functions of state — the future hard
action-mask source), and `Rules.Apply` re-validates against the same functions
and throws on anything illegal. `GetAllLegalActions` includes `endTurn`, which
is always legal while the game runs.

### Game logs (`logs/*.jsonl`)

One file per game — `logs/` sits next to the project root in the editor and
next to the executable in builds. One JSON object per line, in order:

1. **header** (first line):
   `{"type":"header","schemaVersion":1,"createdUtc":"<ISO-8601>","mode":"hotseat|vsBot|botVsBot","initialState":<GameState>}`
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
- `RandomBot` only samples from `GetAllLegalActions` — any future agent should
  do the same, which is exactly the hard-masking contract.
- Tests: `Assets/Tests/EditMode` — rules unit tests plus seeded random
  self-play that validates termination, legality, and the JSONL schema
  end-to-end.
