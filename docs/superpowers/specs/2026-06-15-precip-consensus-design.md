# Precipitation Consensus: Confidence-Weighted Multi-Source Resolution

**Date:** 2026-06-15
**Branch:** `feature/precip-consensus` (to be created off `dev`)
**Status:** Approved

## Problem

The app routinely shows ugly precipitation grades for Colorado peaks on days when
mainstream forecasts ("online") show little or no precip. The discrepancy is
manufactured by the aggregation and cap logic, not by bad source data. Live data
for Longs Peak on 2026-06-15 illustrates mild inputs (GFS 4% max PoP, ECMWF 0.1",
ICON 0", NWS ~23% max over 48h) — yet the pipeline can still turn a single model's
spike into a grade-capping headline.

Four mechanisms combine, in order of impact:

1. **PoP is collapsed by `MAX` at every stage.** Each source's snapshot PoP is the
   max over 48h (`NwsGridpointParser:64`); window grades take the max over the
   window hours of the blended series (`WindowGradeCalculator:51`). A single worst
   hour defines the entire window. In Colorado summer, nearly every window contains
   one isolated afternoon-convection hour, which condemns the whole grade.
2. **The cap curve is punishing for ordinary convective PoP** (`PrecipitationFactor.Cap`):
   >30% caps at B, >50% at C, >70% at D. A garden-variety "scattered PM
   thunderstorms, 40%" day — which an alpine climber reads as "summit by noon" —
   gets hard-capped at B.
3. **The probability "consensus" barely exists.** Only 2 of 5 sources report PoP
   (NWS and OpenMeteo-GFS). ECMWF, ICON, and HRRR (the highest-weighted source at
   1.2x, and the best convective model) force PoP to 0 and contribute nothing to
   the probability vote. Open-Meteo only exposes `precipitation_probability` for
   GFS, so this is a data-availability wall, not just a code choice. The probability
   that drives the cap is effectively `mean(NWS, GFS)`.
4. **Probability and amount are decoupled.** A source can show real QPF while its
   PoP is pinned at 0 (ECMWF today: 0.1" but 0% PoP), and GFS can show high PoP
   with ~0" amount. The cap reads probability; the score blends amount; they
   disagree.

The thread tying these together: the app leans on a `MAX`-collapsed probability
that only two sources vote on, while the one field all five sources report —
amount/QPF — is underused.

## Decisions (from brainstorming)

- **Philosophy: confidence-weighted.** Precip should move the grade only to the
  degree that sources corroborate it. Agreement amplifies the penalty;
  disagreement discounts it.
- **Lone-spike rule: discount toward consensus.** A 1-of-5 signal is treated as
  model noise and applies minimal penalty; only corroborated precip moves the grade
  meaningfully. Accepted tradeoff: a genuine isolated storm that only HRRR catches
  will under-warn — mitigated by a muted low-confidence indicator (UI follow-on).
- **Construction: agreement-as-probability (Approach A).** Build one unified
  consensus probability per hour from votes by all five sources, reusing all
  downstream grading machinery.
- **NWS is the strongest precip signal** and gets a precip-specific vote weight of
  **1.75** (above 1, below a veto) so it can bend the consensus without overriding
  four agreeing models.

## Goals

- Replace the thin 2-source `MAX`-PoP with a genuine multi-source, confidence-
  weighted consensus probability that all five sources contribute to.
- An uncorroborated single-model spike no longer caps the grade; corroborated
  precip still does.
- Smallest viable change: reuse the existing cap curve, score, window aggregation,
  and consensus report — feed them a trustworthy number.
- The NWS precip vote weight is a one-line tuning knob in config.

## Non-Goals

- No change to upstream fetching (no new fields, no new requests).
- No change to the cap/score thresholds in `PrecipitationFactor`. They stay as-is;
  the consensus change removes the pressure to touch them. Tunable later.
- No change to the amount-consensus path — it is already a value-presence mean and
  already discounts toward consensus correctly.
- The muted low-confidence UI indicator is a **follow-on** after the backend signal
  is verified, not part of this change.
- No per-source meteorological credibility weighting beyond the single NWS knob
  (the "source-aware credibility" option was considered and not chosen).

## Design

### 1. Per-hour consensus vote (RouteWeather.Core/Grading)

Each source casts a per-hour precip **vote** in [0,1]. The two probability sources
vote with their PoP; the three amount-only models vote via a soft ramp on their
hourly QPF:

```
vote(source, hour) =
    PoP / 100                  if the source reports probability (NWS, GFS)
    ramp(QPF_in_per_hr)        if the source reports only amount (ECMWF, ICON, HRRR)

ramp(q) = clamp01( (q - LO) / (HI - LO) )    // LO = 0.005"/hr, HI = 0.05"/hr

consensusPoP(hour) = round( 100 * Σ(vote_i * weight_i) / Σ(weight_i) )
                     // over sources present that hour
```

- `LO`/`HI` are named constants (starting 0.005"/hr and 0.05"/hr): 0 below trace,
  1 by ~0.05"/hr, linear between. This is the standard "poor-man's ensemble"
  conversion of a deterministic model's precip into a probability vote.
- `weight_i` is the source's existing weight (GFS/ECMWF/ICON 1.0, HRRR 1.2),
  **except NWS uses its precip-specific vote weight (1.75)** — see Section 3.
- `consensusPoP(hour)` **replaces** `PrecipitationProbabilityPct` in the blended
  hourly series. Everything downstream (window max, cap curve, score, headline,
  CV/consensus report) is unchanged and simply consumes a number that finally
  reflects all five sources.

This change is contained almost entirely in `ConsensusCalculator`: the per-hour
vote replaces the PoP-only mean at `BlendHourly` (currently line 95). The
QPF→vote ramp is a small, independently testable helper (e.g. a `PrecipVote`
static, or a method on a new `PrecipConsensus` class).

### 2. Headline consistency cleanup

Derive the blended snapshot's headline `PrecipitationProbabilityPct` from the
blended hourly series (max over hours), the way gust/CAPE/amount already are (the
code comment at `ConsensusCalculator:54-55` already wants this). This makes the
snapshot headline and the window grades agree, and ensures the headline reflects
the new consensus vote rather than a weighted mean of per-source 48h maxes.

### 3. NWS precip vote weight

NWS is the most skillful single source for convective probability (human
forecasters fold in SPC guidance), so it gets a precip-specific vote weight of
**1.75** — distinct from its global source weight (1.0), which continues to govern
wind/temperature blending. The weight lives in config alongside the existing source
weights so it can be tuned empirically without a code change (e.g. a
`Precipitation:NwsVoteWeight` knob or equivalent).

Effect on the disagreement cases (others at source weights; total weight 5.95):

| Disagreement case | NWS @ 1.0 | NWS @ 1.75 |
|---|---|---|
| NWS dry, 4 models ~50% | 40% | **35%** — damps a real 4-model agreement, no veto |
| NWS 50%, models dry | 10% | **15%** — louder lone voice, still discounted (no cap) |
| GFS lone 55% spike, NWS 15% (the bug) | 13% | **14%** — core fix preserved, spike still diluted |

### 4. Worked example — the bug this fixes

A peak where GFS over-forecasts an afternoon storm (its known terrain failure
mode): GFS 55% PoP, NWS a calm 15%, ECMWF/ICON/HRRR all dry (0").

| | Today | This design (NWS @ 1.75) |
|---|---|---|
| Hour's consensus PoP | mean(NWS 15, GFS 55) = **35%** | (0.15·1.75 + 0.55·1.0)/5.95 = **14%** |
| Grade cap (>30% → B) | **Capped at B** | No cap |
| Precip score | 56/100 | 83/100 |

GFS's lone spike is diluted from a grade-capping 35% to a benign 14% because the
other sources don't corroborate it. When models genuinely agree (4–5 wet), the
same formula climbs to the real probability and the grade drops as it should.

## Behavior changes to verify

1. **Lone NWS PoP gets discounted.** NWS 40% with every model dry →
   `0.40·1.75/5.95 ≈ 12%`. This is the chosen philosophy, but NWS is the case most
   likely to under-warn. The NWS vote weight is the lever; revisit empirically.
2. **Single-source fallback.** When only NWS is present (Open-Meteo down), there is
   no corroboration possible, so NWS's PoP is used at face value (falls out of the
   weighted mean over one source). Pinned with a test so a refactor can't
   accidentally damp a lone available source toward zero.
3. **The >30% cap becomes far less twitchy automatically** once noise no longer
   inflates PoP — which is why the cap curve is left untouched.

## UI follow-on (separate change)

When grade-driving precip comes from low agreement, surface a **muted
"low-confidence precip" indicator**, consistent with the app's rule that heads-up
indicators are silent on OK and muted (not alarming) when actionable. Carry an
agreement figure (fraction of source-weight that voted wet) for the grade-driving
hour on the consensus/report path the UI already consumes; render a quiet flag only
when precip is present and low-agreement. This is the safety valve for the
HRRR-catches-a-storm tradeoff. Scoped after the backend signal is verified.

## Testing

- **Vote helper:** ramp is 0 below `LO`, 1 above `HI`, linear between; PoP passes
  through for NWS/GFS.
- **Regression test encoding the bug:** the GFS-lone-spike scenario (Section 4)
  must produce consensus ≈14% and **no cap**.
- **Corroborated storm:** 4–5 wet sources → high consensus PoP → cap fires.
- **Lone NWS PoP** (behavior 1) and **single-source fallback** (behavior 2) each
  pinned with a test.
- **NWS vote weight:** the two disagreement cases in Section 3 produce the tabled
  consensus values at 1.75.
- **Mild day** (today's Longs-Peak-style inputs) → good precip grade.
- Existing window-aggregation and consensus-report tests still pass.

## Files (anticipated)

- `RouteWeather.Core/Grading/ConsensusCalculator.cs` — per-hour vote consensus;
  headline-PoP cleanup.
- New small helper for the QPF→vote ramp (e.g. `PrecipConsensus`/`PrecipVote`).
- Config: NWS precip vote weight knob (appsettings + binding).
- Tests under the Core test project.

## Scope boundary

In scope: the per-hour vote consensus in `ConsensusCalculator`, the QPF→vote ramp
helper, the headline-PoP consistency cleanup, the tunable NWS vote weight, and
tests. Follow-on: the muted UI indicator. Not touched: upstream fetching, cap/score
thresholds, amount blending.
