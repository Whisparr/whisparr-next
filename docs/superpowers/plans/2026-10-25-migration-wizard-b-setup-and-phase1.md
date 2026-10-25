# Migration wizard G-b: detection, setup mode, halt page and phase 1, implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** On a v2 or Eros install, v5 halts on a "Migration needed" page instead of starting. For Eros, keeping StashDB, one click backs up the legacy data, builds a separate v5 DB and `config5.xml`, and restarts into the normal app with the library intact.

**Architecture:**
- **Detection** (`LegacyInstallDetector`) runs in `Bootstrap` before the host is built. It's read-only, and looks at the AppData files, the legacy `config.xml` and the `Whisparr__Postgres__*` env vars.
- **Setup mode:**
  - When a legacy install is found and no marked v5 DB exists, the host is built with the dummy main DB. No DB is migrated, and neither `ApplicationStartingEvent` nor `ApplicationStartedEvent` is published, so no command threads or scheduler run.
  - A middleware allows only the setup API, the login pages and the frontend bundle.
  - The bundle renders a `SetupApp` instead of the normal app.
- **Phase 1** runs as a background task inside setup mode (`SetupWizardService`). The page polls its progress.
- **Discarding an unfinished run:** a sidecar file, `v5-setup.json`, records what phase 1 created, so an interrupted run can be discarded on the next start.
- **On success**, phase 1 writes the migration state, deletes the sidecar and restarts the process. The next start finds a marked v5 DB and boots normally.

**Tech Stack:** C#/.NET 10, Dapper, System.Data.SQLite, Npgsql, FluentMigrator, NUnit/Moq/FluentAssertions, React/TypeScript.

**Spec:** `docs/superpowers/specs/2026-10-24-migration-wizard-design.md` (Detection; The halt page; Phase 1; Building the v5 DB, by source; Resume; Reset and downgrade).

## Scope

**In scope:**
- Detection of every DB shape the spec lists, including both refusals.
- Setup mode and the halt page for v2 and Eros.
- Every phase 1 step for **Eros with StashDB kept**, including mixed movies (merge or detach) and a change of movie source.
- SQLite and Postgres.

**Out of scope, owned by later plans:**
- **G-c:** building the v5 DB for v2 (the porter and staging snapshot).
- **G-d:**
  - the Eros scene-source change (seeding);
  - phase 2;
  - the automation pause;
  - the report;
  - the health notice.
- **Until those land**, choosing v2 or a different scene source halts on the page with `SetupNotAvailableYet`. Detached movies wait in `ImportFiles` with a seed row, and the migration state stays incomplete.

## Global Constraints

- **Edits:** use the Edit and Write tools only. Never sed, shell redirection or chained Bash. Run one plain command per Bash call, with absolute paths.
- **Commits:** on `develop` in `~/Git/whisparr-next`. Never push.
- **No AI attribution.** Use the plz12345 handle and `~/` paths.
- **Commit prefixes:** `New:`, `Fix:` or `Chore:`.
- **Legacy files and DBs are opened read-only, every time.** The legacy DB, `config.xml` and the legacy Postgres DBs are never written to (spec anti-goal).
- **No step touches media files.**
- **v5-only DB migrations are numbered ≥ 305.**
- **Wording:** the halt page says **"renaming is disabled"**, never "off".
- **Postgres safety rule:**
  - The wizard never creates, drops or templates onto a DB it didn't create in this run.
  - It refuses when a v5 target name is the same as a legacy name.
  - An existing target must be empty, or the wizard refuses.
  - Discarding an interrupted run drops only DBs recorded in the sidecar.
- **Tests:**
  - NUnit `*Fixture`, ≥ 80% coverage on new code.
  - Run each test project on its own (Core, Common, Api, Host, Mono). A solution-wide `dotnet test` reports false log-capture failures.
  - Postgres fixtures run only when `Whisparr__Postgres__Host` is set, the same way as `DbTest`. Use a throwaway `postgres:16` container on a free port, never the Skyhook stack's DB.
- **Frontend:** `yarn typecheck`, `yarn lint` and `yarn build` from the repo root. Strings go in `src/NzbDrone.Core/Localization/Core/en.json` (alphabetical). Props are `Readonly<T>`.
- **The V3 API is frozen.**

## Review Focus

1. **A user who runs v5 fresh, then drops an old `whisparr3.db` into AppData.** A marked v5 DB already exists, so v5 boots normally and never enters setup mode. Tests: Task 1.
2. **A Postgres env var (`Whisparr__Postgres__MainDb`, `LogDb` or a connection string) still names the legacy DB after phase 1** (plz12345, 2026-10-25).
   - Before migrating, the halt page lists each variable and the value it needs.
   - After phase 1, the app doesn't restart. The page says the v5 database is ready and to change the listed variables and recreate the container, with a Shut down button.
   - On every later start where a completed v5 DB exists under the `config5.xml` names but an env var still names the legacy DB, a halt page shows only the env-var instructions. The migration is never offered again, and startup doesn't crash.
   - Phase 1 never writes to the legacy DB.

   Tests: Task 2 (seeder target names), Task 8 (no restart, and detection of the pending switch), Task 9 (status fields).
3. **The process is killed halfway through phase 1.**
   - The next start discards only what the sidecar lists. The legacy DB is untouched, and a pre-existing DB the wizard didn't create is never dropped.
   - The halt page shows again.

   Tests: Task 8.
4. **Someone on the network reaches the halt page of an install with no usable credentials** (no `Users` row, or auth disabled). Non-local requests get 403 with an explanation; local requests work. Tests: Task 3.
5. **The legacy file's checksum changes because a "read-only" step wrote to it** (a SQLite journal or WAL, a password rehash on login, `EnsureDefaultConfigFile`). Every task that opens a legacy file asserts that its SHA-256 is unchanged. Tests: Tasks 1, 3, 5 and 6.

---

### Task 1: Legacy install detection

**Files:**
- Create: `src/NzbDrone.Core/Datastore/Migration/Setup/LegacyInstallDetector.cs`, `LegacyInstall.cs`, `LegacyConnections.cs`
- Test: `src/NzbDrone.Core.Test/Datastore/Migration/Setup/LegacyInstallDetectorFixture.cs`, plus a test helper `LegacyDbBuilder.cs` in the same folder

**Interfaces:**
- **Produces:**
  - `public enum LegacyKind { V2, Eros }`.
  - `public enum LegacyRefusal { None, V2TooOld, ErosNewerThanV5 }`.
  - `public class LegacyDatabaseLocation`:
    - `DatabaseType Type`;
    - `string SqlitePath`;
    - `string PostgresMainDb`;
    - `string PostgresConnectionString`, the legacy main DB connection string, used read-only;
    - `bool NamedByEnvironment`, true when `Whisparr__Postgres__MainDb` or `MainDbConnectionString` named it.
  - `public class LegacyInstall`:
    - `LegacyKind Kind`;
    - `LegacyDatabaseLocation Database`;
    - `LegacyRefusal Refusal`;
    - `long MaxMigration`;
    - `int Studios`, `int Scenes`, `int Movies`, `int Files`;
    - `SceneMetadataType PreviousSceneSource`;
    - `MovieMetadataType PreviousMovieSource`;
    - `bool HasUsableCredentials`;
    - `bool HasMixedMovies` (Eros only).
  - `public class DetectionResult`:
    - `bool SetupModeRequired`;
    - `List<LegacyInstall> Installs` (0–2);
    - `string LegacyConfigPath`.
  - `public interface ILegacyInstallDetector { DetectionResult Detect(); }`. The constructor takes `IAppFolderInfo` and `IOptions<PostgresOptions>`, so it can be built by hand in `Bootstrap` before the container exists.
  - `public static class LegacyConnections`:
    - `IDbConnection OpenSqliteReadOnly(string path)`: `ReadOnly = true`, `FailIfMissing = true`, `Pooling = false`, with no journal or WAL change. Follow `LegacyDatabaseGuard.IsRestorableSqliteFile`.
    - `IDbConnection OpenPostgres(string connectionString)`: every session runs `SET default_transaction_read_only = on`.
- **Rules (spec, Detection):**
  - **No setup mode** when a marked v5 main DB exists: `whisparr5.db`, or the v5 Postgres main DB, for which `LegacyDatabaseGuard.IsLegacy` is false and the `VersionInfo` table exists. The v5 Postgres name comes from `config5.xml` plus env vars when `config5.xml` exists. Otherwise it's `whisparr5-main` on the legacy server.
  - **SQLite candidates:** `whisparr2.db` and `whisparr3.db` in AppData.
  - **Postgres candidate:** the main DB named by legacy `config.xml` (`PostgresHost`, `PostgresPort`, `PostgresUser`, `PostgresPassword`, `PostgresMainDb` defaulting to `whisparr-main`, or `PostgresMainDbConnectionString`), with env vars overriding.
  - **Kind:**
    - `Series` without `Movies` means `V2`.
    - `Movies` means `Eros`.
    - Anything else isn't a legacy install.
  - **Refusals:**
    - `V2TooOld` when V2 and `max(VersionInfo.Version) < 221`.
    - `ErosNewerThanV5` when Eros and any `VersionInfo.Version < 300` isn't a `[Migration(N)]` number in the Core assembly. Reflect over `NzbDroneMigrationBase` subclasses in `NzbDrone.Core.Datastore.Migration`.
  - **Counts:**
    - V2: `Series`, `Episodes` and `EpisodeFiles`.
    - Eros: `Studios`; `Movies` joined to `MovieMetadata` with `ItemType` = Scene (1) for scenes and Movie (0) for movies; and `MovieFiles`.
  - **Previous sources:**
    - V2 is TPDB/TPDB.
    - Eros scenes are StashDB. The Eros movie source is the `Config` row `whisparrmoviemetadatasource`, parsed case-insensitively as `MovieMetadataType`. A missing row means TMDB.
  - **`HasUsableCredentials`:** legacy `config.xml` has `AuthenticationMethod` of `Forms` or `Oidc`, and the method is Oidc or `Users` has at least one row.
  - **`HasMixedMovies`:** Eros movie rows (`ItemType` = 0) include more than one `MetadataSource` value among `Tmdb` (0) and `TpdbMovie` (2).

- [ ] **Step 1: Write the failing tests**

`LegacyDbBuilder`:
- `CreateErosSqlite(path)` runs the Core migrations with `MigrationContext.DesiredVersion = 247` on a fresh file.
- `CreateV2Sqlite(path, maxVersion)` creates `VersionInfo` (rows 1..maxVersion), `Series`, `Episodes` and `EpisodeFiles` with the columns phase 2 will read (`Id`, `TvdbId`, `Title`, `Monitored`, `Path`, `SeriesId`, `EpisodeFileId`, `RelativePath`).
- Helpers to insert scene, movie and file rows.

`LegacyInstallDetectorFixture` (`TempFolder`-based, SQLite):
- `should_not_require_setup_for_empty_appdata`.
- `should_detect_v2_sqlite`: Kind V2, counts, and sources TPDB/TPDB.
- `should_detect_eros_sqlite_with_previous_movie_source`: a Config row `TPDB` gives `PreviousMovieSource == TPDB`.
- `should_default_eros_movie_source_to_tmdb_when_unset`.
- `should_detect_both_installs`: 2 installs.
- `should_not_require_setup_when_marked_v5_db_exists`: a fresh `whisparr5.db` plus `whisparr3.db` gives `SetupModeRequired == false`.
- `should_refuse_v2_older_than_221`: max 220 gives `V2TooOld`.
- `should_refuse_eros_with_unknown_migration`: a `VersionInfo` row 250 gives `ErosNewerThanV5`.
- `should_flag_mixed_movies`.
- `should_report_credentials_usable_only_with_forms_and_a_user`: 3 cases.
- `should_not_change_legacy_file_bytes`: SHA-256 of `whisparr3.db` is the same before and after `Detect()`, and there are no `-journal` or `-wal` siblings.

Postgres-only (`Assert.Ignore` without a host):
- `should_detect_eros_postgres`;
- `should_mark_env_named_legacy_db` (`NamedByEnvironment == true`).

- [ ] **Step 2: Run, expect a build failure.** `dotnet test ~/Git/whisparr-next/src/NzbDrone.Core.Test/Whisparr.Core.Test.csproj --filter "FullyQualifiedName~Migration.Setup"`
- [ ] **Step 3: Implement** the files above.
- [ ] **Step 4: Run, expect pass.**
- [ ] **Step 5: Commit:** `New: Detect legacy v2 and Eros installs before startup`.

### Task 2: `config5.xml` seeding

**Files:**
- Create: `src/NzbDrone.Core/Datastore/Migration/Setup/Config5Seeder.cs`
- Test: `src/NzbDrone.Core.Test/Datastore/Migration/Setup/Config5SeederFixture.cs`

**Interfaces:**
- **Consumes:** `LegacyInstall` (Task 1).
- **Produces:**
  - `public class V5DatabaseTargets { string MainDb; string LogDb; bool EnvironmentStillNamesLegacy; }`.
  - `public interface IConfig5Seeder { V5DatabaseTargets Seed(string legacyConfigPath, string config5Path, LegacyInstall install); }`. It writes `config5.xml` to a temp file in the same folder, then moves it into place.
- **Rules:**
  - Copy every element of the legacy `<Config>` root.
  - Apply the v2 key renames. Build the list by diffing the keys v2's `ConfigFileProvider` reads with the keys v5's reads (`~/Git/Whisparrv2/src/NzbDrone.Core/Configuration/ConfigFileProvider.cs` against v5's). Keys with no v5 equivalent are copied unchanged. Put the list in a `private static readonly Dictionary<string, string> V2KeyRenames` with a comment naming its source.
  - **Postgres targets:**
    - `MainDb` is the env var `Whisparr__Postgres__MainDb` when it's set and differs from the legacy main name; otherwise `whisparr5-main`.
    - `LogDb` follows the same rule, with `whisparr5-log`.
    - Write `PostgresMainDb` and `PostgresLogDb`.
    - When the legacy config uses `PostgresMainDbConnectionString` or `PostgresLogDbConnectionString`, rewrite their `Database=` to the targets with `NpgsqlConnectionStringBuilder`.
  - `EnvironmentStillNamesLegacy` is true when an env var names the legacy main or log DB.
  - The legacy file is opened read-only and is never written to.

- [ ] **Step 1: Write the failing tests:**
  - `should_copy_every_key`;
  - `should_set_postgres_names_to_v5_defaults`;
  - `should_keep_env_named_non_legacy_target`;
  - `should_flag_env_still_naming_legacy`;
  - `should_rewrite_connection_string_database`;
  - `should_apply_v2_key_renames`: at least one rename from the list, if the diff finds any. If it finds none, the test asserts that the dictionary is empty and the commit body says so;
  - `should_not_modify_legacy_config` (SHA-256).
- [ ] **Step 2: Run, expect a build failure.** Same filter as Task 1.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run, expect pass.**
- [ ] **Step 5: Commit:** `New: Seed config5.xml from a legacy config.xml`.

### Task 3: Setup-mode boot, request gate and halt-page sign-in

**Files:**
- Modify: `src/NzbDrone.Host/Bootstrap.cs`, `src/NzbDrone.Host/Startup.cs` (`Configure`, around lines 274–296), `src/NzbDrone.Host/AppLifetime.cs:57-69`
- Create:
  - `src/NzbDrone.Core/Datastore/Migration/Setup/SetupModeState.cs`
  - `src/NzbDrone.Core/Datastore/Migration/Setup/SetupRunState.cs`
  - `src/Whisparr.Http/Middleware/SetupModeMiddleware.cs`
  - `src/NzbDrone.Core/Authentication/PasswordHasher.cs`
  - `src/NzbDrone.Core/Datastore/Migration/Setup/LegacyUserService.cs`
- Modify:
  - `src/NzbDrone.Core/Authentication/UserService.cs`, to use `PasswordHasher`;
  - `src/Whisparr.Http/Frontend/InitializeJsonController.cs`, to add `"setupMode": true|false`.
- Test:
  - `src/NzbDrone.Host.Test/SetupModeContainerFixture.cs`
  - `src/NzbDrone.Api.Test/Middleware/SetupModeMiddlewareFixture.cs`
  - `src/NzbDrone.Core.Test/Authentication/PasswordHasherFixture.cs`
  - `src/NzbDrone.Core.Test/Datastore/Migration/Setup/LegacyUserServiceFixture.cs`
  - `src/NzbDrone.Core.Test/Datastore/Migration/Setup/SetupRunStateFixture.cs`

**Interfaces:**
- **Consumes:** `ILegacyInstallDetector` (Task 1); `IConfig5Seeder` (Task 2).
- **Produces:**
  - `public interface ISetupModeState { bool IsActive { get; } DetectionResult Detection { get; } bool SeededConfig5 { get; } }`. It's a registered instance: `SetupModeState.Inactive` in normal mode. `SeededConfig5` is true when setup entry created `config5.xml`.
  - `public interface ISetupRunState`, implemented by `SetupRunState` with an `IAppFolderInfo` constructor so `Bootstrap` can build it before the container exists. It's backed by the sidecar `<AppData>/v5-setup.json`:
    - `void Begin(bool seededConfig5)`;
    - `void RecordDatabase(string name)`, for a Postgres DB, with the server connection details kept alongside so it can be dropped later;
    - `void RecordSqlitePartial(string path)`;
    - `void Complete()`, which deletes the sidecar;
    - `bool DiscardIfPresent()`. It drops only the recorded Postgres DBs, deletes only the recorded partial file, and deletes `config5.xml` only when `seededConfig5` was true. It returns whether anything was discarded, and logs each item it removes.
  - `public static class PasswordHasher`:
    - `bool Verify(User user, string password)` covers the salted PBKDF2-HMACSHA512 path, using `user.Iterations`, and the unsalted SHA-256 legacy path.
    - `void SetHash(User user, string password)`.
  - `LegacyUserService : IUserService`, registered only in setup mode. It reads `Users` from the chosen legacy DB through `LegacyConnections` and verifies with `PasswordHasher.Verify`. It never rehashes or updates. `Add`, `Update` and `Upsert` throw `InvalidOperationException`. When both installs exist, it prefers Eros.
- **Rules:**
  - **`Bootstrap.CreateConsoleHostBuilder`:**
    - Build the detector by hand.
    - Before detecting, call `new SetupRunState(appFolderInfo).DiscardIfPresent()`, so an interrupted phase 1 is discarded and the halt page shows again.
    - If `SetupModeRequired` is true:
      - when `config5.xml` is missing, seed it with `Config5Seeder` from the first install;
      - register `AddDummyDatabase()` in place of `AddDatabase()`;
      - register the `SetupModeState` instance and `LegacyUserService` (`IfAlreadyRegistered.Replace`).
    - Port, bind address and URL base then come from the seeded `config5.xml` through the existing `GetConfiguration`.
  - **`Startup.Configure` in setup mode:** skip `_ = mainDatabaseFactory.Value`, `SchemaBuilder.Initialize` and the `ApplicationStartingEvent` publish. The log DB still initialises as today.
  - **`AppLifetime.OnAppStarted` in setup mode:** don't publish `ApplicationStartedEvent`. Launching the browser stays.
  - **`SetupModeMiddleware`**, placed right after `UseAuthorization`:
    - In setup mode, requests to `/api/*` other than `/api/setup/*`, and requests to `/signalr`, `/feed` and `/docs`, get 503 with JSON `{"setupMode":true}`.
    - When `HasUsableCredentials` is false on every install, a request whose remote address isn't local (`IPAddress.IsLoopback` or a private range, reusing the helper the "disabled for local addresses" check uses) gets 403 with a plain-text explanation.
    - In normal mode it passes everything through.

- [ ] **Step 1: Write the failing tests:**
  - `PasswordHasherFixture`:
    - `should_verify_v2_hash_with_10000_iterations`: hash with iterations 10000 via a copy of v2's formula, then `Verify` is true;
    - `should_verify_unsalted_sha256`;
    - `should_reject_wrong_password`.
  - `LegacyUserServiceFixture`:
    - `should_find_user_with_correct_password`;
    - `should_not_write_to_legacy_db_on_login` (SHA-256 unchanged, even when the iterations are below v5's);
    - `should_throw_on_add`.
  - `SetupModeMiddlewareFixture`:
    - `should_block_api_in_setup_mode`;
    - `should_allow_setup_api`;
    - `should_forbid_remote_without_credentials`;
    - `should_allow_local_without_credentials`;
    - `should_pass_everything_in_normal_mode`.
  - `SetupModeContainerFixture`:
    - the container built through the setup-mode path resolves `Startup`'s `Configure` dependencies and `IUserService` as `LegacyUserService`;
    - resolving them opens no DB connection (the main DB is the dummy).
  - `SetupRunStateFixture`:
    - `discard_should_drop_only_recorded_items`: an unrecorded `whisparr5.db.partial` survives, and a recorded one is deleted;
    - `discard_should_keep_config5_not_seeded_by_wizard`;
    - `discard_should_return_false_without_sidecar`;
    - `complete_should_delete_sidecar`;
    - Postgres-only: `discard_should_drop_recorded_db_and_leave_others`.
- [ ] **Step 2: Run each project's filter, expect failures.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run Core, Api and Host tests in full, each project on its own. Expect pass.**
- [ ] **Step 5: Commit:** `New: Start in setup mode when a legacy install is found`.

### Task 4: Pre-v5 backup (hard gate)

**Files:**
- Create: `src/NzbDrone.Core/Datastore/Migration/Setup/PreV5BackupService.cs`, `PostgresTableCopier.cs`
- Test: `src/NzbDrone.Core.Test/Datastore/Migration/Setup/PreV5BackupServiceFixture.cs`

**Interfaces:**
- **Consumes:** `LegacyInstall`, `LegacyConnections` (Task 1).
- **Produces:**
  - `public interface IPreV5BackupService { string Backup(LegacyInstall install, string legacyConfigPath); }`. It returns the zip path: `<AppData>/Backups/pre-v5/<yyyy.MM.dd_HH.mm.ss>.zip`. It throws `SetupStepException` (a new class in this task, with a `string UserMessage`) on any failure, after deleting a partial zip.
  - `public class PostgresTableCopier`:
    - `IEnumerable<string> ListTables(IDbConnection)`: the `public` base tables;
    - `void CopyOut(NpgsqlConnection, string table, Stream)`: COPY text format;
    - `void CopyIn(NpgsqlConnection, string table, Stream)`;
    - `string DescribeSchema(NpgsqlConnection)`: `CREATE TABLE` statements from `information_schema.columns`, plus the `pg_indexes.indexdef` lines.
- **Rules:**
  - **SQLite:** stage in a temp folder under `Backups/pre-v5/`. Copy the DB through the SQLite backup API from a read-only source connection into `<name>.db` with journal mode Truncate (as `MakeDatabaseBackup` does), plus `config.xml`. Zip, then delete the staging folder.
  - **Postgres:** `schema.sql`, plus one `data/<table>.copy` per table, plus `config.xml`.
  - The zip opens with `ZipFile.OpenRead`, and the backed-up SQLite opens and holds the same row counts.

- [ ] **Step 1: Write the failing tests:**
  - `should_write_zip_with_db_and_config`;
  - `should_produce_db_with_same_row_counts`;
  - `should_leave_legacy_bytes_unchanged`;
  - `should_throw_and_leave_no_zip_when_db_missing`;
  - Postgres-only: `should_dump_every_table_and_schema`.
- [ ] **Step 2: Run, expect a build failure.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run, expect pass.**
- [ ] **Step 5: Commit:** `New: Back up a legacy install before migration`.

### Task 5: Build the v5 DB from an Eros DB

**Files:**
- Create: `src/NzbDrone.Core/Datastore/Migration/Setup/V5DatabaseBuilder.cs`
- Modify:
  - `src/NzbDrone.Core/Datastore/Migration/Framework/MigrationContext.cs`: add `bool AllowLegacySource`.
  - `src/NzbDrone.Core/Datastore/Migration/Framework/MigrationController.cs`: skip `RefuseLegacyDatabase` when `AllowLegacySource` is set.
- Test: `src/NzbDrone.Core.Test/Datastore/Migration/Setup/V5DatabaseBuilderFixture.cs`

**Interfaces:**
- **Consumes:** `LegacyInstall` (Task 1), `V5DatabaseTargets` (Task 2), `PostgresTableCopier` and `SetupStepException` (Task 4).
- **Produces:**
  - `public class BuiltDatabases`:
    - `string MainConnectionString`, `string LogConnectionString`;
    - `DatabaseType Type`;
    - `string SqlitePartialPath`, which is `whisparr5.db.partial` and null for Postgres;
    - `List<string> CreatedPostgresDatabases`.
  - `public interface IV5DatabaseBuilder { BuiltDatabases CopyEros(LegacyInstall install, V5DatabaseTargets targets, Action<string> recordCreated); }`. `recordCreated` is called with each Postgres DB name right after it's created and before anything is written to it, so Task 8 can add it to the sidecar.
- **Rules:**
  - **SQLite:** use the backup API from a read-only source into `whisparr5.db.partial`, then run the main migrations with `AllowLegacySource = true` against the partial file. The log DB `logs5.db` is left to normal startup.
  - **Postgres:**
    - Refuse with `SetupStepException` when a target equals the legacy main or log name.
    - If a target exists, it must have no tables in `public`, or the wizard refuses.
    - **Create:**
      - Call `NpgsqlConnection.ClearAllPools()`.
      - Try `CREATE DATABASE "<main>" TEMPLATE "<legacy>"`.
      - **If that fails but a plain create succeeds:** create an empty DB, migrate it with `DesiredVersion = install.MaxMigration` and `AllowLegacySource = true`, then `CopyIn` each table from a read-only legacy `CopyOut`, except `VersionInfo`. Then `setval` each `<table>_Id_seq` to `max("Id")`.
      - Create the log DB empty.
      - **If create isn't permitted:** throw `SetupStepException`, with `UserMessage` carrying the exact `CREATE DATABASE` SQL for both names and the env vars `Whisparr__Postgres__MainDb` and `Whisparr__Postgres__LogDb`.
    - Then run the main migrations with `AllowLegacySource = true`.
  - The marker row (300) exists after migration, so `LegacyDatabaseGuard.IsLegacy` is false.

- [ ] **Step 1: Write the failing tests** (SQLite):
  - `should_copy_and_migrate_eros_db`: the partial has migration 304 and the marker, and its scene, movie and file counts equal the legacy ones;
  - `should_leave_legacy_bytes_unchanged`;
  - `should_lock_stashdb_via_migration_301`: the `Config` row `metadatasourceslocked` is true.

  Postgres-only:
  - `should_template_copy`;
  - `should_fall_back_to_streaming_when_template_fails` (keep a second connection open on the legacy DB so TEMPLATE fails);
  - `should_refuse_target_equal_to_legacy_name`;
  - `should_refuse_non_empty_existing_target`;
  - `should_report_sql_and_env_vars_when_create_denied` (a role without `CREATEDB`).
- [ ] **Step 2: Run, expect a build failure.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run, expect pass. Also run** `--filter "FullyQualifiedName~LegacyDatabaseGuard"` to confirm the guard is unchanged when `AllowLegacySource` is false.
- [ ] **Step 5: Commit:** `New: Build the v5 database from an Eros database`.

### Task 6: Migration state, movie seeds and mixed movies

**Files:**
- Create:
  - `src/NzbDrone.Core/Datastore/Migration/305_add_legacy_migration_state.cs`
  - `src/NzbDrone.Core/LegacyMigration/LegacyMigrationState.cs`
  - `src/NzbDrone.Core/LegacyMigration/LegacyMigrationStateRepository.cs`
  - `src/NzbDrone.Core/LegacyMigration/LegacyMovieSeed.cs`
  - `src/NzbDrone.Core/Datastore/Migration/Setup/MixedMovieResolver.cs`
- Modify: `src/NzbDrone.Core/Datastore/TableMapping.cs`
- Test:
  - `src/NzbDrone.Core.Test/Datastore/Migration/Setup/MixedMovieResolverFixture.cs`
  - `src/NzbDrone.Core.Test/LegacyMigration/LegacyMigrationStateRepositoryFixture.cs`

**Interfaces:**
- **Produces:**
  - **Migration 305** creates two tables:
    - `LegacyMigrationState`: `Id`; `SourceKind` int (`LegacyKind`); `SceneSource` int; `MovieSource` int; `Phase` string; `Progress` text (JSON, nullable); `Completed` bool; `StartedAt` and `CompletedAt` (nullable) datetimes; `BackupPath` string.
    - `LegacyMovieSeeds`: `Id`; `LegacyForeignId`; `Title`; `Year` int; `TmdbId` int; `ImdbId`; `Monitored` bool; `QualityProfileId` int; `RootFolderPath`; `Tags` text; `ImportFileId` int (nullable); `Resolved` bool.
  - `LegacyMigrationState` and `LegacyMovieSeed : ModelBase` are mapped in `TableMapping`.
  - `ILegacyMigrationStateRepository : IBasicRepository<LegacyMigrationState>` adds `LegacyMigrationState GetCurrent()`, which returns the single row or null.
  - `public class MixedMovieResult { int Kept; int Merged; int Detached; }`.
  - `public interface IMixedMovieResolver { MixedMovieResult Resolve(IDbConnection v5Connection, IDbTransaction transaction, MovieMetadataType chosen); }`. It runs raw SQL on the new v5 DB, never the legacy one.
- **Rules (spec, mixed movies):**
  - Kept rows are movies (`ItemType` = 0) whose `MetadataSource` matches the chosen source: TMDB → `Tmdb` (0), TPDB → `TpdbMovie` (2).
  - **Merge:** an other-source movie row is a twin only when its `MovieMetadata.TmdbId > 0` equals a kept row's `TmdbId`. No title matching: a wrong merge would be silent.
  - **Twin handling:**
    - If the kept row has no file (`MovieFileId = 0`), move the twin's file onto it: update `MovieFiles.MovieId` and the kept row's `Movies.MovieFileId`.
    - Otherwise, detach the twin's file.
    - Then delete the twin's `Movies` and `MovieMetadata` rows and their `Credits` and `AlternativeTitles`.
  - **No twin:**
    - Detach its file, if any.
    - Insert a `LegacyMovieSeeds` row from its metadata and movie values, with `ImportFileId` set.
    - Delete its `Movies` and `MovieMetadata` rows and their `Credits` and `AlternativeTitles`.
  - **Detach:** insert an `ImportFiles` row:
    - `RootFolderId` comes from the `RootFolders` row whose path is the longest prefix of the movie's path;
    - `Path` is the absolute file path;
    - `RelativePath` is relative to that root;
    - `OsHash`, `PHash`, `PHashAttemptedAt` and `HashedFileModifiedAt` are copied from the file;
    - `QualityProfileId` comes from the movie.
    
    Then delete the `MovieFiles` row and set `Movies.MovieFileId = 0`. Nothing on disk is touched.
  - Scenes (`ItemType` = 1) are never touched.

- [ ] **Step 1: Write the failing tests:**
  - `should_keep_rows_matching_chosen_source`;
  - `should_merge_twin_file_onto_kept_row_without_file`;
  - `should_detach_twin_file_when_kept_row_has_file`;
  - `should_detach_and_seed_row_without_twin`: there's an `ImportFiles` row with hashes, and a seed with `Monitored`, `TmdbId` and the `ImportFileId`;
  - `should_not_merge_on_title_alone`: same title and year with a different `TmdbId` gives a detach, not a merge;
  - `should_not_touch_scenes`;
  - `should_be_noop_without_mixed_movies`.
  - Repository: `get_current_should_return_null_then_row`.
- [ ] **Step 2: Run, expect a build failure.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run, expect pass, including the existing migration fixtures** (`--filter "FullyQualifiedName~Datastore"`).
- [ ] **Step 5: Commit:** `New: Record migration state and resolve mixed movie sources`.

### Task 7: Skyhook preflight and finishing steps

**Files:**
- Create: `src/NzbDrone.Core/Datastore/Migration/Setup/SkyhookPreflight.cs`, `V5DatabaseFinisher.cs`
- Test: `src/NzbDrone.Core.Test/Datastore/Migration/Setup/SkyhookPreflightFixture.cs`, `V5DatabaseFinisherFixture.cs`

**Interfaces:**
- **Consumes:** `SetupStepException` (Task 4); `LegacyMigrationState` (Task 6).
- **Produces:**
  - `public interface ISkyhookPreflight { void Check(); }`.
    - It sends a GET through `IWhisparrCloudRequestBuilder.WhisparrMetadata` to route `movie/changed`, built exactly as `SkyHookProxy.cs:248` builds it, with a since time of one hour ago and a 15 s timeout.
    - A non-2xx response or an exception throws `SetupStepException`, with `UserMessage` saying Skyhook didn't answer, plus the status or error.
  - `public interface IV5DatabaseFinisher { void Finish(IDbConnection v5, IDbTransaction tx, SceneMetadataType scene, MovieMetadataType movie, LegacyMigrationState state); }`. In one transaction it:
    - upserts the `Config` rows `whisparrscenemetadatasource`, `whisparrmoviemetadatasource` and `metadatasourceslocked` = `true`, with lowercase keys and values matching what `ConfigService.LockMetadataSources` stores;
    - updates `NamingConfig` to `RenameScenes = 0` and `RenameMovies = 0`;
    - inserts the state row.

- [ ] **Step 1: Write the failing tests:**
  - `preflight_should_pass_on_200`;
  - `preflight_should_throw_on_503`;
  - `preflight_should_throw_on_timeout`;
  - `finisher_should_lock_sources_and_disable_renaming`: read back with `ConfigService` and `NamingConfigService` against the same DB, and expect equal values;
  - `finisher_should_overwrite_stashdb_lock_from_301_with_choice`;
  - `finisher_should_write_state_row`.
- [ ] **Step 2: Run, expect a build failure.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run, expect pass.**
- [ ] **Step 5: Commit:** `New: Skyhook preflight and source lock for migration`.

### Task 8: Phase 1 orchestration

**Files:**
- Create:
  - `src/NzbDrone.Core/Datastore/Migration/Setup/SetupWizardService.cs`
  - `SetupProgress.cs`
  - `SetupChoice.cs`
- Modify:
  - `src/NzbDrone.Core/Datastore/Migration/Setup/LegacyInstallDetector.cs` and `LegacyInstall.cs` (pending env switch, below)
  - `src/NzbDrone.Host/Bootstrap.cs`, so that a pending env switch also enters setup mode
- Test: `src/NzbDrone.Core.Test/Datastore/Migration/Setup/SetupWizardServiceFixture.cs`, `ErosSameSourceEndToEndFixture.cs`, plus new cases in `LegacyInstallDetectorFixture.cs`

**Interfaces:**
- **Consumes:** every earlier task's interfaces, including `ISetupRunState` and `ISetupModeState.SeededConfig5` (Task 3), and `ILifecycleService.Restart()`.
- **Produces:**
  - `public class SetupChoice { LegacyKind Kind; SceneMetadataType SceneSource; MovieMetadataType MovieSource; }`.
  - `public enum SetupStep { Backup, SeedConfig, CreateDatabase, Migrate, MixedMovies, SkyhookPreflight, LockSources, Finish }`.
  - `public enum SetupStatus { Idle, Running, Failed, Succeeded, Restarting }`.
  - `public class SetupProgress`:
    - `SetupStatus Status`;
    - `List<(SetupStep Step, string State)> Steps`, where `State` is `pending`, `running`, `done`, `failed` or `skipped`;
    - `string BackupPath`;
    - `string ErrorMessage`;
    - `string ErrorKey`: `SetupNotAvailableYet`, `PostgresCreateFailed`, `SkyhookUnavailable`, `BackupFailed` or `Unexpected`;
    - `bool CanRetry`;
    - `List<string> EnvVarsToChange`.
  - `public interface ISetupWizardService { SetupProgress Progress { get; } void Start(SetupChoice choice); void Retry(); void ShutDown(); }`. `Start` returns immediately and runs on a background task. It is ignored while `Running`.
- **Rules:**
  - `SetupNotAvailableYet` is the error, with `CanRetry = false`, when `choice.Kind == V2` or `choice.SceneSource != install.PreviousSceneSource`.
  - **Step order:**
    1. `Backup`. On failure, stop. Nothing else has run, and no sidecar exists yet.
    2. `Begin(ISetupModeState.SeededConfig5)` the sidecar, then `SeedConfig`: re-seed `config5.xml` from the chosen install.
    3. `CreateDatabase` and `Migrate` (Task 5), with `recordCreated` going to `RecordDatabase`.
    4. `MixedMovies`, run when the chosen movie source differs from a movie row's source.
    5. `SkyhookPreflight`, skipped when `Detached == 0`.
    6. `LockSources`, then `Finish`. The state row has `Phase` = `"Phase2Pending"` and `Completed = false` when `Detached > 0`; otherwise `Phase` = `"Done"` and `Completed = true`, written in the same transaction (spec).
    7. Rename `whisparr5.db.partial` to `whisparr5.db`.
    8. `Complete()` the sidecar.
    9. `Status = Restarting`, then `ILifecycleService.Restart()`.
  - **Failure handling:**
    - **Any failure after `Begin`:** discard everything the sidecar records, leave the legacy data alone, and set `Failed` with the `SetupStepException.UserMessage`.
    - **`PostgresCreateFailed` and `SkyhookUnavailable`** have `CanRetry = true`. `Retry` reruns from `Backup` with a new zip.
  - **`EnvVarsToChange`** is `V5DatabaseTargets.EnvVarsStillNamingLegacy`, verbatim. When it's non-empty:
    - step 9 doesn't restart;
    - `Status` becomes `Succeeded`;
    - the progress also carries `EnvTargets`, a list of `{ Name, Value }` pairs giving the value each variable needs: the v5 DB name, or for a connection-string variable, the connection string with `Database=` replaced;
    - the page shows the instructions and a Shut down button.

    When it's empty, step 9 restarts as before.
  - **Pending env switch (detection):**
    - `DetectionResult` gains `bool PendingEnvSwitch` and `List<EnvTarget> EnvTargets`.
    - When `config5.xml` exists, the detector probes the v5 main DB under the `config5.xml` names, ignoring env vars.
    - If that DB is marked and holds a `LegacyMigrationState` row while an env var still names the legacy DB:
      - `PendingEnvSwitch` is true;
      - `SetupModeRequired` is true;
      - `EnvTargets` is filled;
      - `Installs` stays empty, so the migration isn't offered.
    - Detection stays read-only throughout.
    - Bootstrap enters setup mode for it like any other setup case.
  - **`ShutDown`** logs `"Whisparr v5 setup declined. To go back, reinstall or switch to your previous Whisparr version; your data was not changed."`, then calls `ILifecycleService.Shutdown()`.

- [ ] **Step 1: Write the failing tests:**
  - `SetupWizardServiceFixture` (mocks):
    - `should_run_steps_in_order`;
    - `backup_failure_should_stop_before_sidecar`;
    - `failure_after_begin_should_discard`;
    - `should_skip_preflight_without_detached_movies`;
    - `should_mark_completed_without_detached_movies`;
    - `should_leave_incomplete_with_detached_movies`;
    - `should_refuse_v2_and_scene_source_change_as_not_available_yet`;
    - `should_list_env_vars_to_change`;
    - `should_not_restart_when_env_still_names_legacy`, which also checks `EnvTargets` values for a `MainDb` variable and a connection-string variable;
    - `retry_should_rerun_from_backup`.
  - `ErosSameSourceEndToEndFixture` (real services, SQLite, Skyhook and lifecycle mocked):
    - Build an Eros DB with scenes, movies and files.
    - Run `Start` with StashDB plus the previous movie source, and wait for `Restarting`.
    - Assert:
      - `whisparr5.db` exists with the marker, and its counts equal the legacy ones;
      - `config5.xml` has `PostgresMainDb = whisparr5-main`;
      - the state is `Completed`;
      - the sources are locked to the choice;
      - renaming is disabled;
      - the backup zip exists;
      - the legacy DB and `config.xml` SHA-256 values are unchanged;
      - there's no sidecar;
      - `LegacyInstallDetector.Detect().SetupModeRequired` is false.
  - `LegacyInstallDetectorFixture`, Postgres-only:
    - `should_flag_pending_env_switch_when_completed_v5_db_and_env_names_legacy`: no installs, and `EnvTargets` is filled;
    - `should_not_flag_pending_env_switch_without_migration_state_row`.
- [ ] **Step 2: Run, expect a build failure.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run, expect pass. Run the Core project in full.**
- [ ] **Step 5: Commit:** `New: Run migration phase 1 from setup mode`.

### Task 9: Setup API

**Files:**
- Create:
  - `src/Whisparr.Http/Setup/SetupController.cs`
  - `src/Whisparr.Http/Setup/SetupStatusResource.cs`
  - `src/Whisparr.Http/Setup/SetupChoiceResource.cs`
- Test: `src/NzbDrone.Api.Test/Setup/SetupControllerFixture.cs`

**Interfaces:**
- **Consumes:** `ISetupModeState` (Task 3); `ISetupWizardService`, `SetupProgress` and `SetupChoice` (Task 8).
- **Produces** routes on `[Route("api/setup")]`, an `[ApiController]` outside the versioned API, using the normal fallback auth policy:
  - `GET status` returns `SetupStatusResource`:
    - `installs[]`, each with `kind`, `databaseType`, `refusal`, `studios`, `scenes`, `movies`, `files`, `previousSceneSource`, `previousMovieSource`, `hasMixedMovies` and `namedByEnvironment`;
    - `errors[]`, from `DetectionResult.Errors`;
    - `pendingEnvSwitch` and `envTargets[]` (`{ name, value }`), from detection. Before migrating, `envTargets` is also filled when any install has `namedByEnvironment`, using the targets `Config5Seeder` would pick, so the page can show the values up front;
    - `defaults`, with `kind`, `sceneSource` and `movieSource`;
    - `progress`, which is `SetupProgress` in camelCase.
  - `POST migrate` takes `{ kind, sceneSource, movieSource }`. It returns 202, or 409 when it's running or the chosen install has a refusal.
  - `POST retry` returns 202.
  - `POST shutdown` returns 202.
  - Every route returns 404 when `ISetupModeState.IsActive` is false.
- **Defaults (spec):**
  - **One install:** its kind. **Both:** Eros.
  - **v2:** TPDB/TPDB.
  - **Eros:** StashDB, plus `PreviousMovieSource`, with `NONE` → TMDB.

- [ ] **Step 1: Write the failing tests:**
  - `status_should_return_defaults_for_eros`;
  - `status_should_default_none_movie_source_to_tmdb`;
  - `status_should_default_to_eros_when_both`;
  - `migrate_should_start_wizard`;
  - `migrate_should_409_for_refused_install`;
  - `should_404_outside_setup_mode`;
  - `status_should_expose_errors_and_env_targets`;
  - `migrate_should_409_when_pending_env_switch`.
- [ ] **Step 2: Run, expect a build failure.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the Api project in full, expect pass.**
- [ ] **Step 5: Commit:** `New: Setup API for the migration wizard`.

### Task 10: Halt page (frontend) and live check

**Files:**
- Create:
  - `frontend/src/Setup/SetupApp.tsx`
  - `SetupSummary.tsx`
  - `SetupSourceForm.tsx`
  - `SetupProgress.tsx`
  - `SetupCarriesOver.tsx`
  - `useSetupStatus.ts`
  - matching `.module.css` files
- Modify:
  - `frontend/src/bootstrap.tsx`: render `<SetupApp />` when `window.Whisparr.setupMode`;
  - the `window.Whisparr` typing;
  - `src/NzbDrone.Core/Localization/Core/en.json`.

**Interfaces:**
- **Consumes:** the Task 9 routes. Use `fetch` with `X-Api-Key` from `window.Whisparr.apiKey` and `urlBase`. Don't use the app's query client, which assumes `/api/v5`.
- **Layout (spec, option B, one screen):**
  - **Header:** "Migration needed".
  - **Summary:**
    - found app, DB type and counts;
    - three statements: the legacy data is backed up first and never changed; nothing on disk moves; **renaming is disabled**;
    - with both installs, a v2/Eros choice that drives the defaults;
    - a refusal replaces the form with its message: `SetupRefusedV2TooOld` "Update Whisparr v2 to its latest version first, then start v5 again.", or `SetupRefusedErosNewer` "This Eros database is newer than this v5 build. Update v5 first.".
  - **"What carries over?"** expands a table built from the spec's Data disposition table, for the detected kind. It also holds:
    - the v2 note to finish or clear the download queue first;
    - the reset steps (stop the app, delete `whisparr5.db` and `config5.xml`, or drop the v5 Postgres DBs, then start again);
    - the downgrade steps (stop the app, go back to the old version, possibly purge `MediaCover`).
  - **Source form:**
    - a scene select (StashDB/TPDB) and a movie select (TMDB/TPDB);
    - the previous source is labelled "Your previous source; fastest, most faithful", and any other choice shows "Rebuilds from your files; much slower";
    - TPDB for scenes with TMDB for movies shows "TPDB for movies is strongly recommended with TPDB scenes";
    - the line "This choice can't be changed later without starting over".
  - **Buttons:** "Back up & migrate" and "Shut down".
  - **Detection errors:** shown in place of the form, with no migrate button.
  - **Env vars, before migrating:** when `envTargets` is non-empty, a notice lists each variable and its new value. It says that setting them before migrating works too.
  - **Pending env switch:** the whole page is replaced by "Your v5 database is ready", the variable list, "Change these variables, then recreate or restart the container", and a Shut down button.
  - **While running:** a step list with states, polling `status` every 1 s. On `Failed`, show the message plus Retry when `canRetry`. On `Succeeded` with env vars to change, show the same "ready, change these variables" screen as a pending env switch. On `Restarting`, poll `{urlBase}/initialize.json` every 2 s until `setupMode` is false, and reload.
- **Copy:** every string above is an en.json key. Name them `Setup*`.

- [ ] **Step 1: Implement.**
- [ ] **Step 2: Verify:** `yarn typecheck`, `yarn lint` and `yarn build`, each exiting 0.
- [ ] **Step 3: Live check on the 6969 dev instance (data is expendable):**
  1. Stop the running instance.
  2. Prepare a fresh AppData holding a copy of an Eros `whisparr3.db` plus its `config.xml`. Use one built with `LegacyDbBuilder` and some rows if no real copy is at hand.
  3. Publish, rsync the UI into both `_output/net10.0/osx-arm64/UI` and `publish/UI`, and start with `-data=<that folder>`.
  4. Check:
     - the halt page shows the counts;
     - `curl` of `/api/v5/system/status` gets 503;
     - sign-in works with the legacy user;
     - "Back up & migrate" runs every step, restarts, and lands in the normal app with the library present;
     - the source settings are locked and renaming is disabled;
     - the zip is in `Backups/pre-v5/`;
     - the legacy SHA-256 values are unchanged.
  5. Repeat once against Postgres (the throwaway container from Global Constraints).
  6. Take a screenshot of the halt page with SFW mode on, and save it to the session scratchpad.
- [ ] **Step 4: Commit:** `New: Migration needed page`.
