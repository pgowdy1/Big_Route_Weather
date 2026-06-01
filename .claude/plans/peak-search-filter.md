# Peak Search Filter

**Branch:** `feature/peak-search-filter`
**Scope:** Small, frontend-only
**Complexity:** Solo build

## Description
Add a search input at the top of the home grid that filters visible peak cards by mountain name (case-insensitive substring match). Only affects the home route; peak detail page is untouched.

## Requirements
- Search input rendered inside `RouteGrid`, above the cards
- Filter routes by `mountain` field, case-insensitive substring
- Empty filtered result shows: `No peaks match "<query>".`
- Empty query (or whitespace-only) shows all peaks
- Search box is hidden while the initial load is in-flight and on error (existing status messages already replace the grid in those states)

## Affected Files
- `frontend/src/app/components/route-grid/route-grid.ts` — add `query` signal + `filtered` computed
- `frontend/src/app/components/route-grid/route-grid.html` — search input + filtered loop + empty state
- `frontend/src/app/components/route-grid/route-grid.scss` — search input styles
- `frontend/src/app/components/route-grid/route-grid.spec.ts` — filter + empty-state tests

## Implementation Steps
1. In `route-grid.ts`: add `query = signal('')`, add `filtered = computed(...)` that lowercases the query and filters `routes()` by `mountain`. Wire an `onSearch(value)` setter.
2. In `route-grid.html`: render `<input>` above the grid (only in the success branch). Use `[value]="query()"` + `(input)="onSearch($any($event.target).value)"`. Iterate `filtered()` instead of `routes()`. Add `@if (filtered().length === 0 && query().trim())` branch for the no-match message.
3. In `route-grid.scss`: style the input (full-width, dark theme consistent with hero).
4. In `route-grid.spec.ts`: add specs for "filters by mountain name", "case-insensitive match", "shows no-match message".

## Edge Cases
- Whitespace-only query: treat as empty → show all
- Multiple matches / single match / zero matches
- Mountain names with mixed case (existing data has them)

## Test Plan
- Existing two specs still pass
- New: typing "longs" filters to Longs Peak (assuming present in fixture data, otherwise build matching fixtures)
- New: query with no matches renders the empty-state message
- New: clearing the query restores all cards

## Verification Commands
```bash
cd frontend && npx ng build 2>&1 | tail -5
cd frontend && npm test 2>&1 | tail -20
```

## Out of Scope
- Backend filtering (client-side only — list is ~58 14ers, trivially fast)
- Searching route name, class, drivers
- Highlighting matches
- URL query param sync
