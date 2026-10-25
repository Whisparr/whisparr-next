# Migration wizard G-a: OIDC port and command progress, implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Port v2's OIDC login into v5, and give commands an optional progress value that System → Tasks shows as a bar, a percentage and an ETA. Both are prerequisites for the migration wizard.

**Architecture:**
- **OIDC** is a port of one upstream feature: Sonarr/Sonarr@9f0cb9284, which v2 took as Whisparr/Whisparr@8fe35a5f3 (`git -C ~/Git/Whisparrv2 show 8fe35a5f3`, or `git -C ~/Git/Sonarr show 9f0cb9284`). The patch doesn't apply mechanically, so it's ported file by file. The Sonarr version is the reference for `AuthOptions`, because v5 has `AuthOptions` and v2 doesn't. The v2 version is the reference for everything else. The settings API change goes to `Whisparr.Api.V5` only, because V3 is frozen.
- **Command progress** is a new `CommandProgress` value on `CommandModel`. It isn't persisted. It's set through a throttled reporter that finds the running command through `ProgressMessageContext`, and it's pushed with the existing `CommandUpdatedEvent`.

**Tech Stack:** C#/.NET 10, ASP.NET Core auth (`Microsoft.AspNetCore.Authentication.OpenIdConnect` 10.0.x), NUnit/Moq/FluentAssertions, React/TypeScript.

**Spec:** `docs/superpowers/specs/2026-10-24-migration-wizard-design.md` (Goals: OIDC; Decisions: "Progress in System → Tasks").

## Global Constraints

- **Edits:** use the Edit and Write tools only. Never sed, shell redirection or chained Bash. Run one plain command per Bash call, with absolute paths.
- **Commits** go on `develop` in `~/Git/whisparr-next`. Never push.
- **No AI attribution** in commits or code. Use the plz12345 handle and `~/` paths; never a real name.
- **Commit prefixes:** `New:`, `Fix:` or `Chore:`. Ported OIDC commits name their origin in the body: `Ported from Whisparr/Whisparr@8fe35a5f3 (Sonarr/Sonarr@9f0cb9284)`.
- **No infrastructure details** (hosts, paths, proxy layout) in the repo.
- **Forward only:** nothing goes back to Eros.
- **Tests:**
  - NUnit `*Fixture`, at least 80% coverage on new backend code.
  - Before the branch is handed over, every test project runs (Core, Common, Api, Host, Mono).
- **Frontend:** `yarn typecheck`, `yarn lint` and `yarn build` must pass, run from the repo root.
- **Strings:** user-facing strings go in `src/NzbDrone.Core/Localization/Core/en.json` (flat, PascalCase, alphabetical). Other languages are never edited.
- **Style:** match the surrounding code. Target-typed `new()` has no space. Mark props `Readonly<T>` on any frontend file you touch.
- **The V3 API is frozen.** Don't add OIDC fields to `Whisparr.Api.V3`.

## Review Focus

1. **OIDC chosen but incomplete or insecure** (a missing field, an `http://` authority, no `openid` scope). The effective method falls back to Forms, so nobody is locked out, and the health check names the problem. Tests: Task 1.
2. **Env vars override the config file.** `Whisparr__Auth__OidcAuthority` and its siblings, bound through `AuthOptions`, win over `config5.xml`. Tests: Task 1.
3. **The client secret is never echoed.** `GET` host config returns `********` or an empty string. A `PUT` that sends `********` back keeps the stored secret. Tests: Task 2.
4. **A V3 host-config `PUT` doesn't wipe the OIDC keys.** The V3 resource has no OIDC fields. Tests: Task 2.
5. **The progress reporter is called in a tight loop**, with total 0 or a percent outside 0–100. Updates are throttled, there's no divide-by-zero, and the percent is clamped. Tests: Task 4.

---

### Task 1: OIDC core (config, discovery, validation, health check)

**Files:**
- Modify: `src/NzbDrone.Common/Options/AuthOptions.cs`
- Modify: `src/NzbDrone.Core/Authentication/AuthenticationType.cs`
- Create: `src/NzbDrone.Core/Authentication/AuthenticationConfigurationExtensions.cs`
- Create: `src/NzbDrone.Core/Authentication/OidcDiscoveryService.cs`
- Modify: `src/NzbDrone.Core/Configuration/ConfigFileProvider.cs`
- Create: `src/NzbDrone.Core/Validation/OidcAuthorityValidator.cs`
- Create: `src/NzbDrone.Core/HealthCheck/Checks/OidcConfigurationCheck.cs`
- Modify: `src/NzbDrone.Core/HealthCheck/HealthCheck.cs` (the `HealthCheckReason` enum)
- Modify: `src/NzbDrone.Core/Localization/Core/en.json`
- Test: `src/NzbDrone.Core.Test/Authentication/AuthenticationConfigurationExtensionsFixture.cs`, `src/NzbDrone.Core.Test/Authentication/OidcDiscoveryServiceFixture.cs`, `src/NzbDrone.Core.Test/HealthCheck/Checks/OidcConfigurationCheckFixture.cs`, `src/NzbDrone.Core.Test/Configuration/ConfigFileProviderOidcFixture.cs`

**Interfaces:**
- **Produces:**
  - `AuthenticationType.Oidc = 4`.
  - `IConfigFileProvider.OidcAuthority`, `OidcClientId`, `OidcClientSecret`, `OidcUserIdentifier` and `OidcScopes` (all `string`). Each reads `_authOptions.X ?? GetValue("X", default, persist: false)`. The default is `string.Empty`, except `OidcScopes`, which defaults to `"openid profile email"`.
  - `AuthOptions.OidcAuthority`, `OidcClientId`, `OidcClientSecret`, `OidcUserIdentifier` and `OidcScopes` (all `string`).
  - Static `AuthenticationConfigurationExtensions`, with `IsOidcConfigured`, `HasOidcSettings`, `IsOidcAuthoritySecure`, `GetOidcScopes(string) -> string[]` and `EffectiveAuthenticationMethod(this IConfigFileProvider) -> AuthenticationType`.
  - `IOidcDiscoveryService.IsDiscoverable(string authority) -> bool`.
  - `OidcAuthorityValidator : PropertyValidator`.
  - `HealthCheckReason` values `OidcAuthorityNotSecure`, `OidcDiscoveryFailed` and `OidcNotConfigured`.
- **Source:**
  - Port the bodies verbatim from v2 `8fe35a5f3`.
  - `AuthOptions` and the `_authOptions ??` getters come from Sonarr `9f0cb9284`.
  - The 18 en.json keys are copied verbatim from the v2 patch and kept in alphabetical order.

- [ ] **Step 1: Write the failing tests**

`AuthenticationConfigurationExtensionsFixture` (mock `IConfigFileProvider`):
- `should_be_configured_with_all_settings_and_https_authority`: authority `https://id.example`, client `c`, secret `s`, user `u`, scopes `openid profile`. Assert `IsOidcConfigured()` is true.
- `should_not_be_configured_with_http_authority`: as above, but the authority is `http://id.example`. Assert false.
- `should_not_be_configured_without_openid_scope`: scopes `profile email`. Assert false.
- `effective_method_should_fall_back_to_forms_when_oidc_incomplete`: method `Oidc`, secret empty. Assert `EffectiveAuthenticationMethod()` is `Forms`.
- `effective_method_should_stay_oidc_when_configured`: assert `Oidc`.
- `effective_method_should_pass_through_non_oidc`: method `External`. Assert `External`.
- `get_scopes_should_split_on_spaces_and_commas`: `"openid, profile  email"` gives `["openid", "profile", "email"]`, and `null` gives an empty array.

`OidcDiscoveryServiceFixture` (mock `IHttpClient.Get<OidcDiscoveryDocument>`):
- `should_request_well_known_configuration`: assert the request URL ends `/.well-known/openid-configuration`.
- `should_be_discoverable_with_both_endpoints`: true.
- `should_not_be_discoverable_on_http_error`: response 404. False.
- `should_not_be_discoverable_without_token_endpoint`: false.
- `should_not_be_discoverable_when_request_throws`: false.
- `should_not_be_discoverable_for_blank_authority`: `" "` gives false, and `IHttpClient` is never called.

`OidcConfigurationCheckFixture`:
- `should_be_ok_when_method_is_not_oidc`.
- `should_error_not_configured_when_settings_missing`: reason `OidcNotConfigured`.
- `should_error_not_secure_for_http_authority`: reason `OidcAuthorityNotSecure`.
- `should_error_discovery_failed_when_not_discoverable`: reason `OidcDiscoveryFailed`.
- `should_be_ok_when_discoverable`.

`ConfigFileProviderOidcFixture`:
- `should_prefer_auth_options_over_config_file`: `AuthOptions.OidcAuthority = "https://env.example"` and config `https://file.example`. Assert the env value is returned.
- `should_default_scopes`: assert `"openid profile email"`.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test ~/Git/whisparr-next/src/NzbDrone.Core.Test/Whisparr.Core.Test.csproj --filter "FullyQualifiedName~Oidc|FullyQualifiedName~AuthenticationConfigurationExtensions"`

Expected: a build failure, because the types aren't defined yet.

- [ ] **Step 3: Implement the files listed above**, porting from the two upstream commits.

- [ ] **Step 4: Run the tests to verify they pass**

Run the same command. Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git -C ~/Git/whisparr-next add src/NzbDrone.Common/Options/AuthOptions.cs src/NzbDrone.Core src/NzbDrone.Core.Test
git -C ~/Git/whisparr-next commit -m "New: OIDC authentication settings, discovery and health check" -m "Ported from Whisparr/Whisparr@8fe35a5f3 (Sonarr/Sonarr@9f0cb9284)"
```

### Task 2: OIDC in the HTTP pipeline and the V5 host-config API

**Files:**
- Modify: `src/Whisparr.Http/Whisparr.Http.csproj` and `src/NzbDrone.Host/Whisparr.Host.csproj` (add `Microsoft.AspNetCore.Authentication.OpenIdConnect`, version `10.0.13` to match the other `Microsoft.*` packages)
- Modify: `src/Whisparr.Http/Authentication/AuthenticationBuilderExtensions.cs`
- Create: `src/Whisparr.Http/Authentication/OidcOptionsChangeTokenSource.cs`
- Modify: `src/Whisparr.Http/Authentication/AuthenticationController.cs` and `AuthenticationService.cs`
- Modify: `src/Whisparr.Http/Authentication/UiAuthorizationPolicyProvider.cs`
- Modify: `src/Whisparr.Http/Frontend/Mappers/IndexHtmlMapper.cs` and `LoginHtmlMapper.cs`
- Create: `src/Whisparr.Http/Frontend/Mappers/LogoutHtmlMapper.cs`
- Modify: `src/Whisparr.Http/Frontend/StaticResourceController.cs`
- Modify: `src/Whisparr.Api.V5/Config/HostConfigController.cs` and `HostConfigResource.cs`
- Test: `src/NzbDrone.Api.Test/Authentication/UiAuthorizationPolicyProviderFixture.cs`, `src/NzbDrone.Api.Test/v5/Config/HostConfigControllerOidcFixture.cs`, `src/NzbDrone.Api.Test/v3/Config/HostConfigControllerOidcFixture.cs`

**Interfaces:**
- **Consumes (Task 1):** `EffectiveAuthenticationMethod()`, `GetOidcScopes`, `OidcAuthorityValidator`, and the `IConfigFileProvider.Oidc*` properties.
- **Produces:**
  - `HostConfigResource` (V5) gains `OidcAuthority`, `OidcClientId`, `OidcClientSecret`, `OidcUserIdentifier` and `OidcScopes`.
  - The `/logout` page, through `LogoutHtmlMapper`.
  - When the effective method is `Oidc`, the UI policy authenticates against `CookieAuthenticationDefaults.AuthenticationScheme`.
- **Source:** port from v2 `8fe35a5f3`, mapping v2's `Whisparr.Api.V3/Config` changes onto `Whisparr.Api.V5/Config`. Keep `PrivateValue = "********"` and the validator rules exactly as in v2.

- [ ] **Step 1: Write the failing tests**

`UiAuthorizationPolicyProviderFixture`:
- `should_use_cookie_scheme_for_configured_oidc`: assert the policy's `AuthenticationSchemes` is `["Cookies"]`.
- `should_use_forms_scheme_when_oidc_falls_back`: assert `["Forms"]`.

`v5/Config/HostConfigControllerOidcFixture`:
- `get_should_mask_client_secret`: a stored secret `s3cret` comes back as `********`.
- `get_should_return_empty_when_no_secret`.
- `put_should_keep_stored_secret_when_placeholder_sent`: assert `SaveConfigDictionary` receives `OidcClientSecret = "s3cret"`.
- `put_should_reject_http_authority_for_oidc`: a validation error on `OidcAuthority`.
- `put_should_reject_scopes_without_openid`.

`v3/Config/HostConfigControllerOidcFixture`:
- `put_should_not_write_oidc_keys`: assert the dictionary passed to `SaveConfigDictionary` has no key starting `Oidc`.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test ~/Git/whisparr-next/src/NzbDrone.Api.Test/Whisparr.Api.Test.csproj --filter "FullyQualifiedName~Oidc"`

Expected: a build failure or failing tests.

- [ ] **Step 3: Implement the files listed above.**

- [ ] **Step 4: Run the tests to verify they pass.** Then run the whole `Whisparr.Api.Test` project, with no filter, to check that existing auth tests still pass.

- [ ] **Step 5: Commit**, with the message `New: OIDC sign-in and V5 host config settings` and the same "Ported from" body line.

### Task 3: OIDC frontend

**Files:**
- Create: `frontend/src/Settings/General/AuthenticationMethodSettings.tsx`, `FormsAuthenticationSettings.tsx` and `OidcAuthenticationSettings.tsx`
- Modify: `frontend/src/Settings/General/SecuritySettings.tsx` and `GeneralSettings.tsx`
- Modify: `frontend/src/FirstRun/AuthenticationRequiredModalContent.tsx`
- Modify: `frontend/src/Components/Page/Header/PageHeaderActionsMenu.tsx`
- Modify: `frontend/src/login.html`
- Create: `frontend/src/logout.html`
- Modify: the general-settings typing that matches v2's `frontend/src/typings/Settings/General.ts` (find the v5 equivalent with `rg -l "authenticationMethod" frontend/src/typings`)

**Interfaces:**
- **Consumes (Task 2):** the five `oidc*` fields on the V5 host-config resource.
- **Source:** port from v2 `8fe35a5f3`, adapting to v5's existing component and hook names wherever v5's Radarr-lineage files differ from v2's.

- [ ] **Step 1: Port the files**, keeping v5's existing imports and hooks.

- [ ] **Step 2: Verify**

Run each of these from `~/Git/whisparr-next`:
- `yarn typecheck`
- `yarn lint`
- `yarn build`

Expected: each exits 0, and `tsc` reports 0 errors.

- [ ] **Step 3: Live check on the dev instance**

Publish with `dotnet publish src/NzbDrone.Console/Whisparr.Console.csproj -c Debug -f net10.0 -r osx-arm64 --self-contained -o _output/net10.0/osx-arm64/publish`, then do the same for `src/NzbDrone.Mono/Whisparr.Mono.csproj`. Rsync `_output/UI` into place, and start on 6969.

Then check:
- Settings → General shows an OIDC option under the authentication method.
- Saving OIDC with an `http://` authority is rejected.
- Forms login still works.
- `/logout` serves a page.

Take screenshots with SFW mode on.

- [ ] **Step 4: Commit**, with the message `New: OIDC settings and sign-in pages` and the "Ported from" body line.

### Task 4: Command progress (backend)

**Files:**
- Create: `src/NzbDrone.Core/Messaging/Commands/CommandProgress.cs`
- Modify: `src/NzbDrone.Core/Messaging/Commands/CommandModel.cs`
- Modify: `src/NzbDrone.Core/Messaging/Commands/CommandQueueManager.cs` (interface and class)
- Modify: `src/NzbDrone.Core/Datastore/TableMapping.cs:166` (ignore `Progress`)
- Create: `src/NzbDrone.Core/ProgressMessaging/CommandProgressReporter.cs`
- Modify: `src/Whisparr.Api.V5/Commands/CommandResource.cs` (property and mapper)
- Test: `src/NzbDrone.Core.Test/ProgressMessaging/CommandProgressReporterFixture.cs`, `src/NzbDrone.Core.Test/Datastore/CommandProgressMappingFixture.cs`

**Interfaces:**
- **Produces:**
  - `public class CommandProgress`, with `string Phase`, `long Done`, `long Total` and `double Percent` (0–100, overall).
  - `CommandModel.Progress` (a `CommandProgress`, nullable). It isn't persisted: `TableMapping` uses `.Ignore(c => c.Progress)`.
  - `IManageCommandQueue.SetProgress(CommandModel command, CommandProgress progress)`.
  - `public interface IReportCommandProgress { void Report(string phase, long done, long total, double percent); }`, implemented by `CommandProgressReporter`.
  - `CommandResource.Progress` (V5 only), mapped from `model.Progress`.
- **Reporter rules:**
  - Read the running command from `ProgressMessageContext.CommandModel`. Do nothing when it's null or `!Body.SendUpdatesToClient`.
  - Clamp the percent to [0, 100], and turn NaN into 0.
  - Publish `CommandUpdatedEvent` only when one of these is true: the phase changed; `done == total`; at least 1 second has passed since the last publish for that command.
  - Always store the latest value on the model, even when no event is published.

- [ ] **Step 1: Write the failing tests**

`CommandProgressReporterFixture`. Use a `CommandModel` set as `ProgressMessageContext.CommandModel`, a mocked `IEventAggregator`, and an injectable clock or `Func<DateTime>`.
- `should_do_nothing_without_running_command`: the event is never published.
- `should_store_and_publish_first_report`: `Progress` equals `{Phase "Linking files", Done 1, Total 10, Percent 5}`, and one event is published.
- `should_throttle_reports_within_one_second`: 50 reports at the same instant give 1 event, and `Progress.Done` equals the last report's value.
- `should_publish_on_phase_change_within_throttle`.
- `should_publish_when_done_equals_total`.
- `should_clamp_percent`: −5 becomes 0, 150 becomes 100, and NaN becomes 0.
- `should_accept_zero_total`: no exception.

`CommandProgressMappingFixture` (`DbTest`):
- `should_save_and_load_command_without_progress_column`: insert a `CommandModel` with `Progress` set, read it back, and assert no exception and `Progress` is null.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test ~/Git/whisparr-next/src/NzbDrone.Core.Test/Whisparr.Core.Test.csproj --filter "FullyQualifiedName~CommandProgress"`

Expected: a build failure.

- [ ] **Step 3: Implement the files listed above.**

- [ ] **Step 4: Run the tests to verify they pass.** Then run `--filter "FullyQualifiedName~Messaging"` to check that existing command tests still pass.

- [ ] **Step 5: Commit**, with the message `New: Optional progress on long-running commands`.

### Task 5: Command progress in System → Tasks (frontend)

**Files:**
- Modify: `frontend/src/Commands/Command.ts` (add `progress?: CommandProgress`, with `{ phase: string; done: number; total: number; percent: number }`)
- Modify: `frontend/src/System/Tasks/Queued/QueuedTasks.tsx` (pass `progress` through)
- Modify: `frontend/src/System/Tasks/Queued/QueuedTaskRow.tsx` and `QueuedTaskRowNameCell.tsx` (with its `.module.css`)
- Modify: `src/NzbDrone.Core/Localization/Core/en.json`

**Interfaces:**
- **Consumes (Task 4):** `progress` on the V5 command resource.
- **Display rules:**
  - When `status === 'started'` and `progress` is set, the name cell shows `Components/ProgressBar` with `progress={percent}`.
  - Under the bar it shows the line `{phase}: {done} / {total} ({percent}%)`. The counts use thousands separators and the percent is rounded to 1 decimal.
  - An ETA follows only when at least 60 s have passed since `started` and `percent > 0`. Compute it as `eta = elapsed × (100 − percent) / percent`, format it with the existing `formatTimeSpan`, and show it with the `CommandProgressEta` string.
  - Rows without `progress` look exactly as they do today.
- **New en.json keys:**
  - `"CommandProgress": "{phase}: {done} / {total} ({percent}%)"`
  - `"CommandProgressEta": "About {eta} left"`

- [ ] **Step 1: Implement the frontend changes listed above.**

- [ ] **Step 2: Verify:** `yarn typecheck`, `yarn lint` and `yarn build` from the repo root. Each exits 0.

- [ ] **Step 3: Commit**, with the message `New: Show command progress in System > Tasks`.

The live check of this view happens in G-d, when the migration import first reports progress. Before G-d, no command reports progress.
