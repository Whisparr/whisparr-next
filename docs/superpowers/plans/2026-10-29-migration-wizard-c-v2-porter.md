# Migration wizard G-c: the v2 porter, implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** On a v2 install, "Back up & migrate" builds a fresh v5 DB, ports every v2 setting the spec lists, snapshots the v2 library into staging tables for phase 2, and restarts into v5 with the migration state at `Phase2Pending`.

**Architecture:**
- **Fresh DB:** `V5DatabaseBuilder` gains `CreateEmpty`, which builds and migrates an empty `whisparr5.db.partial` or an empty pair of Postgres DBs. The existing safety rules (sidecar records, refusal on legacy names, empty-or-missing targets) apply unchanged.
- **Porter:** `V2Porter` opens the legacy DB read-only through `LegacyConnections` and writes into the new v5 DB through raw Dapper SQL, inside one v5 transaction. It is split into one class per area (copy-as-is tables, config and naming, quality, providers, library snapshot). Each class appends what it couldn't carry over to a shared report list, which is written to a new `LegacyMigrationReportItems` table.
- **Pure maps:** `V2QualityMap` (quality IDs, quality source, language and indexer-flag values) and `V2TokenMap` (naming tokens) hold every v2 → v5 value translation. They have no DB access, so they are tested on their own.
- **Wizard:** `SetupWizardService` runs the v2 path through two new steps, `PortSettings` and `SnapshotLibrary`. Phase 2 itself (consuming the staging tables) is G-d.

**Tech Stack:** C#/.NET 10, Dapper, System.Data.SQLite, Npgsql, FluentMigrator, System.Text.Json (`JsonNode`), NUnit/Moq/FluentAssertions.

**Spec:** `docs/superpowers/specs/2026-10-24-migration-wizard-design.md` (Building the v5 DB, by source → v2; Data disposition, v2 column; Resume; Migration report).

## Scope

**In scope:**
- The v2 phase 1 for both scene sources (TPDB and StashDB). Phase 1 is the same for both; only phase 2 differs.
- SQLite and Postgres legacy DBs.
- The report rows the porter produces, stored in the DB.

**Out of scope, owned by G-d:**
- Phase 2, which reads the staging tables.
- The automation pause, the report page and the health notice.
- The Eros scene-source change. It keeps returning `SetupNotAvailableYet`.

**Until G-d lands,** a migrated v2 install boots with an empty library and `Phase2Pending`, the same way G-b left detached Eros movies.

## Recon facts the plan rests on

These were measured on 2026-10-29 against a fresh v2 DB at schema 221, produced by running v2 `develop` (`c55fdc66a`).

- **Fixtures:** the dumps are in the session scratchpad. Task 1 commits them.
  - SQLite `.dump`: `<scratchpad>/v2-fresh-221.sql`
  - Postgres `pg_dump --inserts --no-owner --no-privileges --no-comments`: `<scratchpad>/v2-fresh-221.pg.sql`
- **Default rows:** a fresh v2 DB holds default rows in `QualityProfiles` (7), `QualityDefinitions` (23), `Metadata` (4) and `DelayProfiles` (1). It has **no** `NamingConfig` row and **no** `Config` rows.
- **Column differences, v2 → v5:**
  - `NamingConfig`:
    - v2 `StandardEpisodeFormat` → v5 `StandardSceneFormat`; `SeriesFolderFormat` → `SceneFolderFormat`.
    - v2 has `MultiEpisodeStyle`, `RenameEpisodes`, `JavEpisodeFormat` and `CustomColonReplacementFormat`, which v5 lacks.
    - v5 adds `StandardMovieFormat`, `MovieFolderFormat`, `RenameMovies`, `RenameScenes`, `SceneImportFolderFormat`, `MaxFolderPathLength` and `MaxFilePathLength`.
    - v2 defaults: `StandardEpisodeFormat = "{Site Title} - {Release-Date} - {Episode Title} [{Quality Full}]"`, `SeriesFolderFormat = "{Site Title}"`, `ColonReplacementFormat = Smart (4)`.
  - **Quality sizes:** `QualityDefinitions` has no size columns in v2. Sizes live per item in `QualityProfiles.Items` JSON (`minSize`, `maxSize`, `preferredSize`). v5 keeps them on `QualityDefinitions`.
  - `QualityProfiles`: v5 adds `Language` and `Fallback`.
  - `ReleaseProfiles`: v2 `IndexerId` (int) → v5 `IndexerIds` (JSON array). v2-only: `ExcludedTags`, `AirDateRestriction`, `AirDateGracePeriod`.
  - `Indexers`: v2-only `SeasonSearchMaximumSingleEpisodeAge`.
  - `Notifications`:

    | v2 | v5 |
    |---|---|
    | `OnSeriesDelete` | `OnMovieDelete` |
    | `OnEpisodeFileDelete` | `OnMovieFileDelete` |
    | `OnEpisodeFileDeleteForUpgrade` | `OnMovieFileDeleteForUpgrade` |
    | `OnSeriesAdd` | `OnMovieAdded` |
    | `OnImportComplete` | none |

  - **Identical columns:** `Config`, `CustomFormats`, `AutoTagging`, `Metadata`, `DownloadClients`, `Tags`, `RootFolders`, `Users`, `DelayProfiles` and `RemotePathMappings`.
- **Quality IDs:** v2 → v5 is `13→20` (Bluray-480p), `23→21` (Bluray-576p), `20→30` (Remux-1080p), `21→31` (Remux-2160p) and `22→32` (VR). Every other ID is unchanged.
- **`QualitySource` values** (custom-format `SourceSpecification.value`), v2 → v5: `Unknown 0→0`, `Television 1→5`, `TelevisionRaw 2→6`, `Web 3→1`, `WebRip 4→2`, `DVD 5→3`, `Bluray 6→7`, `VR 7→10`, `BlurayRaw 8→8`.
- **Language IDs** differ for 19 languages. Map them by name, using the `Language` lists in both trees. Azerbaijani (47), Uzbek (48) and Malay (49) don't exist in v5.
- **`IndexerFlags`**, v2 → v5: `1→1`, `2→2`, `4→4`, `8 (Internal)→32`, `16 (Scene)→128`, `32 (Freeleech75)→256`, `64 (Freeleech25)→512`, `128 (Nuked)→2048`. `256 (Subtitles)` has no v5 equivalent.
- **Provider settings JSON** (camelCase property names):
  - **Download clients:**
    - `tvCategory`→`movieCategory`
    - `tvImportedCategory`→`movieImportedCategory`
    - `tvDirectory`→`movieDirectory`
    - `recentTvPriority`→`recentMoviePriority`
    - `olderTvPriority`→`olderMoviePriority`
    - Flood only: `startOnAdd` (bool) → `addPaused = !startOnAdd`.
  - **Implementations missing in v5:**
    - download clients `RQBit` and `Tribler`;
    - indexer `Fanzub`;
    - metadata `PlexMetadata`.
  - **Indexers:** Newznab and Torznab drop `seriesNameSource`, `searchSiteTitleOnly`, `searchTitleOnly`, `searchExternalIdOnly` and `dateSearchFormat`.
  - **Notifications:**
    - Email: `requireEncryption` (bool) → `useEncryption` (`Always = 1` when true, `Preferred = 0` when false).
    - Gotify: `includeSeriesPoster` → `includeMoviePoster`.
    - Telegram drops `linkPreview`.
  - **Metadata:** Xbmc, Roksbox and Wdtv use `episodeMetadata`, `episodeImages`, `seriesMetadataUrl` and other series/season keys in v2; v5 uses `movieMetadata`, `movieImages` and `movieMetadataURL`.
- **Spec JSON shape:** custom-format and auto-tagging specifications are stored as `[{"type": "<ClassName>", "body": {...}}]`. This comes from `Datastore/Converters/CustomFormatSpecificationConverter.cs`. The auto-tagging converter is the same shape; confirm it in `TableMapping`.
- **Auto-tagging specs:**
  - v2 has `Network` and `Status`, which v5 lacks. `Status` in v5 is `MovieStatusType`, which means something different.
  - `Genre`, `Monitored`, `OriginalLanguage`, `QualityProfile` and `RootFolder` exist in both.
- **Config keys (DB `Config` table, lowercase):**
  - v2 `autounmonitorpreviouslydownloadedepisodes` → `autounmonitorpreviouslydownloadedmovies`.
  - `createemptyseriesfolders` → `createemptymoviefolders`.
  - v2-only, with no v5 reader: `episodetitlerequired` and `userrejectedextensions`.
  - Every other v2 key has the same name in v5.
- **`config.xml` keys:** `Config5Seeder.V2KeyRenames` is empty, and that's correct (G-b).
- **Ranks and defaults:**
  - v5 seeds `QualityProfileQualityRanks` for every profile without ranks on `ApplicationStartedEvent` (`QualityProfileService.Handle`).
  - It inserts missing `QualityDefinitions` and default `Metadata` definitions on start.
  - So the porter writes neither ranks nor missing definitions.
- **Passwords:** v2 password hashes already verify in v5 (`PasswordHasher`, G-b), so `Users` copies as-is.
- **v2 library columns:**
  - `Series`: `Id`, `TvdbId` (numeric TPDB site ID), `Title`, `Path`, `Monitored`, `MonitorNewItems` (`All = 0`, `None = 1`), `QualityProfileId`, `Tags`, `SeriesType`, `Added`.
  - `Episodes`: `Id`, `SeriesId`, `TvdbId` (numeric TPDB scene ID), `ExternalId` (string), `Title`, `AirDate`, `Monitored`, `EpisodeFileId`.
  - `EpisodeFiles`: `Id`, `SeriesId`, `RelativePath`, `Size`, `Quality` (JSON), `Languages` (JSON array of language IDs), `ReleaseGroup`, `SceneName`, `OriginalFilePath`, `MediaInfo`, `IndexerFlags`, `DateAdded`.

## Global Constraints

- **Edits:** use the Edit and Write tools only. Never sed, shell redirection or chained Bash. Run one plain command per Bash call, with absolute paths. Call allowlisted scripts directly by path, never with a `bash` prefix.
- **Commits:** on `develop` in `~/Git/whisparr-next`. Never push.
- **No AI attribution.** Use the plz12345 handle and `~/` paths.
- **Commit prefixes:** `New:`, `Fix:` or `Chore:`.
- **Legacy files and DBs are opened read-only, every time.** The legacy DB, `config.xml` and the legacy Postgres DBs are never written to (spec anti-goal).
- **No step touches media files.**
- **v5-only DB migrations are numbered ≥ 305.** This plan uses 306.
- **IDs:** tables copied "as-is" keep their IDs (spec), and so do `QualityProfiles`, `CustomFormats`, `Indexers`, `DownloadClients` and `Notifications`, so that cross-references (`Tags`, `FormatItems`, `IndexerIds`, `DownloadClientId`) hold.
- **Every table or setting left behind or dropped gets a report row** (spec).
- **Tests:**
  - NUnit `*Fixture`, ≥ 80% coverage on new code.
  - Run each test project on its own (Core, Common, Api, Host, Mono). A solution-wide `dotnet test` reports false log-capture failures.
  - Postgres fixtures run only when `Whisparr__Postgres__Host` is set. Use a throwaway `postgres:16` container on a free port from 55440 up, never the Skyhook stack's DB, and stop it afterwards.
- **Waiting on processes:** never poll with `pgrep -f <pattern>` loops, because the pattern matches the loop itself. Use `until <condition>; do sleep 1; done` against a log line or a port.
- **Frontend:** strings go in `src/NzbDrone.Core/Localization/Core/en.json`, in alphabetical order.
- **The V3 API is frozen.**

## Review Focus

1. **Naming formats with separators, case and modifiers.** The v2 default contains `{Release-Date}`. Users also write `{site.title}`, `{Site_Title}`, `{SITE TITLE}`, `{Episode Title:30}` and `{[Quality Full]}`. Each must map with its separator, case style, truncation and prefix/suffix intact. Dropped tokens leave the surrounding text and produce a report row. Tests: Task 2.
2. **A v2 install that never saved naming settings** (no `NamingConfig` row, the case for every fresh v2 DB). v5 gets the ported v2 defaults, not the v5 defaults. The v5 defaults would put scenes under `scenes/…` and break the link to existing folders. Tests: Task 3.
3. **Providers v5 can't run:** Fanzub, RQBit, Tribler, Plex metadata, and an indexer pinned to a skipped download client. They're skipped with a report row; the indexer keeps working with `DownloadClientId = 0`. Tests: Task 5.
4. **Postgres after explicit-ID inserts.** On a Postgres v5 DB, adding a tag, indexer or profile after migration must not collide with a ported ID: sequences are reset. Tests: Task 3, Postgres-only.
5. **Quality profiles round-trip through v5 code.** Remapped IDs, a remapped cutoff, groups (IDs ≥ 1000), and the v5-only qualities that v2 never had must load through `QualityProfileRepository` with every v5 quality present exactly once per profile. Otherwise the Profiles settings page breaks. Tests: Task 4.

---

### Task 1: Fixtures, staging and report tables

**Files:**
- Create:
  - `src/NzbDrone.Core.Test/Files/LegacyV2/v2-fresh-221.sql`, a verbatim copy of the scratchpad SQLite dump;
  - `src/NzbDrone.Core.Test/Files/LegacyV2/v2-fresh-221.pg.sql`, a verbatim copy of the scratchpad Postgres dump;
  - `src/NzbDrone.Core/Datastore/Migration/306_add_legacy_v2_staging.cs`;
  - `src/NzbDrone.Core/LegacyMigration/LegacyV2Series.cs`, `LegacyV2Episode.cs`, `LegacyV2EpisodeFile.cs`;
  - `src/NzbDrone.Core/LegacyMigration/LegacyMigrationReportItem.cs`.
- Modify:
  - `src/NzbDrone.Core/Datastore/TableMapping.cs`;
  - `src/NzbDrone.Core.Test/Datastore/Migration/Setup/LegacyDbBuilder.cs`.
- Test:
  - `src/NzbDrone.Core.Test/Datastore/Migration/Setup/LegacyDbBuilderV2Fixture.cs`;
  - `src/NzbDrone.Core.Test/Datastore/Migration/306_add_legacy_v2_stagingFixture.cs`.

**Interfaces:**
- **Produces:**
  - **Migration 306** creates four tables, each with `Id` as the PK:
    - `LegacyV2Series`: `LegacyId` int, `TpdbSiteId` int, `Title`, `Path`, `RootFolderPath` (nullable), `Monitored` bool, `MonitorNewItems` int, `QualityProfileId` int, `Tags` text, `SeriesType` int, `Added` datetime (nullable), `Done` bool (default false). Indexed on `LegacyId`.
    - `LegacyV2Episodes`: `LegacyId` int, `LegacySeriesId` int, `TpdbSceneId` int, `ExternalId` (nullable), `Title` (nullable), `AirDate` (nullable string, as v2 stores it), `Monitored` bool, `LegacyEpisodeFileId` int, `Done` bool. Indexed on `LegacySeriesId` and `LegacyEpisodeFileId`.
    - `LegacyV2EpisodeFiles`: `LegacyId` int, `LegacySeriesId` int, `RelativePath`, `Path`, `Size` bigint, `Quality` text, `Languages` text, `ReleaseGroup` (nullable), `SceneName` (nullable), `OriginalFilePath` (nullable), `MediaInfo` text (nullable), `IndexerFlags` int, `DateAdded` datetime, `Done` bool. Indexed on `LegacyId`.
    - `LegacyMigrationReportItems`: `Category` int, `Area` string, `Subject` string (nullable), `Detail` string (nullable), `Count` int.
  - **Models:** `LegacyV2Series`, `LegacyV2Episode`, `LegacyV2EpisodeFile`, `LegacyMigrationReportItem : ModelBase`, mapped in `TableMapping` to those tables.
  - **`public enum LegacyReportCategory { SkippedSetting = 0, DroppedToken = 1, LeftBehind = 2, UnmatchedFile = 3, NeedsAttention = 4 }`**, in `LegacyMigrationReportItem.cs`. G-d uses `UnmatchedFile`.
  - **`LegacyDbBuilder`:**
    - `CreateV2SqliteFromDump(string path)` runs `Files/LegacyV2/v2-fresh-221.sql` against a new file, resolved from `TestContext.CurrentContext.TestDirectory`.
    - `CreateV2PostgresFromDump(string connectionString)` runs the `.pg.sql` statements on an open Npgsql connection. It skips lines starting with `\`, and opens a fresh connection afterwards, because the dump empties `search_path`.
    - Row helpers that work on either: `InsertV2Row(IDbConnection, string table, object values)` (Dapper, quoted identifiers).

- [ ] **Step 1: Copy both dumps into `Files/LegacyV2/`** (Read, then Write, byte-for-byte).
- [ ] **Step 2: Write the failing tests:**
  - `LegacyDbBuilderV2Fixture`:
    - `sqlite_dump_should_load_at_schema_221`: max `VersionInfo` is 221, and there are 7 `QualityProfiles` rows.
    - `sqlite_dump_should_be_detected_as_v2`: `LegacyInstallDetector` gives Kind V2 and `Refusal == None`.
    - Postgres-only: `postgres_dump_should_load_at_schema_221`.
  - Migration 306 fixture (`MigrationTest<…>` like the neighbours):
    - `should_create_staging_and_report_tables`: insert and read back one row in each through the mapped models.
- [ ] **Step 3: Run, expect a build failure.** `dotnet test ~/Git/whisparr-next/src/NzbDrone.Core.Test/Whisparr.Core.Test.csproj --filter "FullyQualifiedName~LegacyDbBuilderV2|FullyQualifiedName~add_legacy_v2_staging"`
- [ ] **Step 4: Implement.**
- [ ] **Step 5: Run, expect pass.**
- [ ] **Step 6: Commit:** `New: Staging and report tables for the v2 migration`. The body names the v2 commit the dumps came from (`c55fdc66a`) and how they were made (a fresh v2 start on SQLite, and on Postgres 16 with `pg_dump --inserts`).

### Task 2: v2 → v5 value maps

**Files:**
- Create:
  - `src/NzbDrone.Core/Datastore/Migration/Setup/V2/V2QualityMap.cs`
  - `src/NzbDrone.Core/Datastore/Migration/Setup/V2/V2TokenMap.cs`
- Test:
  - `src/NzbDrone.Core.Test/Datastore/Migration/Setup/V2/V2QualityMapFixture.cs`
  - `src/NzbDrone.Core.Test/Datastore/Migration/Setup/V2/V2TokenMapFixture.cs`

**Interfaces:**
- **Produces:**
  - `public static class V2QualityMap`:
    - `int QualityId(int v2Id)`: the five remaps from Recon; any other ID is unchanged.
    - `int QualitySource(int v2Value)`: the table from Recon. Unknown input returns 0.
    - `int? Language(int v2Id)`: by name. It's built once from a private static v2 `(id, name)` table copied from `~/Git/Whisparrv2/src/NzbDrone.Core/Languages/Language.cs`, matched against v5 `Language.All` by name. A missing language returns null. A comment names the source file.
    - `int? IndexerFlag(int v2Flag)`: a single flag, or null for `256`.
    - `int IndexerFlags(int v2Flags)`: a bitwise remap that drops `256`.
  - `public static class V2TokenMap`:
    - `TokenMapResult Map(string v2Format)`.
    - `public class TokenMapResult { string Format; List<string> DroppedTokens; }`.
- **Token rules (spec, plus the F table; this is the final table G owns):**
  - **Parsing:** tokens are parsed with Sonarr's token regex, copied from v2 `FileNameBuilder.TitleRegex`, so `prefix`, `separator`, `customFormat` and `suffix` are kept. Matching is case-insensitive on the words.
  - **Case:** the case style of the v2 token is applied to the v5 words. All upper stays upper, all lower stays lower, anything else becomes the canonical v5 case.
  - **Separator:** the v2 separator between words is reused between the v5 words.
  - **Renames:**

    | v2 | v5 |
    |---|---|
    | `Site Title` | `Studio Title` |
    | `Site CleanTitle` | `Studio CleanTitle` |
    | `Site TitleSlug` | `Studio TitleSlug` |
    | `Site TitleThe` | `Studio TitleThe` |
    | `Site TitleFirstCharacter` | `Studio TitleFirstCharacter` |
    | `Site Network` | `Studio Network` |
    | `Site TitleWithoutYear` | `Studio Title` |
    | `Site CleanTitleWithoutYear` | `Studio CleanTitle` |
    | `Site TitleTheWithoutYear` | `Studio TitleThe` |
    | `Episode Title` | `Scene Title` |
    | `Episode CleanTitle` | `Scene CleanTitle` |
    | `Episode Performers`, `…Female`, `…Male`, `…Other` | `Scene Performers`, `…Female`, `…Male`, `…Other` |
    | `TpdbSceneId` | `Scene ForeignId` |

  - **Unchanged:** `Release Date`, `Release ShortDate`, `Quality *`, `MediaInfo *`, `Release Group`, `Original Title`, `Original Filename`, `Custom Formats`.
  - **Dropped and reported:**
    - `Site Year`, `Site TitleYear`, `Site CleanTitleYear`, `Site TitleTheYear`, `Site CleanTitleTheYear`, `Site CleanTitleThe`, `Site CleanTitleTheWithoutYear`;
    - `Episode Year`, `TpdbId`, `External Id`;
    - any token not in the tables above.

    The whole `{…}` is removed. Surrounding literal text stays. Runs of spaces left behind are collapsed to one, and the result is trimmed.
  - `TpdbSceneId` → `Scene ForeignId` maps, and also adds a `DroppedTokens` entry, `"TpdbSceneId (now the TPDB UUID)"`, so the report notes the change (spec).

- [ ] **Step 1: Write the failing tests:**
  - `V2QualityMapFixture`:
    - `[TestCase(13, 20)]`, `[TestCase(23, 21)]`, `[TestCase(20, 30)]`, `[TestCase(21, 31)]`, `[TestCase(22, 32)]`, `[TestCase(7, 7)]` → `QualityId`;
    - every row of the `QualitySource` table;
    - `Language(26) == 31` (Arabic), `Language(1) == 1`, `Language(47) == null`;
    - `IndexerFlags(8 | 16 | 256) == 32 | 128`.
  - `V2TokenMapFixture`:
    - `[TestCase("{Site Title}", "{Studio Title}")]`
    - `[TestCase("{Site Title} - {Release-Date} - {Episode Title} [{Quality Full}]", "{Studio Title} - {Release-Date} - {Scene Title} [{Quality Full}]")]`
    - `[TestCase("{site.title}", "{studio.title}")]`
    - `[TestCase("{SITE_TITLE}", "{STUDIO_TITLE}")]`
    - `[TestCase("{Episode Title:30}", "{Scene Title:30}")]`
    - `[TestCase("{[Quality Full]}", "{[Quality Full]}")]`
    - `[TestCase("{Site Title} ({Site Year})", "{Studio Title} ()")]`, with `DroppedTokens == ["Site Year"]`
    - `[TestCase("{Site Title} {Episode Year} {Episode Title}", "{Studio Title} {Scene Title}")]`
    - `[TestCase("{TpdbSceneId}", "{Scene ForeignId}")]`, with one `DroppedTokens` note
    - `[TestCase("{Episode PerformersFemale}", "{Scene PerformersFemale}")]`
- [ ] **Step 2: Run, expect a build failure.** Filter `FullyQualifiedName~Setup.V2`.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run, expect pass.**
- [ ] **Step 5: Commit:** `New: v2 to v5 value and naming-token maps`.

### Task 3: Porter shell, copy-as-is tables, config and naming

**Files:**
- Create, in `src/NzbDrone.Core/Datastore/Migration/Setup/V2/`:
  - `V2PortContext.cs`
  - `V2Porter.cs`
  - `V2CopyPorter.cs`
  - `V2ConfigPorter.cs`
  - `PostgresSequences.cs`
- Modify: `src/NzbDrone.Core/Datastore/Migration/Setup/V5DatabaseBuilder.cs`. `StreamTables` calls `PostgresSequences.Reset` instead of its inline loop, with no behaviour change.
- Test: `src/NzbDrone.Core.Test/Datastore/Migration/Setup/V2/V2CopyPorterFixture.cs`, `V2ConfigPorterFixture.cs`, plus a shared base `V2PorterTestBase.cs`.

**Interfaces:**
- **Consumes:** `LegacyConnections` (G-b), `V2TokenMap` (Task 2), `LegacyMigrationReportItem` (Task 1).
- **Produces:**
  - `public sealed class V2PortContext : IDisposable`:
    - `IDbConnection Legacy` (owned and disposed);
    - `IDbConnection V5`;
    - `IDbTransaction Transaction`;
    - `DatabaseType V5Type`;
    - `List<LegacyMigrationReportItem> Report`;
    - `HashSet<string> InsertedTables`, the tables written with explicit IDs, used for sequence resets;
    - `void Add(LegacyReportCategory category, string area, string subject, string detail, int count = 1)`.
  - `public interface IV2Porter`:
    - `V2PortContext Open(LegacyInstall install, IDbConnection v5, IDbTransaction transaction, DatabaseType v5Type)`. SQLite opens through `LegacyConnections.OpenSqliteReadOnly`; Postgres through `LegacyConnections.OpenPostgres`.
    - `void PortSettings(V2PortContext context)` runs, in order, `V2CopyPorter`, `V2ConfigPorter`, `V2QualityPorter` (Task 4) and `V2ProviderPorter` (Task 5). On Postgres it then calls `PostgresSequences.Reset(context.V5, context.InsertedTables, context.Transaction)`.
    - `V2SnapshotCounts SnapshotLibrary(V2PortContext context)` (Task 6).
    - `void WriteReport(V2PortContext context)` inserts `context.Report` into `LegacyMigrationReportItems`.

    In this task, `V2Porter` takes only the porters that exist so far. Tasks 4–6 add theirs to its constructor and to `PortSettings`.
  - `public static class PostgresSequences { void Reset(IDbConnection connection, IEnumerable<string> tables, IDbTransaction transaction); }`, the same `setval` logic as `V5DatabaseBuilder.StreamTables`.
  - `public class V2CopyPorter { void Port(V2PortContext context); }`:
    - For `Tags`, `RootFolders`, `Users`, `DelayProfiles` and `RemotePathMappings`: delete every v5 row, then insert every legacy row with its `Id`, column for column (the columns are identical, per Recon).
    - Booleans are read and written through typed Dapper parameters, so SQLite ints and Postgres booleans both work.
  - `public class V2ConfigPorter { void Port(V2PortContext context); }`:
    - **`Config`:** every legacy row is upserted into v5 by `Key`, with these key rules (a `private static readonly` dictionary and set, commented with their source):
      - Renamed: `autounmonitorpreviouslydownloadedepisodes` → `autounmonitorpreviouslydownloadedmovies`; `createemptyseriesfolders` → `createemptymoviefolders`.
      - Dropped, with a `SkippedSetting` row each (`Area = "Config"`, `Subject = key`): `episodetitlerequired` and `userrejectedextensions`.
    - **`NamingConfig`:** delete the v5 rows and insert one, from the legacy row, or from the v2 defaults in Recon when there is none:
      - `StandardSceneFormat` = `V2TokenMap.Map(StandardEpisodeFormat)`.
      - `SceneFolderFormat` = `V2TokenMap.Map(SeriesFolderFormat)`. If the mapped folder format is empty, use `"{Studio Title}"` and add a `NeedsAttention` row.
      - `ReplaceIllegalCharacters` is copied.
      - `ColonReplacementFormat`: `5` (Custom) → `4` (Smart), with a `SkippedSetting` row whose `Detail` is the custom string. Any other value is copied.
      - `RenameScenes = false` and `RenameMovies = false`.
      - `StandardMovieFormat`, `MovieFolderFormat`, `SceneImportFolderFormat`, `MaxFolderPathLength` and `MaxFilePathLength` come from v5 `NamingConfig.Default`.
      - Each dropped token gives a `DroppedToken` row (`Area = "Naming"`, `Subject` = the field name, `Detail` = the token).
      - A non-empty `JavEpisodeFormat` gives a `SkippedSetting` row. `MultiEpisodeStyle` is dropped silently, because v5 has no multi-episode files.

- [ ] **Step 1: Write the failing tests.** The shared base builds a v2 SQLite DB with `CreateV2SqliteFromDump` and a fresh v5 SQLite DB through the Core migrations, then opens the context.
  - `V2CopyPorterFixture`:
    - `should_copy_tags_root_folders_users_delay_profiles_mappings_keeping_ids` (insert v2 rows with IDs 5 and 9 first);
    - `should_replace_v5_default_delay_profile`.
    - Postgres-only: `should_reset_sequences_after_copy`. Port, commit, insert a new tag through `TagRepository`, and assert its ID is greater than the ported maximum.
  - `V2ConfigPorterFixture`:
    - `should_copy_config_keys`;
    - `should_rename_series_keys`;
    - `should_drop_and_report_v2_only_keys`;
    - `should_port_v2_defaults_when_no_naming_row`: `SceneFolderFormat == "{Studio Title}"`, and `StandardSceneFormat == "{Studio Title} - {Release-Date} - {Scene Title} [{Quality Full}]"`;
    - `should_map_custom_colon_to_smart_and_report`;
    - `should_disable_renaming`;
    - `should_report_dropped_tokens`;
    - `naming_should_read_back_through_v5_service`: `NamingConfigService.GetConfig()` on the v5 DB returns the ported values.
- [ ] **Step 2: Run, expect a build failure.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run, expect pass. Also run `FullyQualifiedName~V5DatabaseBuilder`** to confirm the `PostgresSequences` extraction changed nothing.
- [ ] **Step 5: Commit:** `New: Port v2 tags, root folders, users, config and naming`.

### Task 4: Quality, custom formats, release profiles and auto tagging

**Files:**
- Create: `src/NzbDrone.Core/Datastore/Migration/Setup/V2/V2QualityPorter.cs`
- Modify: `V2Porter.cs`
- Test: `src/NzbDrone.Core.Test/Datastore/Migration/Setup/V2/V2QualityPorterFixture.cs`

**Interfaces:**
- **Consumes:** `V2PortContext`, `V2QualityMap`.
- **Produces:** `public class V2QualityPorter { void Port(V2PortContext context); }`. It works on JSON with `System.Text.Json.Nodes`, never through v2 types.
- **Rules:**
  - **`QualityProfiles`:** delete the v5 rows and the v5 `QualityProfileQualityRanks` rows. Insert each legacy profile with its `Id`:
    - Every `quality` value in `Items`, at any depth, goes through `V2QualityMap.QualityId`. `minSize`, `maxSize` and `preferredSize` are removed from the items.
    - `Cutoff` goes through `QualityId` when it's below 1000. Group IDs (≥ 1000) are kept.
    - Every v5 quality (`Quality.All`) missing from the items is inserted at the **start** of `Items` as `{"quality": id, "items": [], "allowed": false}`, so ranks for allowed qualities keep their order.
    - `Language = Language.Any.Id` (-1) and `Fallback = false`. `FormatItems`, `MinFormatScore`, `CutoffFormatScore`, `MinUpgradeFormatScore` and `UpgradeAllowed` are copied.
  - **`QualityDefinitions`:**
    - Delete the v5 rows.
    - Choose the **size-source profile**: the profile used by the most `Series` rows, or the lowest `Id` when there are none.
    - For each `QualityDefinitions` legacy row, insert `Quality = QualityId(v2)`. `Title` is the v2 title unless the ID was remapped, in which case it's the v5 `Quality` name.
    - `MinSize`, `MaxSize` and `PreferredSize` come from that quality's item in the size-source profile, or null when it isn't there.
    - Add one `SkippedSetting` row (`Area = "Quality"`, `Detail` = `"Sizes taken from profile <name>"`) when more than one profile exists and any other profile has different sizes.
  - **`CustomFormats`:** delete the v5 rows. Insert each legacy row with its `Id`, remapping each specification's `body.value` by `type`:
    - `SourceSpecification` → `QualitySource`;
    - `LanguageSpecification` → `Language`;
    - `IndexerFlagSpecification` → `IndexerFlag`;
    - any other type is copied.

    A spec whose value maps to null is removed and reported (`SkippedSetting`, `Area = "CustomFormats"`, `Subject` = the format name). A format left with no specs is skipped, and reported, and its ID is also removed from every profile's `FormatItems`.
  - **`ReleaseProfiles`:** delete the v5 rows. Insert with `Id`:
    - `IndexerIds` is `"[]"` when `IndexerId == 0`, otherwise `"[<id>]"`.
    - `Required`, `Ignored`, `Tags`, `Name` and `Enabled` are copied.
    - A non-empty `ExcludedTags` (not `[]`) or `AirDateRestriction = true` gives a `SkippedSetting` row.
  - **`AutoTagging`:** delete the v5 rows. Insert with `Id`:
    - Specs of type `NetworkSpecification` or `StatusSpecification` are removed and reported.
    - `OriginalLanguageSpecification.value` goes through `Language`, with null removed and reported.
    - A rule left with no specs is skipped and reported.

- [ ] **Step 1: Write the failing tests** (the fixture DB is the real dump, so its 7 default profiles are the input):
  - `should_remap_profile_quality_ids_and_cutoff`: put 22 (VR) in a profile with cutoff 22, and expect 32.
  - `should_add_missing_v5_qualities_disallowed_at_start`.
  - `ported_profiles_should_load_through_v5_repository`: `QualityProfileRepository.All()` returns 7, and every v5 quality appears exactly once per profile.
  - `should_leave_no_ranks_for_startup_seeding`.
  - `should_take_sizes_from_most_used_profile`.
  - `should_remap_source_language_and_flag_specs`.
  - `should_drop_and_report_unmappable_language_spec` (Azerbaijani).
  - `should_skip_empty_format_and_remove_it_from_profiles`.
  - `should_convert_release_profile_indexer_id`.
  - `should_drop_network_and_status_auto_tag_specs`.
- [ ] **Step 2: Run, expect a build failure.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run, expect pass.**
- [ ] **Step 5: Commit:** `New: Port v2 quality, custom formats, release profiles and auto tagging`.

### Task 5: Metadata, indexers, download clients and notifications

**Files:**
- Create: `src/NzbDrone.Core/Datastore/Migration/Setup/V2/V2ProviderPorter.cs`
- Modify: `V2Porter.cs`
- Test: `src/NzbDrone.Core.Test/Datastore/Migration/Setup/V2/V2ProviderPorterFixture.cs`

**Interfaces:**
- **Consumes:** `V2PortContext`.
- **Produces:** `public class V2ProviderPorter { void Port(V2PortContext context); }`.
- **Rules:** in each table, delete the v5 rows, then insert each legacy row with its `Id`. Settings JSON is edited with `JsonNode`, renaming only the keys listed in Recon. Unknown keys are left in place, because v5 ignores them.
  - **Skip list:** `Indexers.Implementation = "Fanzub"`, and `DownloadClients.Implementation` `"RQBit"` or `"Tribler"`. Each is skipped with a `SkippedSetting` row (`Area` = the table, `Subject` = the row's `Name`).
  - **`Indexers`:**
    - All columns are copied except `SeasonSearchMaximumSingleEpisodeAge`.
    - A `DownloadClientId` pointing at a skipped client becomes 0, with a `NeedsAttention` row.
    - Newznab and Torznab settings lose the five v2-only keys.
  - **`DownloadClients`:**
    - The `tv*` → `movie*` key renames.
    - Flood: `addPaused = !startOnAdd`, and `startOnAdd` is removed.
  - **`Notifications`:**
    - Columns are mapped by the Recon table.
    - `OnImportComplete = true` gives a `SkippedSetting` row.
    - Email: `useEncryption` from `requireEncryption`.
    - Gotify: `includeMoviePoster` from `includeSeriesPoster`.
  - **`Metadata`:**
    - Rows with `Implementation` `XbmcMetadata`, `RoksboxMetadata` or `WdtvMetadata` are inserted with the same `Name`, `Implementation`, `ConfigContract` and `Enable`, and with settings rebuilt as:
      - `movieMetadata = episodeMetadata`;
      - `movieImages = episodeImages`;
      - Xbmc only: `movieMetadataURL = seriesMetadataUrl`.
    - Every other v5 setting takes its default.
    - `PlexMetadata` is skipped, and reported only when `Enable = true`.

- [ ] **Step 1: Write the failing tests:**
  - `should_rename_download_client_tv_keys` (Sabnzbd and qBittorrent);
  - `should_invert_flood_start_on_add`;
  - `should_skip_and_report_rqbit_tribler_fanzub`;
  - `should_clear_indexer_download_client_pointing_at_skipped_client`;
  - `should_map_notification_event_columns`;
  - `should_convert_email_encryption`;
  - `should_report_on_import_complete`;
  - `should_map_metadata_settings_and_drop_plex`, using the dump's 4 default metadata rows;
  - `ported_providers_should_load_through_v5_factories`: `IndexerRepository`, `DownloadClientRepository` and `NotificationRepository` `.All()` deserialize every row without throwing.
- [ ] **Step 2: Run, expect a build failure.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run, expect pass.**
- [ ] **Step 5: Commit:** `New: Port v2 indexers, download clients, notifications and metadata`.

### Task 6: Library snapshot and left-behind counts

**Files:**
- Create: `src/NzbDrone.Core/Datastore/Migration/Setup/V2/V2LibrarySnapshot.cs`
- Modify: `V2Porter.cs`
- Test: `src/NzbDrone.Core.Test/Datastore/Migration/Setup/V2/V2LibrarySnapshotFixture.cs`

**Interfaces:**
- **Consumes:** `V2PortContext`, `V2QualityMap`, and the Task 1 tables.
- **Produces:**
  - `public class V2SnapshotCounts { int Series; int Episodes; int EpisodeFiles; }`.
  - `public class V2LibrarySnapshot { V2SnapshotCounts Snapshot(V2PortContext context); void CountLeftBehind(V2PortContext context); }`. `IV2Porter.SnapshotLibrary` calls both.
- **Rules (spec: phase 2 never touches the legacy DB):**
  - **Series:**
    - Copy every `Series` row into `LegacyV2Series`, with `LegacyId = Id` and `TpdbSiteId = TvdbId`.
    - `RootFolderPath` is the legacy `RootFolders.Path` that is the longest path-prefix of `Series.Path`, compared with `OsPath` on whole path segments.
    - When none matches, `RootFolderPath` is null and there's a `NeedsAttention` row (`Area = "Library"`, `Subject` = title, `Detail` = path).
  - **Episodes:** copy every row into `LegacyV2Episodes`.
  - **Episode files:** copy every row into `LegacyV2EpisodeFiles`:
    - `Path` = `Series.Path` + `RelativePath`, joined with `OsPath`;
    - `Quality` JSON with its `quality` field through `V2QualityMap.QualityId`;
    - `Languages` JSON array elements through `V2QualityMap.Language`, dropping nulls;
    - `IndexerFlags` through `V2QualityMap.IndexerFlags`.
  - **Streaming:** read in pages of 1000 by `Id`, and insert in batches inside the context transaction. Never load a whole table, because v2 libraries reach hundreds of thousands of episodes.
  - **`CountLeftBehind`:** for each table in `ImportLists`, `ImportListExclusions`, `CustomFilters`, `History`, `DownloadHistory`, `Blocklist`, `PendingReleases`, `Commands`, `ScheduledTasks`, `UpdateHistory`, `ExtraFiles`, `SubtitleFiles`, `MetadataFiles` and every legacy table whose name ends in `Status`:
    - if it exists in the legacy DB and has rows, add a `LeftBehind` row (`Area` = the table, `Count` = its row count);
    - missing tables are skipped silently.

- [ ] **Step 1: Write the failing tests:**
  - `should_snapshot_series_episodes_files_with_ids_and_monitored`;
  - `should_resolve_root_folder_by_longest_prefix`: roots `/data` and `/data/studios`, series `/data/studios/X`, expect `/data/studios`;
  - `should_not_match_root_folder_on_partial_segment`: root `/data/stud`, series `/data/studios/X`, expect null plus a report row;
  - `should_remap_file_quality_languages_and_flags`;
  - `should_page_large_tables`: 2,500 episodes all arrive;
  - `should_count_left_behind_tables` (`History` with 3 rows and `ScheduledTasks` from the dump give two rows; an empty `Blocklist` gives none).
  - Postgres-only: `should_snapshot_from_postgres_legacy`, with the Postgres dump plus rows.
- [ ] **Step 2: Run, expect a build failure.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run, expect pass.**
- [ ] **Step 5: Commit:** `New: Snapshot the v2 library for the migration import`.

### Task 7: Wizard v2 path, fresh v5 DB and end-to-end

**Files:**
- Modify:
  - `src/NzbDrone.Core/Datastore/Migration/Setup/V5DatabaseBuilder.cs` (`CreateEmpty`);
  - `src/NzbDrone.Core/Datastore/Migration/Setup/SetupWizardService.cs`;
  - `src/NzbDrone.Core/Datastore/Migration/Setup/SetupProgress.cs` (new steps);
  - `src/NzbDrone.Core/Localization/Core/en.json`.
- Test:
  - `src/NzbDrone.Core.Test/Datastore/Migration/Setup/V5DatabaseBuilderFixture.cs` (new cases);
  - `SetupWizardServiceFixture.cs` (new cases);
  - `src/NzbDrone.Core.Test/Datastore/Migration/Setup/V2EndToEndFixture.cs`.

**Interfaces:**
- **Consumes:** `IV2Porter` and `V2SnapshotCounts` (Tasks 3–6), plus every G-b interface.
- **Produces:**
  - `IV5DatabaseBuilder.CreateEmpty(LegacyInstall install, V5DatabaseTargets targets, Action<string> recordCreated, Action<string> recordReused) → BuiltDatabases`:
    - **SQLite:** a new `whisparr5.db.partial` next to the legacy file, migrated with the normal `MigrationContext(MigrationType.Main)`.
    - **Postgres:**
      - the same `ValidateTargets` and `RequireEmptyOrMissing`;
      - a plain `CREATE DATABASE` for missing targets (no template);
      - the same denied-privilege message;
      - then migrate.
    - Errors are wrapped as in `CopyEros`.
  - `SetupStep` gains `PortSettings` and `SnapshotLibrary`, placed right after `Migrate` in the enum, because the page lists steps in enum order.
  - en.json gains `SetupStepPortSettings`, "Port your settings", and `SetupStepSnapshotLibrary`, "Save your library for import".
- **Rules:**
  - **`Validate`:** `SetupNotAvailableYet` now applies only when `choice.Kind == Eros && choice.SceneSource != install.PreviousSceneSource`. v2 accepts any scene and movie source.
  - **`Run` for `install.Kind == V2`:**
    1. `Backup` and `SeedConfig`, unchanged.
    2. `CreateDatabase` uses `CreateEmpty`, with the same sidecar recording. `Migrate` is marked done with it.
    3. In one v5 transaction:
       - `Open`;
       - `PortSettings` (step `PortSettings`);
       - `SnapshotLibrary` (step `SnapshotLibrary`);
       - `WriteReport`;
       - commit.
    4. `MixedMovies` is `Skipped`.
    5. `SkyhookPreflight` runs when `counts.Series > 0`, otherwise it's `Skipped`.
    6. `LockSources` and `Finish`: `Phase = "Phase2Pending"` and `Completed = false` when `counts.Series > 0`; otherwise `Phase = "Done"` and `Completed = true`.
    7. The partial rename, sidecar completion and restart, unchanged.
  - **For Eros**, `PortSettings` and `SnapshotLibrary` are `Skipped`. The Eros path is otherwise byte-for-byte unchanged.
  - **A porter failure** goes through the existing `Fail` path: `Unexpected`, `CanRetry = false`, sidecar discard.

- [ ] **Step 1: Write the failing tests:**
  - `V5DatabaseBuilderFixture`:
    - `create_empty_should_build_migrated_partial`: it has the marker, and holds no `Movies` rows;
    - `create_empty_should_leave_legacy_bytes_unchanged`.
    - Postgres-only: `create_empty_should_create_both_databases_without_template`.
  - `SetupWizardServiceFixture`:
    - `v2_should_run_port_and_snapshot_steps_in_order`;
    - `v2_should_no_longer_be_refused`;
    - `eros_scene_change_should_still_be_refused`;
    - `v2_with_series_should_be_phase2_pending`;
    - `v2_without_series_should_be_completed_and_skip_preflight`;
    - `porter_failure_should_discard_sidecar_items`.
  - `V2EndToEndFixture` (real services, SQLite; Skyhook and lifecycle mocked):
    1. Build a v2 DB from the dump.
    2. Add 2 series, 3 episodes and 2 files, a tag, a root folder, a user, a Sabnzbd client, a Newznab indexer, an Email notification, a naming row with `{Site Title}` / `{Episode Title}`, and one `History` row.
    3. Run `Start(V2, Tpdb, Tpdb)` and wait for `Restarting`.

    Assert:
    - `whisparr5.db` has the marker;
    - the staging counts are 2/3/2;
    - the state is `Phase2Pending`;
    - the sources are locked to TPDB/TPDB;
    - renaming is disabled;
    - `SceneFolderFormat == "{Studio Title}"`;
    - the client has `movieCategory`;
    - there's a `LeftBehind` report row for `History`;
    - the user signs in through `UserService` with the v2 password;
    - the legacy DB and `config.xml` SHA-256 values are unchanged;
    - there's no sidecar;
    - `Detect().SetupModeRequired` is false.
  - Postgres-only: `v2_postgres_end_to_end`. The same, from the Postgres dump, with fewer assertions: the marker, the counts, the state, and a new tag ID above the ported maximum.
- [ ] **Step 2: Run, expect a build failure.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the Core project in full, then Api and Host, each on its own. Expect pass.** Run `yarn lint` (en.json only changed, so typecheck and build aren't needed).
- [ ] **Step 5: Commit:** `New: Migrate v2 installs in setup phase 1`.

### Task 8: Live check of the v2 path

No code unless the check finds a bug. A fix goes in its own `Fix:` commit with a test.

- [ ] **Step 1: SQLite.**
  1. Stop any instance on 6969.
  2. Make a fresh AppData with `whisparr2.db` built from the dump, plus the Task 7 end-to-end rows (`sqlite3` on the scratch copy, never on a real install), plus a `config.xml` with `Forms` auth.
  3. Start the v5 publish with `-data=<that folder>`.
  4. Check:
     - the halt page shows v2 with TPDB/TPDB defaults and the queue note;
     - sign-in works with the v2 user;
     - "Back up & migrate" shows the two new steps and restarts;
     - Settings → Media Management shows `{Studio Title}`, and renaming is disabled;
     - Profiles, Indexers, Download Clients, Notifications and Metadata pages load and show the ported rows;
     - the `LegacyMigrationReportItems` rows match the skipped items;
     - the legacy SHA-256 values are unchanged.
- [ ] **Step 2: Postgres.** Repeat with a throwaway `postgres:16`, loading the `.pg.sql` dump into `whisparr-main`. Stop the container after.
- [ ] **Step 3: Screenshots.** Take a halt-page screenshot of the v2 step list with SFW mode on, and save it to the session scratchpad. Report its absolute path.
