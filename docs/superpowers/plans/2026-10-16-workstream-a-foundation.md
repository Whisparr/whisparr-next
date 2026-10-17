# Workstream A: Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make whisparr-next a separate v5 app. It reports version 5, keeps its own config, DB, log DB and backups, refuses to run migrations on a legacy v2/v3 database, serves `/api/v5` next to a frozen `/api/v3`, and talks to Skyhook `/v5`.

**Architecture:**
- Storage names change at the one place each is defined: `PathExtensions` constants, the `ConfigFileProvider` Postgres defaults, and the `BackupService` filename pattern. The legacy AppData rename is removed.
- A static `LegacyDatabaseGuard` runs inside `MigrationController.Migrate` before FluentMigrator. A database that has a `VersionInfo` table but no row ≥ 300 is legacy and refused. Migration 300 is the v5 marker.
- `Whisparr.Api.V5` is a namespace-swapped copy of `Whisparr.Api.V3`, routed by a new `V5ApiControllerAttribute`. This follows Sonarr's layout, including the `version` field on SignalR messages that lets the frontend ignore V3 broadcasts.

**Tech Stack:** .NET 10, FluentMigrator, Dapper, ASP.NET Core + Swashbuckle, SignalR, NUnit/Moq/FluentAssertions, React 19 (TypeScript).

**Spec:** `docs/v5-roadmap.md`, section "A. Foundation". Decisions 8 and 9, and the anti-goals, also apply.

## Global Constraints

- Work on branch `ws-a-foundation` off `develop` in `~/Git/whisparr-next`, with one commit per task. Open one PR into `develop` at the end. The repo has no CI, so run every test project locally (Core, Common, Api, Host, Mono) before the PR.
- Commit prefixes are `New:`, `Fixed:` and `Chore:`. Dev-facing changes use `Chore:`. No AI attribution in commits or the PR.
- Write `plz12345` and `~/` paths in any doc or PR text, never a real name or `/Users/...`.
- Storage names, verbatim from the spec:
  - `whisparr5.db` / `whisparr5.restore`;
  - `config5.xml`;
  - Postgres `whisparr5-main` / `whisparr5-log`.
  - The log DB is `logs5.db`. This is a plan decision: a v2 or Eros `logs.db` can't take v5 migrations.
- v5-only migrations start at **300**. Eros migrations 248–299 keep their own numbers when cherry-picked.
- The legacy DB (`whisparr2.db`, `whisparr3.db`, `nzbdrone.db`, `whisparr.db`, Postgres `whisparr-main`/`whisparr-log`) and `config.xml` are never opened for writing, moved or deleted.
- `/api/v3` keeps working unchanged, because Prowlarr and other integrations call it. V3 is frozen and gets bug fixes only.
- Skyhook base URL: `https://api.whisparr.com/v5/{route}`.
- Migration test fixtures follow `src/NzbDrone.Core.Test/Datastore/Migration/247_lowercase_tpdb_foreign_idsFixture.cs`.
- Out of scope here: setup mode and the wizard (G) and the `alpha` pre-release label. The label needs the updater's branch matching reworked, so it lands with CI and containers (H). G adds a bypass for the guard when it copies an Eros DB on purpose.

## Review Focus

1. **v5 starts in an AppData that holds a v2 or Eros install.** `whisparr2.db`/`whisparr3.db`/`nzbdrone.db` and `config.xml` must keep the same bytes, and v5 must start empty beside them. Today `AppFolderFactory` would rename `nzbdrone.db` into v5's DB, and on macOS it would mirror and then delete `~/.config/Whisparr`. Pinned in Task 2.
2. **The `Whisparr__Postgres__MainDb` or `__LogDb` env var still names the Eros/v2 database.** Startup must stop with a message that names the env var. It must not run v5 migrations on that database, and the message must not be swallowed by the generic "Error creating main database" wrapper. Pinned in Task 4. The tests run on Postgres too when `DbTest`'s Postgres env is set.
3. **Backups share the `Backups` folder with Eros.** v5's retention cleanup must not delete Eros backup zips, and restoring an Eros zip into v5 must be refused rather than migrated. Pinned in Task 3.
4. **A movie is edited in the UI.** The edit must show up exactly once (no double SignalR update from the V3 and V5 controllers), and `/api/v5/movie` must not serve a stale cached resource after Core changes the movie. Pinned in Tasks 7 and 8.
5. **Skyhook `/v5` isn't live yet.** Switching the app to `/v5` before Workstream B's proxy rule exists breaks every metadata call. Task 9 has a precondition check and doesn't start until it passes.

---

### Task 1: Version 5 and updater defaults

**Files:**
- Modify: `src/Directory.Build.props:87`
- Rename: `.github/workflows/build_v3.yml` → `.github/workflows/build_v5.yml` (`git mv`), then edit its triggers (`:5-12`) and env (`:28-32`)
- Modify: the live references to the old name. In `.github/workflows/sonarqube.yml`, the comments, the `--workflow build_v3.yml` coverage lookup at `:265`, and `VERSION_FALLBACK` at `:15`, which becomes `5.0.0`. Also the comments in `.github/workflows/codeql.yml:12` and `.github/actions/test/action.yml:31`, `AGENTS.md:52,150`, and `.claude/skills/review-eros/SKILL.md:114,142`. Historical records (`UPSTREAM_SYNC.md`, `.github/upstream/state.json`, `REDUX_MIGRATION.md`) stay as written.
- Modify: `src/NzbDrone.Core/Configuration/ConfigFileProvider.cs:205-218` (`GithubOwnerRepo`), `:288` (`Branch`)
- Modify: `AGENTS.md` ("What this is" section: repo name and default branch, plus the workflow name)
- Test: `src/NzbDrone.Common.Test/ConfigFileProviderTest.cs` (the existing fixture)

**Interfaces:**
- Produces: `BuildInfo.Version.Major == 5` for local builds.

- [ ] **Step 1: Write the failing tests**

```csharp
[Test]
public void should_default_github_owner_repo_to_whisparr_next()
{
    Subject.GithubOwnerRepo.Should().Be("whisparr/whisparr-next");
}

[Test]
public void should_default_branch_to_develop()
{
    Subject.Branch.Should().Be("develop");
}
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test src/NzbDrone.Common.Test/Whisparr.Common.Test.csproj --filter "FullyQualifiedName~ConfigFileProvider"`
Expected: FAIL. The actual values are `whisparr/whisparr-eros` and `eros`.

- [ ] **Step 3: Change the defaults**

- `GithubOwnerRepo` defaults to `"whisparr/whisparr-next"`.
- `Branch` defaults to `"develop"`.
- `<AssemblyVersion>` becomes `5.0.0.*`.
- In `build_v5.yml`:
  - `WHISPARR_MAJOR_VERSION: 5`;
  - `VERSION_FALLBACK: 5.0.0`;
  - the `push` and `pull_request` branch filters list `develop` only;
  - the `SEMVER_LABEL` check compares against `develop` instead of `eros-develop`.
- In `AGENTS.md`, change the repo to `Whisparr/whisparr-next` and the PR-target branch to `develop`.

- [ ] **Step 4: Run the tests and check the built version**

Run: the Step 2 command, then `dotnet build src/Whisparr.sln -p:Platform=Posix` and `dotnet test src/NzbDrone.Common.Test/Whisparr.Common.Test.csproj --filter "FullyQualifiedName~BuildInfoFixture"`
Expected: PASS. Then start `_output/net10.0/Whisparr` and check that its first log line reads `Version 5.0.0.`.

- [ ] **Step 5: Commit**

```bash
git commit -am "Chore: Version 5 and whisparr-next updater defaults"
```

### Task 2: Own storage names, no legacy rename

**Files:**
- Modify: `src/NzbDrone.Common/Extensions/PathExtensions.cs:14-17`
- Modify: `src/NzbDrone.Core/Configuration/ConfigFileProvider.cs:302-303`
- Modify: `src/NzbDrone.Common/EnvironmentInfo/AppFolderFactory.cs:103-160`. Remove `MigrateAppDataFolder` and `MoveSqliteDatabase` along with their call site; keep `RemovePidFile` only if something else still calls it.
- Modify: `src/NzbDrone.Test.Common/NzbDroneRunner.cs:187`, which becomes `config5.xml`.
- Test:
  - `src/NzbDrone.Common.Test/PathExtensionFixture.cs:302`
  - `src/NzbDrone.Core.Test/Datastore/ConnectionStringFactoryFixture.cs:29` and `:59`
  - Create `src/NzbDrone.Common.Test/EnvironmentInfo/AppFolderFactoryFixture.cs`

**Interfaces:**
- Produces:
  - `appFolderInfo.GetDatabase()` → `<AppData>/whisparr5.db`;
  - `GetDatabaseRestore()` → `whisparr5.restore`;
  - `GetLogDatabase()` → `logs5.db`;
  - `GetConfigPath()` → `config5.xml`;
  - `PostgresMainDb` / `PostgresLogDb` default to `whisparr5-main` / `whisparr5-log`.

- [ ] **Step 1: Write the failing tests**

```csharp
// PathExtensionFixture
[Test]
public void should_use_v5_storage_names()
{
    var info = GetIAppDirectoryInfo();
    info.GetConfigPath().Should().BeEquivalentTo(@"C:\Whisparr\config5.xml".AsOsAgnostic());
    info.GetDatabase().Should().BeEquivalentTo(@"C:\Whisparr\whisparr5.db".AsOsAgnostic());
    info.GetDatabaseRestore().Should().BeEquivalentTo(@"C:\Whisparr\whisparr5.restore".AsOsAgnostic());
    info.GetLogDatabase().Should().BeEquivalentTo(@"C:\Whisparr\logs5.db".AsOsAgnostic());
}

// AppFolderFactoryFixture (TestBase<AppFolderFactory>, real temp AppData via GetTempFilePath())
[TestCase("nzbdrone.db")]
[TestCase("whisparr.db")]
[TestCase("whisparr2.db")]
[TestCase("whisparr3.db")]
public void should_leave_legacy_database_untouched(string legacyName)
{
    // write legacyName and config.xml with known bytes into AppData, run Subject.Register()
    File.ReadAllBytes(legacyPath).Should().Equal(legacyBytes);
    File.ReadAllBytes(legacyConfigPath).Should().Equal(configBytes);
    File.Exists(Path.Combine(appData, "whisparr5.db")).Should().BeFalse();
}
```

Update the `ConnectionStringFactoryFixture` mocks and expectations from `whisparr-main` to `whisparr5-main`. Add a test that the unmocked `ConfigFileProvider.PostgresLogDb` default is `whisparr5-log`.

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test src/NzbDrone.Common.Test/Whisparr.Common.Test.csproj --filter "FullyQualifiedName~PathExtensionFixture|FullyQualifiedName~AppFolderFactoryFixture"`
Expected:
- `should_use_v5_storage_names` fails on `Config.xml`.
- The `nzbdrone.db` case fails because the file was moved.

- [ ] **Step 3: Change the constants and defaults, and delete the legacy rename**

Use exactly the values from Global Constraints.

- [ ] **Step 4: Run the tests to verify they pass**

Run: the Step 2 command, plus `dotnet test src/NzbDrone.Core.Test/Whisparr.Core.Test.csproj --filter "FullyQualifiedName~ConnectionStringFactory"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git commit -am "New: v5 keeps its own config, database and log database"
```

### Task 3: v5-only backups

**Files:**
- Modify: `src/NzbDrone.Core/Backup/BackupService.cs`:
  - `:42` (`BackupFileRegex`);
  - `:139-152` (restore name matching);
  - `:204` (the log line naming `config.xml`).
- Test: `src/NzbDrone.Core.Test/Backup/BackupServiceFixture.cs`

**Interfaces:**
- Produces: `BackupService.BackupFileRegex` matches only `whisparr_backup_v5.<…>_<date>.zip`.

- [ ] **Step 1: Write the failing tests**

```csharp
[TestCase("whisparr_backup_v5.0.0.12_2026.10.16_12.00.00.zip", true)]
[TestCase("whisparr_backup_v3.5.0.1234_2026.10.16_12.00.00.zip", false)]
[TestCase("whisparr_backup_v10.0.0.1_2026.10.16_12.00.00.zip", false)]
[TestCase("nzbdrone_backup_2016.01.01_00.00.00.zip", false)]
public void should_only_recognise_v5_backups(string fileName, bool expected)
{
    BackupService.BackupFileRegex.IsMatch(fileName).Should().Be(expected);
}

[Test]
public void should_not_delete_eros_backups_during_retention_cleanup() { /* old v3 + old v5 zip in scheduled folder; Backup(Scheduled); only the v5 one deleted */ }

[TestCase("whisparr3.db")]
[TestCase("whisparr2.db")]
[TestCase("whisparr.db")]
[TestCase("nzbdrone.db")]
public void should_refuse_to_restore_a_legacy_database(string databaseFileName)
{
    // zip extracts databaseFileName + config.xml
    Assert.Throws<RestoreBackupFailedException>(() => Subject.Restore("backup.zip"))
        .Message.Should().Contain("v5");
    // MoveFile to GetDatabaseRestore() / GetConfigPath() never called
}
```

Replace the existing `should_restore_database_from_backup_zip` TestCases with one case: `whisparr5.db` plus `config5.xml`.

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test src/NzbDrone.Core.Test/Whisparr.Core.Test.csproj --filter "FullyQualifiedName~BackupServiceFixture"`
Expected: FAIL.

- [ ] **Step 3: Implement**

- **Regex:** `whisparr_backup_v5\.[._0-9]+\.zip`, keeping `IgnoreCase` and `RegexDefaults.Timeout`.
- **Restore:**
  - Accept only `Path.GetFileName(GetDatabase())` and `Path.GetFileName(GetConfigPath())`.
  - Check the whole extracted file list before moving anything.
  - If it contains a legacy DB name or `config.xml`, throw `RestoreBackupFailedException(HttpStatusCode.BadRequest, "This backup is from Whisparr v2 or v3. v5 only restores v5 backups; legacy installs are migrated by the v5 setup wizard.")`.
  - Nothing is moved in that case.

- [ ] **Step 4: Run the tests to verify they pass**

Run: the Step 2 command.
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git commit -am "New: v5 backups ignore and refuse v2/v3 backups"
```

### Task 4: Legacy database guard and migration 300

**Files:**
- Create: `src/NzbDrone.Core/Datastore/Migration/Framework/LegacyDatabaseGuard.cs`
- Create: `src/NzbDrone.Core/Datastore/LegacyDatabaseException.cs`
- Create: `src/NzbDrone.Core/Datastore/Migration/300_whisparr_v5.cs`
- Modify: `src/NzbDrone.Core/Datastore/Migration/Framework/MigrationController.cs:29-66`
- Modify: `src/NzbDrone.Core/Datastore/DbFactory.cs:111-197`. Add `catch (LegacyDatabaseException) { throw; }` ahead of the other catches in `CreateMain` and `CreateLog`.
- Test:
  - `src/NzbDrone.Core.Test/Datastore/LegacyDatabaseGuardFixture.cs`: two fixture classes, Main and Log, through `DbTest`'s `MigrationType`.

**Interfaces:**
- Produces:
  - `public static class LegacyDatabaseGuard { public const long FirstV5Migration = 300; public static bool IsLegacy(IDbConnection connection, DatabaseType databaseType); }`
  - `public class LegacyDatabaseException : NzbDrone.Common.Exceptions.WhisparrStartupException`, with constructor `(string databaseName, MigrationType migrationType)`.
- Consumes: G will add a `MigrationContext` flag that skips the guard. It's not part of this task.

- [ ] **Step 1: Write the failing tests**

```csharp
[Test] public void should_not_flag_a_v5_database()                 // migrated by DbTest → IsLegacy == false
[Test] public void should_flag_a_database_without_v5_migrations()   // DELETE FROM "VersionInfo" WHERE "Version" >= 300 → true
[Test] public void should_not_flag_an_empty_database()              // DROP TABLE "VersionInfo" → false
[Test] public void should_refuse_to_migrate_a_legacy_database()
{
    // after the DELETE above: MigrationController.Migrate(<DbTest's connection string>, new MigrationContext(MigrationType), databaseType)
    var ex = Assert.Throws<LegacyDatabaseException>(...);
    ex.Message.Should().Contain(MigrationType == MigrationType.Main ? "Whisparr__Postgres__MainDb" : "Whisparr__Postgres__LogDb");
    // VersionInfo still has no row >= 300 (nothing was migrated)
}
```

If `DbTest` doesn't expose its connection string, expose it as a `protected` property.

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test src/NzbDrone.Core.Test/Whisparr.Core.Test.csproj --filter "FullyQualifiedName~LegacyDatabaseGuard"`
Expected: build failure, because `LegacyDatabaseGuard` isn't defined yet.

- [ ] **Step 3: Implement**

- **`IsLegacy`:**
  - Tests for the table with SQLite `SELECT name FROM sqlite_master WHERE type='table' AND name='VersionInfo'` or Postgres `SELECT to_regclass('public."VersionInfo"')`.
  - Then returns `NOT EXISTS (SELECT 1 FROM "VersionInfo" WHERE "Version" >= 300)`.
- **`MigrationController.Migrate`:**
  - When `migrationContext.DesiredVersion == null`, open a raw connection from `connectionString` (`SQLiteConnection` or `NpgsqlConnection`) and call `IsLegacy`. If it's true, throw `LegacyDatabaseException`.
  - The database name comes from the connection string builder: the `DataSource` file name for SQLite, `Database` for Postgres.
  - Migration tests always set `DesiredVersion`, so they aren't affected.
- **Migration 300:** `[Migration(300)] public class whisparr_v5 : NzbDroneMigrationBase`, with empty `MainDbUpgrade`/`LogDbUpgrade` and one comment: its `VersionInfo` row is the marker `LegacyDatabaseGuard` looks for.
- **Exception message**, exact copy:
  `"{databaseName} was created by Whisparr v2 or v3 and can't be used by v5, which keeps its own {main|log} database. If Whisparr__Postgres__{MainDb|LogDb} (or Postgres{MainDb|LogDb} in config5.xml) still names a legacy database, point it at a new, empty one. Legacy installs are migrated by the v5 setup wizard."`

- [ ] **Step 4: Run the tests to verify they pass, on both engines**

Run: the Step 2 command. Then run it again with the `DbTest` Postgres env vars set, the way CI does, against a local Postgres 16+.
Expected: PASS both times. Also run the whole `FullyQualifiedName~Datastore` filter to confirm the existing migration fixtures still pass.

- [ ] **Step 5: Commit**

```bash
git commit -am "New: Refuse to run v5 migrations on a v2/v3 database"
```

### Task 5: V5 route attribute and versioned SignalR messages

**Files:**
- Modify: `src/Whisparr.Http/VersionedApiControllerAttribute.cs`. Add `public int Version { get; set; }` (set in the constructor) and `V5ApiControllerAttribute`, mirroring `~/Git/Sonarr/src/Sonarr.Http/VersionedApiControllerAttribute.cs`.
- Modify: `src/NzbDrone.SignalR/SignalRMessage.cs`. Add `public int? Version { get; set; }`.
- Modify: `src/Whisparr.Http/REST/RestControllerWithSignalR.cs:18-34`, plus every `new SignalRMessage` in that file. Add `protected int? Version { get; }` set from the attribute and stamp it on each broadcast, as in `~/Git/Sonarr/src/Sonarr.Http/REST/RestControllerWithSignalR.cs:23-33`.
- Test: create `src/NzbDrone.Api.Test/Http/VersionedApiControllerAttributeFixture.cs`.

**Interfaces:**
- Produces:
  - `[V5ApiController]` / `[V5ApiController("resource")]` → route `api/v5/{resource}`, with `Version == 5`;
  - `SignalRMessage.Version` serialised as `version`;
  - V3 controllers broadcast `version: 3`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Test]
public void should_route_v5_controllers_under_api_v5()
{
    var attr = new V5ApiControllerAttribute("movie");
    attr.Template.Should().Be("api/v5/movie");
    attr.Version.Should().Be(5);
}

[Test]
public void should_stamp_controller_version_on_broadcasts()
{
    // a [V3ApiController] test controller deriving RestControllerWithSignalR, mocked IBroadcastSignalRMessage (IsConnected = true)
    // Handle(new ModelEvent<TModel>(model, ModelAction.Updated)) → BroadcastMessage called with m.Version == 3
}
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test src/NzbDrone.Api.Test/Whisparr.Api.Test.csproj --filter "FullyQualifiedName~VersionedApiControllerAttribute"`
Expected: build failure, because `V5ApiControllerAttribute` isn't defined yet.

- [ ] **Step 3: Implement it as described under Files**

- [ ] **Step 4: Run the tests to verify they pass**

Run: the Step 2 command.
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git commit -am "Chore: V5 route attribute and versioned SignalR messages"
```

### Task 6: Whisparr.Api.V5 project wired into the host

**Files:**
- Create: `src/Whisparr.Api.V5/`, a copy of `src/Whisparr.Api.V3/` without `bin/`, `obj/` and `Calendar/CalendarFeedController.cs`. The iCal feed stays at `feed/v3`; a copy would register the same route twice.
- Modify:
  - `src/Whisparr.sln`: add the project. Copy V3's `ProjectConfigurationPlatforms` lines (Posix/Windows) for the new GUID.
  - `src/NzbDrone.Host/Whisparr.Host.csproj:18`, `src/NzbDrone.Api.Test/Whisparr.Api.Test.csproj:11`: add the ProjectReference.
  - `src/NzbDrone.Host/Bootstrap.cs:40`: add `"Whisparr.Api.V5"` to `ASSEMBLIES`.
  - `src/NzbDrone.Host/Startup.cs`:
    - `:91`: `AddApplicationPart(typeof(Whisparr.Api.V5.System.SystemController).Assembly)`.
    - `:101-118`: add a `"v5"` SwaggerDoc (`Version = "5.0.0"`) and include `Whisparr.Api.V5.xml`.
    - Add a `DocInclusionPredicate` copied from `~/Git/Sonarr/src/NzbDrone.Host/Startup.cs:181-208`, covering `VersionedApiControllerAttribute` and `VersionedFeedControllerAttribute`. Add a `Version` property to the feed attribute if it doesn't already have one.
    - `:304`: add the endpoint `"/docs/v5/openapi.json", "Whisparr API v5"`, listed first.
    - `:325`: add `"/api/v5/command"`.
  - `src/NzbDrone.Host/OpenApi/OperationIds.cs:10`: strip any `^api/v\d+/` prefix.
  - `src/NzbDrone.Host/OpenApi/CommandResourceSchemaFilter.cs` and `BackupRestoreUploadOperationFilter.cs`: match the V5 `CommandResource`/`BackupController` as well as V3's.
  - `src/Whisparr.Http/ApiInfoController.cs`: `Current = "v5"`, `Deprecated = { "v3" }`, as Sonarr does.
  - `AGENTS.md`:
    - the API row and the "New API surface" bullet: new endpoints go in `Whisparr.Api.V5` with `[V5ApiController]`; V3 is frozen;
    - the `Whisparr.Api.V3` table row: add a `Whisparr.Api.V5` row.
- Test:
  - create `src/NzbDrone.Host.Test/OpenApi/OperationIdsFixture.cs`;
  - create `src/NzbDrone.Api.Test/v5/RoutingFixture.cs`.

**Interfaces:**
- Consumes: `V5ApiControllerAttribute` (Task 5).
- Produces:
  - Every V3 resource exists as `Whisparr.Api.V5.<Area>.<Name>Resource`. Task 7 uses `Whisparr.Api.V5.Movies.MovieResource`, `Whisparr.Api.V5.Performers.PerformerResource` and `Whisparr.Api.V5.Studios.StudioResource`.
  - `/api/v5/*` serves the same handlers as `/api/v3/*`.
  - `/docs/v5/openapi.json` and `/docs/v3/openapi.json` are separate.

- [ ] **Step 1: Write the failing tests**

```csharp
// OperationIdsFixture
[TestCase("GET", "api/v3/tag/{id}", "getTagById")]
[TestCase("GET", "api/v5/tag/{id}", "getTagById")]
[TestCase("POST", "api/v5/movie/import", "postMovieImport")]
public void should_build_the_same_id_for_each_api_version(string method, string path, string expected)
    => OperationIds.FromRoute(method, path).Should().Be(expected);

// RoutingFixture: reflect over typeof(Whisparr.Api.V5.System.SystemController).Assembly
[Test] public void every_v5_controller_routes_under_api_v5()  // each concrete Controller type has V5ApiControllerAttribute, none has V3ApiController or V3FeedController
[Test] public void every_v3_controller_has_a_v5_twin()        // same type Name set, minus CalendarFeedController
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test src/NzbDrone.Host.Test/Whisparr.Host.Test.csproj --filter "FullyQualifiedName~OperationIds"` and `dotnet test src/NzbDrone.Api.Test/Whisparr.Api.Test.csproj --filter "FullyQualifiedName~v5"`
Expected: FAIL. The `api/v5/` id isn't stripped, and the V5 assembly doesn't exist yet.

- [ ] **Step 3: Create the project**

```bash
rsync -a --exclude bin --exclude obj src/Whisparr.Api.V3/ src/Whisparr.Api.V5/
git mv -f src/Whisparr.Api.V5/Whisparr.Api.V3.csproj src/Whisparr.Api.V5/Whisparr.Api.V5.csproj 2>/dev/null || mv src/Whisparr.Api.V5/Whisparr.Api.V3.csproj src/Whisparr.Api.V5/Whisparr.Api.V5.csproj
rm src/Whisparr.Api.V5/Calendar/CalendarFeedController.cs
grep -rl 'Whisparr.Api.V3\|V3ApiController' src/Whisparr.Api.V5 | xargs sed -i '' -e 's/Whisparr\.Api\.V3/Whisparr.Api.V5/g' -e 's/V3ApiController/V5ApiController/g'
```

Then make the wiring edits listed under Files.

- [ ] **Step 4: Run the tests and a full build**

Run: the Step 2 commands, then `dotnet build src/Whisparr.sln -p:Platform=Posix`.
Expected: PASS, and the build has 0 warnings (warnings are errors here).

- [ ] **Step 5: Smoke-test both API versions**

1. Start `_output/net10.0/Whisparr` on 6969.
2. Check these return 200 with the same body: `GET /api/v3/system/status` and `GET /api/v5/system/status`, both with `X-Api-Key`.
3. Check `GET /api` returns `{"current":"v5","deprecated":["v3"]}`.
4. Check `/docs/v5/openapi.json` contains no `/api/v3/` paths, and `/docs/v3/openapi.json` contains no `/api/v5/` paths.
5. Check `GET /feed/v3/calendar/Whisparr.ics?apikey=…` returns 200.

- [ ] **Step 6: Commit**

```bash
git add -A src/Whisparr.Api.V5 && git commit -am "New: Serve /api/v5 alongside a frozen /api/v3"
```

### Task 7: Core invalidates both API resource caches

**Files:**
- Create: `src/NzbDrone.Core/Cache/ApiResourceCaches.cs` (new folder, namespace `NzbDrone.Core.Cache`)
- Modify:
  - `src/NzbDrone.Core/Movies/MovieService.cs:121,1258-1265`
  - `src/NzbDrone.Core/Movies/MovieMetadataService.cs:37,104-111`
  - `src/NzbDrone.Core/Movies/Studios/StudioService.cs:55,187`
  - `src/NzbDrone.Core/Movies/Performers/PerformerService.cs:64,236`
  - `src/NzbDrone.Core/MediaFiles/MediaFileService.cs:54,234`
  - `src/NzbDrone.Core/Housekeeping/Housekeepers/AssignDefaultQualityProfiles.cs:40-45`, with one target row per cache name.
- Test:
  - `src/NzbDrone.Core.Test/Cache/ApiResourceCachesFixture.cs`
  - `src/NzbDrone.Api.Test/v5/ApiResourceCacheNamesFixture.cs`

**Interfaces:**
- Consumes: the V5 resource type names (Task 6).
- Produces:

```csharp
public static class ApiResourceCaches
{
    public static readonly string[] Movie;      // "Whisparr.Api.V3.Movies.MovieResource_movieResources", "Whisparr.Api.V5.Movies.MovieResource_movieResources"
    public static readonly string[] Performer;  // ...Performers.PerformerResource_performerResources (V3, V5)
    public static readonly string[] Studio;     // ...Studios.StudioResource_studioResources (V3, V5)
    public static void Remove(this ICacheManager cacheManager, IEnumerable<string> cacheNames, string cacheKey);
}
```

- [ ] **Step 1: Write the failing tests**

```csharp
// ApiResourceCachesFixture
[Test]
public void should_remove_key_from_every_existing_cache()
{
    // ICacheManager.FindCache returns a Mock<ICached> for Movie[0], null for Movie[1]
    Mocker.GetMock<ICacheManager>().Object.Remove(ApiResourceCaches.Movie, "42");
    // Movie[0] cache: Remove("42") once; no exception for the missing one
}

// ApiResourceCacheNamesFixture: guards against a resource rename silently orphaning a cache
[Test]
public void should_name_real_resource_caches()
{
    ApiResourceCaches.Movie.Should().Contain(typeof(Whisparr.Api.V5.Movies.MovieResource).FullName + "_movieResources")
        .And.Contain(typeof(Whisparr.Api.V3.Movies.MovieResource).FullName + "_movieResources");
    // same for Performer and Studio
}
```

The name format matches `CacheManager.GetCache<T>(Type, string)`, which builds it as `host.FullName + "_" + name`.

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test src/NzbDrone.Core.Test/Whisparr.Core.Test.csproj --filter "FullyQualifiedName~ApiResourceCaches"`
Expected: build failure, because `ApiResourceCaches` isn't defined yet.

- [ ] **Step 3: Implement it, then replace each `_cacheName` field and `FindCache` block with `_cacheManager.Remove(ApiResourceCaches.<X>, key)`**

- [ ] **Step 4: Run the tests to verify they pass**

Run: the Step 2 command, plus the Api.Test command filtered to `ApiResourceCacheNames`, plus `FullyQualifiedName~MovieService|FullyQualifiedName~StudioService|FullyQualifiedName~PerformerService|FullyQualifiedName~MediaFileService|FullyQualifiedName~AssignDefaultQualityProfiles`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git commit -am "Fixed: Core invalidates the v5 API resource caches"
```

### Task 8: Frontend on /api/v5

**Files:**
- Modify: `src/Whisparr.Http/Frontend/InitializeJsonController.cs:48`, changing `apiRoot` to `{_urlBase}/api/v5`.
- Modify: `frontend/src/Utilities/Fetch/getQueryPath.ts:1-3`, updating the comment to `/api/v5`.
- Modify: `frontend/src/Components/SignalRListener.tsx:44-47,355`:
  - add `version?: number` to `SignalRMessage`;
  - at the top of `handleMessage`, return early when `message.version !== undefined && message.version < 5`.
  - Unversioned messages, such as the hub's `version` message, still pass.
- Modify: the comments that say "Eros is on v3" in `frontend/src/Settings/UI/useUiSettings.ts:9`, `Settings/MediaManagement/useMediaManagementSettings.ts:5` and `Settings/MediaManagement/Naming/useNamingSettings.ts:10`. Rewrite them to state the v5 facts; don't change behaviour.

**Interfaces:**
- Consumes: `/api/v5/*` (Task 6), and SignalR `version` (Task 5).

- [ ] **Step 1: Make the edits listed under Files**

There's no frontend unit-test harness for `SignalRListener`, so the smoke test in Step 3 is the test.

- [ ] **Step 2: Verify the frontend**

Run: from the repo root, `yarn typecheck && yarn lint && yarn build`
Expected: `tsc` reports 0 errors, lint is clean and the build succeeds.

- [ ] **Step 3: Smoke-test the driven UI** on 6969 with SFW mode on. Rsync the UI into `_output/UI` first.

- In devtools Network, every XHR goes to `/api/v5/...` and none to `/api/v3/...`.
- Edit a movie's monitored flag. The console's `[signalR] received movie` log shows `v5`, and the V3 copy of the message is dropped. The row updates once.
- Reload. The movie still shows the new state, which shows the v5 resource cache was invalidated (Task 7).
- `/api/v3/movie/{id}` returns the same state.
- If anything you use in the UI fails, record the route and the response.

- [ ] **Step 4: Commit**

```bash
git commit -am "New: UI uses /api/v5"
```

### Task 9: Skyhook /v5 base URL

**Precondition:** Workstream B's proxy rule is live. Run
`curl -s -o /dev/null -w '%{http_code}' https://api.whisparr.com/v5/site/<any known stash studio id>`
It must print `200`, with the same body as the `/v4/` URL. Until it does, leave this task unstarted, or finish it on the branch without merging.

**Files:**
- Modify: `src/NzbDrone.Core/MetadataSource/SkyHook/SkyHookProxy.cs:63`
- Modify: `src/NzbDrone.Common/Cloud/WhisparrCloudRequestBuilder.cs:25`
- Modify: `src/NzbDrone.Core/Configuration/ConfigFileProvider.cs:188-202`
- Test: `src/NzbDrone.Common.Test/ConfigFileProviderTest.cs`

**Interfaces:**
- Produces: `ConfigFileProvider.WhisparrMetadata` returns `https://api.whisparr.com/v5/{route}` unless the saved value is a non-legacy override.

- [ ] **Step 1: Write the failing tests**

```csharp
[TestCase(null)]
[TestCase("")]
[TestCase("https://api.whisparr.com/v3/{route}")]
[TestCase("https://api.whisparr.com/v4/{route}")]
[TestCase("HTTPS://API.WHISPARR.COM/V4/{route}")]
public void should_use_v5_metadata_for_missing_or_legacy_values(string saved)
{
    // config5.xml WhisparrMetadata = saved
    Subject.WhisparrMetadata.Should().Be("https://api.whisparr.com/v5/{route}");
}

[Test]
public void should_keep_a_custom_metadata_override()
{
    // saved = "http://localhost:5000/{route}"
    Subject.WhisparrMetadata.Should().Be("http://localhost:5000/{route}");
}
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test src/NzbDrone.Common.Test/Whisparr.Common.Test.csproj --filter "FullyQualifiedName~ConfigFileProvider"`
Expected: FAIL. The default is still `/v4/`, and `/v4/` isn't rejected.

- [ ] **Step 3: Implement**

Set all three defaults to `https://api.whisparr.com/v5/{route}`. The guard treats the `/v3/` and `/v4/` URLs as the default, compared case-insensitively.

- [ ] **Step 4: Run the tests, then smoke-test**

Run: the Step 2 command.
Expected: PASS. Then on 6969, the log shows `Using WhisparrMetadata https://api.whisparr.com/v5/{route}`, and adding a studio and a scene works.

- [ ] **Step 5: Commit**

```bash
git commit -am "New: Use the Skyhook v5 endpoint"
```

---

## Finish

- [ ] Run every test project: Core, Common, Api, Host and Mono, without `--no-build` after a Posix sln build. All of them must pass.
- [ ] Do the Sonar pass on the files this branch touched, if the SonarQube MCP is connected. If it isn't, say so in the PR.
- [ ] Push `ws-a-foundation` and open a PR into `develop` using `.github/PULL_REQUEST_TEMPLATE.md`. The DB migration line is `YES - 300`.
