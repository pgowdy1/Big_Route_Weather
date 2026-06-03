# About Page

**Branch:** `feature/about-page`
**Scope:** Small, frontend-only
**Layer:** Angular frontend only — no backend, no data model

## Description

Add a dedicated `/about` route describing what the app is for, who it's geared toward, what weather sources it uses, and how to give feedback. Place a small "About" text link in the top-right of the shared hero header — present but unobtrusive.

## Decisions (from planning)

1. **Route, not modal.** `/about` is a real route under the existing shared hero.
2. **Link placement:** top-right of `app.html` hero, label `About`, muted styling.
3. **Voice:** first-person. Reads like the maker talking to climbers/dirtbags.
4. **Content sections:** Intro, "Who it's for", "What's coming," "Where the data comes from," Contact.
5. **Contact:** `mailto:pgowdy1@gmail.com` only.
6. **Sources detail:** concretely name NWS forecast + hourly + SNOTEL — accurate to what's wired up today.
7. **Hero stays on /about** unchanged (shared via `<router-outlet />`).

## Affected files

**New:**
- `frontend/src/app/pages/about/about.ts`
- `frontend/src/app/pages/about/about.html`
- `frontend/src/app/pages/about/about.scss`
- `frontend/src/app/pages/about/about.spec.ts`

**Modified:**
- `frontend/src/app/app.routes.ts` — register `/about`
- `frontend/src/app/app.html` — add About link in hero
- `frontend/src/app/app.scss` — style the link (top-right, muted)
- `frontend/src/app/app.spec.ts` — assert link renders (optional — covered by About spec already)

## Implementation steps

1. **Create the About page component.** Standalone, `ChangeDetectionStrategy.OnPush`, no service deps. Template renders five short sections in first-person voice, matching the dark palette (`#11202c` cards, `#e8eef3` text, muted `#8aa0b4` accents).
2. **Register the route** in `app.routes.ts` between `peak/:slug` and the wildcard.
3. **Add the About link** to the hero in `app.html`. Use a flex layout — H1 + tagline on the left, About link top-right.
4. **Style the link** in `app.scss` — small font, muted color (`#8aa0b4`), hover lifts to `#cfd8dc`, no underline by default.
5. **Spec for About** — renders heading, audience pills, sources list, and contact email link.

## Test plan

- `about.spec.ts`:
  - Renders the H2 page title
  - Renders the audience list (climbers, hikers, dirtbags, trail runners)
  - Renders the mailto link with the right email
  - Renders the data-source names (NWS, SNOTEL)
- `app.spec.ts` (extend): About link is present with `routerLink="/about"` and text "About"

## Verification commands

```
cd frontend
npx ng build 2>&1 | tail -5      # build clean
npm test                          # vitest, runs once
```

## Edge cases handled

- Unknown routes already redirect to `/` via the existing wildcard — no /about typo trap.
- Hero is shared — clicking the H1 from /about returns home (existing `routerLink="/"`).
- Mobile: hero becomes column-wrapped via flex `flex-wrap: wrap`.

## Complexity assessment

Solo build. Single component, no agents needed.
