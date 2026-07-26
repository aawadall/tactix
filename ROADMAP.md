# Tactix roadmap — proposed features

Design notes for features discussed but **not yet implemented**. Each entry
records what the feature is, why it is worth doing, a concrete design sketch,
what it costs in schema and ML terms, and its rough size. Nothing here is
committed to; the sequencing at the bottom is a recommendation, not a plan of
record.

Current baseline: continuous space, log **schemaVersion 6**, 24x24 standard or
generated maps, 14 units per side, 60 tests. See `README.md` for the shipped
rules and data contract.

Two constraints apply to every entry below:

- **Schema stability is the highest-value property of this project.** Any change
  to logged fields bumps `GameLogger.SchemaVersion` and must be recorded in the
  README's version history, including whether older logs stay
  training-compatible.
- **The rules engine stays authoritative.** New mechanics belong in
  `Tactix.Core` as pure functions, with `Rules.Apply` re-validating; the Unity
  layer and any future policy only ever submit actions the engine produced or
  approved.

---

## 1. Artillery spotting (indirect fire)

**Status:** proposed. **Size:** small (~an hour). **Schema:** v7, no field changes
to state — only rule semantics, but logs are behaviourally incompatible, so bump.

### Problem

The `sight` stat exists, is displayed, and is logged, but has no gameplay effect
whatsoever. Recon carries sight 8 — by far the best on the board — and it buys
nothing. Meanwhile artillery requires its *own* line of sight to fire, which is
backwards for indirect fire: real guns shoot over terrain at coordinates someone
else is observing.

### Design

Replace artillery's self-LOS requirement with a **spotter** requirement:

```
A unit with RequiresLineOfSight may attack target T if:
    distance(unit, T) <= AttackRange            (unchanged)
    AND exists a friendly unit S (possibly the firing unit itself) such that
        distance(S, T) <= S.Stats.Sight
        AND LineOfSight.HasLineOfSight(state, S, T)
```

The firing unit qualifying as its own spotter keeps direct fire working when the
gun does have eyes on the target. Everything else — Euclidean range, damage,
forest defence, high ground — is unchanged.

Rename the stat concept from `RequiresLineOfSight` to `RequiresSpotter` for
honesty, and surface the spotter in the UI: when an artillery piece is selected,
draw a thin line to the unit spotting each target so the player can see *why* a
shot is available and what killing that scout would cost them.

### Consequences

- Recon becomes genuinely load-bearing: it is the eyes for every gun on the
  board, and killing the enemy's scout blinds their artillery.
- Artillery gets stronger (it can shell over ridges) but more fragile
  strategically (it depends on someone else surviving). That is the intended
  trade.
- Consider whether Sight should also gate *direct*-fire units. Recommendation:
  no — keep sight meaningful only for spotting until fog of war lands, otherwise
  two different visibility systems accumulate.

### ML notes

Cheap. The action space is unchanged and attacks stay enumerable, so masking is
unaffected. It does add a non-local dependency to the legality of an attack (a
third unit's position), which is worth knowing when designing the observation
encoding — a per-unit feature vector alone no longer determines legality.

---

## 2. Victory conditions beyond elimination

**Status:** proposed. **Size:** medium. **Schema:** v7+, adds state fields.

### Problem

Eliminating every enemy unit is the only way to win, which has two costs. For
play, it forces grinding attrition and long games. For learning, it is an
extremely sparse reward: one bit of signal at the end of a ~400-step game, with
nothing along the way to distinguish good play from bad. There is also still no
draw condition, so a stalemate cannot terminate.

### Design

Add **objectives** and a **turn limit** with a points tiebreak.

```jsonc
// GameState additions
"objectives": [
  {"id": 0, "x": 11.5, "y": 11.5, "radius": 1.5,
   "controlledBy": null,          // 0 | 1 | null while contested or empty
   "heldTurns": {"0": 3, "1": 0}} // consecutive turns each player has held it
],
"turnLimit": 60,                  // null for unlimited
"score": {"0": 4, "1": 1}
```

Control rule: at end of turn, an objective is controlled by the only player with
a unit inside its radius; contested (both present) or empty leaves the previous
controller in place. Scoring: +1 point per objective held at end of turn.

Victory, checked in order: all enemies eliminated → win; a player reaches the
score target → win; turn limit reached → higher score wins, equal score is a
draw. Objectives are placed symmetrically by `MapGenerator` (centre plus mirrored
flank pairs) so generated maps stay fair.

`winner` becomes `0 | 1 | null`, and the result line needs a reason so a draw is
distinguishable from an abandoned game:

```jsonc
{"type":"result","winner":null,"reason":"turnLimit|elimination|score|aborted",
 "completed":true,"score":{"0":7,"1":7},"totalSteps":412}
```

### Consequences

- Games get shorter and more decisive; the map's centre and flanks acquire
  meaning, which also makes the terrain generator's output matter more.
- The engine finally has a terminating condition that does not require one side
  to be wiped out.

### ML notes

This is the single biggest improvement to training signal on this list. Score
per turn is a dense, well-shaped reward that correlates with winning long before
the game ends, and the turn limit bounds episode length (useful for both
imitation and RL). It does mean `winner: null` becomes a legitimate outcome —
any training pipeline must handle draws rather than assuming a binary label.

---

## 3. Fog of war

**Status:** proposed. **Size:** large. **Schema:** v8+, structural change to logs.

### Problem

Both players see everything. The `sight` stat, forest concealment, and scouting
as an activity are all inert. This is the deepest realism gap remaining — and
the original v1 spec deliberately deferred it.

### Design

A unit is visible to a player if any of that player's units has line of sight to
it within their sight range. Forest should conceal: units inside a forest tile
are only visible at a reduced range (say 40% of the observer's sight), which
finally gives forests an offensive use rather than only a defensive bonus.

Derive observations rather than storing them:

```csharp
// Tactix.Core
public static class Observation
{
    // Pure function: the state as `player` may legally perceive it.
    public static GameState For(GameState truth, int player);
    public static bool IsVisible(GameState truth, int player, Unit unit);
}
```

`Observation.For` returns a state with enemy units the player cannot see removed.
Terrain and elevation stay fully visible — this is fog of war, not an unexplored
map, which suits a tactical engagement where both sides have the map.

**Logging must change shape.** Each step needs both the ground truth and the
acting player's observation:

```jsonc
{"type":"step","stepIndex":12,"player":0,
 "stateBefore":<truth>,"observationBefore":<what player 0 could see>,
 "action":<action>,"stateAfter":<truth>}
```

### Consequences

- Bots and any future policy must act on `observationBefore`, not the truth.
  `RandomBot` needs rewriting to sample from what it can see.
- Legality gets subtle: attacking an unseen enemy must be illegal, but the
  *rules engine* still needs the truth to resolve. Keep `Rules` operating on
  truth and filter candidate actions through visibility, so there is exactly one
  authority.

### ML notes

The reason to design this carefully rather than bolt it on: if logs contain only
ground truth, an imitation learner trains on information the agent will not have
at inference. It will silently learn to exploit unseen units, then collapse when
deployed. Logging the observation alongside the truth is what keeps the dataset
honest, and it costs roughly double the log size. Recurrent or history-stacked
policies become relevant here, since a single observation is no longer Markov.

**Do this after spotting (§1) and win conditions (§2)** — spotting establishes
the visibility machinery, and objectives give agents a reason to scout.

---

## 4. Prisoners of war

**Status:** proposed. **Size:** small-medium, but depends on §2. **Schema:** v7+.

### Problem

Units simply vanish at 0 HP. Surrender and capture are a real part of the domain
and currently unmodelled.

### Design

Instead of always destroying a unit at 0 HP, capture it when it is **isolated**:
at 0 HP, if the unit has no friendly unit within some radius (say 3.0) and an
enemy unit is adjacent, it becomes a prisoner rather than a casualty.

```jsonc
"units": [{"id": 7, ..., "status": "active|captured", "capturedBy": 1}]
```

Captured units are removed from play but recorded, and are worth victory points
to the captor. Optionally they can be **liberated** if a friendly unit reaches
the capture site before the game ends — which gives cavalry-style rescue raids a
reason to exist.

### Consequences

- Encircling becomes tactically distinct from simply killing, which rewards
  manoeuvre over attrition.
- Only meaningful once there is a scoring system, since otherwise a captured
  unit and a dead unit are identical in effect.

### ML notes

Small state addition. The interesting part is that it creates a second axis of
"winning" a fight, which makes the value function less directly tied to HP
totals. Sequence after §2.

---

## 5. Combat uncertainty ("injecting errors")

**Status:** proposed, lowest priority. **Size:** medium. **Schema:** v7+, and
changes the determinism guarantee.

> Note: this entry assumes "inject errors" means *combat uncertainty* — friction,
> miss chance, dispersion. If the intent was fault injection to test the engine,
> that is a different and much cheaper piece of work: property-based tests that
> throw malformed states and adversarial actions at `Rules.Apply` and assert it
> rejects them cleanly. Worth doing regardless.

### Design

Introduce a `Ruleset` config carried in the log header, **defaulting to fully
deterministic**, with opt-in uncertainty:

- **Artillery dispersion** — the most physically justified. An indirect shot
  lands at an offset drawn from a distribution around the aim point; damage falls
  off with miss distance. Pairs naturally with §1.
- **Hit variance** — damage drawn from a small range instead of a fixed number.
- **Suppression instead of damage** — a suppressed unit loses part of its next
  turn. Adds a state field and a genuinely different tactical texture.

### The determinism requirement

`Rules.Apply(state, action)` is currently a pure function, and a great deal of
the project's testability rests on that. If outcomes become stochastic, the RNG
must be explicit and logged, not ambient:

```jsonc
{"type":"header", "ruleset": {"dispersion": true, "hitVariance": false},
 "rngSeed": 987654321}
{"type":"step", ..., "rngDraws": [0.4172, 0.9931]}
```

Either thread an RNG through `Apply` and log every draw, or log the resolved
outcome and treat replay as verification rather than re-simulation. The first is
stricter and preferable.

### ML notes

Stochastic transitions mean the model must learn a distribution over outcomes
rather than a mapping, which materially increases the data required, and it adds
variance to any evaluation — two agents of equal strength will trade wins more
often, so more games are needed to distinguish them. **Keep the deterministic
ruleset as the default for the first training pass**, and treat uncertainty as a
later robustness experiment rather than a launch feature.

---

## 6. A 3D view

**Status:** proposed, and the recommendation is **don't** — at least not as a
full 3D renderer. **Size:** large. **Schema:** none; rendering is entirely
downstream of the game state.

### The case for it

Elevation is the one thing the current presentation asks you to *read* rather
than see. Contour lines are honest and precise, but they take a moment to
interpret: a player has to trace the isolines and the spot heights to work out
that a ridge blocks their artillery. A tilted 3D view would make relief and
sight lines immediately obvious.

### The case against it

- **The symbology is 2D by nature.** NATO APP-6 symbols are map notation. Tilted,
  extruded, or billboarded in a perspective view they stop reading as a military
  map and start reading as an unfinished 3D game.
- **Occlusion hurts a tactics game.** The moment terrain has height in screen
  space, hills hide units behind them. Every 3D tactics game then spends effort
  fighting its own camera — transparency on intervening geometry, unit
  silhouettes through terrain, free camera rotation, edge-scrolling. That is a
  large amount of work whose main purpose is to undo a problem the top-down view
  does not have.
- **It contributes nothing to the actual goal.** The state is coordinates and the
  training data is JSON; the renderer is not part of the ML pipeline. Every hour
  spent on a 3D camera is an hour not spent on spotting, objectives, or fog.
- **It needs an art pipeline.** Everything currently drawn is procedural, with no
  asset files at all. Models, materials, and lighting would end that property.

### The middle grounds, if elevation legibility is the real problem

Ordered cheapest first:

1. **Hillshade under the contours** — shade the terrain by the slope's angle to a
   fixed light direction. One pass over the elevation raster, no new systems, and
   relief becomes readable at a glance. Note this is *not* the flat per-tile
   elevation tinting that was tried and rejected earlier; hillshade responds to
   slope direction, which is what makes it read as shape rather than as a legend.
2. **A tilt toggle** — keep the game top-down, but let a key tilt the orthographic
   camera 30-40° and extrude the terrain into prisms by elevation, purely as a
   look-around view. No gameplay in that mode, so occlusion never matters.
3. **Elevation-offset rendering** — draw units and their tiles offset vertically
   by a few pixels per elevation level, with a drop shadow. Cheap fake depth that
   keeps the map flat and unambiguous.

**Recommendation:** option 1, and only if players actually report that reading
the relief is hard. Full 3D is a presentation rewrite in exchange for something
the contour lines already communicate correctly.

---

## Suggested sequence

| # | Feature | Why here | Size |
|---|---|---|---|
| 1 | Artillery spotting | Small, self-contained; immediately makes `sight` and Recon matter; lays the visibility groundwork for fog | S |
| 2 | Victory conditions | Biggest improvement to training signal; bounds game length; closes the missing-draw gap | M |
| 3 | Fog of war | Deepest realism gain, but needs observation logging designed up front — and objectives to give scouting a purpose | L |
| 4 | POW capture | Only meaningful once scoring exists | S–M |
| 5 | Combat uncertainty | Sacrifices determinism; keep behind a flag until a deterministic baseline model exists | M |

Independently of the above, the **engine fault-injection tests** mentioned in §5
are cheap and would harden the authority guarantee that everything else rests on.
