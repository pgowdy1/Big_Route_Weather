# Tone Down Consensus Sensitivity & UI

**Branch:** `feature/tone-down-consensus-sensitivity`
**Scope:** Small full-stack tweak (modify existing)
**Layers:** Backend (ConsensusCalculator) + Frontend (ConsensusBadge component + parents)

## Problem

The consensus confidence indicator (added in PR #11) is too jumpy and too visually loud:
- One noisy factor (highest CV) demotes the whole rating to medium/low.
- CV thresholds (0.15 / 0.35) trip easily when means are small.
- The chip uses saturated red/amber/green and shows on every card even when consensus is high — distracting UI noise for a heads-up signal.

## Design choices (from planning Q&A)

1. **Sensitivity** — "all of the above":
   - Raise thresholds: `highMaxCv` 0.15 → 0.25, `mediumMaxCv` 0.35 → 0.50.
   - Average CV across active factors (not the single worst). Worst factor is still surfaced for context.
   - Absolute-spread floors before a factor contributes any CV: Wind ≥ 5 mph, Temp ≥ 5 °F, Precip ≥ 20 percentage points. Below the floor → factor contributes 0 CV.
2. **UI** — "Muted chip + smaller":
   - Replace saturated backgrounds with soft greys; tiny colored dot (level-coded) is the only color.
   - Smaller font/padding.
   - Drop the inline "disagree on X" text; move to `title`/`aria-label` on the chip.
3. **When shown** — "Only on low consensus":
   - Hide entirely when level is high or medium.
   - High/medium consensus = silent (no badge).

## Affected files

**Backend**
- `backend/RouteWeather.Core/Grading/ConsensusCalculator.cs` — threshold defaults, average-CV resolution, spread floors.
- `backend/RouteWeather.API/Program.cs` — verify defaults flow through (ConsensusCalculator is instantiated where?).
- `backend/RouteWeather.Core.Tests/Grading/ConsensusCalculatorTests.cs` — update assertions for new thresholds and averaging behavior.

**Frontend**
- `frontend/src/app/components/consensus-badge/consensus-badge.html` — drop "disagree on X" inline text; chip gets `title`.
- `frontend/src/app/components/consensus-badge/consensus-badge.scss` — muted styling.
- `frontend/src/app/components/consensus-badge/consensus-badge.ts` — `visible` computed: only render when level === 'low'.
- `frontend/src/app/components/consensus-badge/consensus-badge.spec.ts` — adjust assertions for new visibility rule + tooltip-based worst factor.
- `frontend/src/app/components/route-card/route-card.spec.ts` and `peak-detail.spec.ts` — only if fixtures break.

## Implementation steps

1. Backend: extend `ConsensusCalculator` with spread floors and average-CV resolution; bump default thresholds.
2. Backend: update `ConsensusCalculatorTests` for new behavior.
3. Backend: `dotnet test` (via absolute csproj path — API is likely running).
4. Frontend: hide badge except on low; replace styling; drop inline worst-factor text (move to title).
5. Frontend: update `consensus-badge.spec.ts` for new visibility/tooltip rules.
6. Frontend: `npm test`.

## Verification

- Backend tests pass with new defaults.
- Frontend tests pass — badge only renders on low.
- Builds clean (`dotnet build` on Core csproj, `ng build`).
- Manual: route cards no longer show high/medium chip; low chip uses muted style with tooltip.

## Complexity: solo

Single-developer scope. No agent team needed.
