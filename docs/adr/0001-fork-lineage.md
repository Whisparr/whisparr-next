# ADR-0001: Fork lineage and sync clocks

Date: 2026-11-24
Status: Accepted

## Context

`whisparr-next` is a Servarr-family fork in the Sonarr → Radarr → Whisparr chain.
The backend (`src/` and everything else) descends from Radarr. The frontend
(`frontend/`) was ported from Sonarr's completed React Query + zustand migration
(see `REDUX_MIGRATION.md`).

## Decision

- Backend follows `Radarr/Radarr:develop`.
- Frontend follows `Sonarr/Sonarr:develop`.
- Default branch and PR target is `develop` on `Whisparr/whisparr-next`.
- Upstream state lives in `.github/upstream/state.json`; `UPSTREAM_SYNC.md` is
  generated from it via `scripts/upstream-sync.mjs` (see `sync-upstream` skill).
- `Whisparr.Api.V3` is frozen for integrations; new endpoints go in
  `Whisparr.Api.V5`.

## Consequences

- Parser, localization, and Radarr-frontend upstreams are gated (see skill):
  upstream diffs in those areas do not apply cleanly to our scene grammar,
  sorted `en.json`, or Sonarr-based frontend.
- Sonarr `src/Sonarr.Api.V5/` commits have no counterpart here: V5-only commits
  are skipped, mixed commits take the non-V5 half.
- Cherry-pick trailers use `(cherry picked from commit radarr/radarr@<sha>)` and
  `(cherry picked from commit sonarr/sonarr@<sha>)`.
