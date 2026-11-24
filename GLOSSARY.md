# Glossary

Single-context glossary for `whisparr-next`. Use these terms in issues, PRs, and refactors; avoid synonyms.

- **Movie**: A film entry in the library. Identified by TMDB id; dated by year (`[YYYY]`).
- **Scene**: An adult scene entry. Carries a full date, plus Performer and Studio links. Scenes are first-class, not episodes.
- **Performer**: A person credited on scenes. Has its own index, edit modal, tags, and defaults.
- **Studio**: A production studio credited on scenes. Has its own index and details views.
- **Collection**: A grouping of movies (Radarr lineage). Distinct from Studio.
- **Import List**: An external source that feeds candidate Movies/Scenes into the library.
- **Indexer / Download Client / Notification**: ThingiProvider types under `src/NzbDrone.Core/` (see `AGENTS.md`).
- **v3 API**: Frozen integration surface at `src/Whisparr.Api.V3/` (`/api/v3`). Do not extend.
- **v5 API**: Current surface (controllers under `src/Whisparr.Api.V5/`, served at `/api/v5` and `/docs`).
- **develop**: Default branch and PR target for this repo (`Whisparr/whisparr-next`).
