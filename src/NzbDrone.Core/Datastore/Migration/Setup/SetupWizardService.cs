using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NLog;
using Npgsql;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Core.LegacyMigration;
using NzbDrone.Core.Lifecycle;

namespace NzbDrone.Core.Datastore.Migration.Setup
{
    public interface ISetupWizardService
    {
        SetupProgress Progress { get; }

        // Returns immediately and runs on a background task. Ignored while a run is in progress.
        void Start(SetupChoice choice);

        // Runs the last choice again from the backup, after a failure that CanRetry.
        void Retry();

        void ShutDown();
    }

    // Runs migration phase 1 from setup mode: back up, build the v5 database from the chosen legacy one, lock the
    // metadata sources, and restart into v5. Legacy data is only ever read.
    public class SetupWizardService : ISetupWizardService
    {
        private const string V5SqliteFile = "whisparr5.db";
        private const string Phase2Pending = "Phase2Pending";
        private const string PhaseDone = "Done";

        private readonly ISetupModeState _setupModeState;
        private readonly IPreV5BackupService _backupService;
        private readonly IConfig5Seeder _config5Seeder;
        private readonly ISetupRunState _runState;
        private readonly IV5DatabaseBuilder _databaseBuilder;
        private readonly IMixedMovieResolver _mixedMovieResolver;
        private readonly ISkyhookPreflight _skyhookPreflight;
        private readonly IV5DatabaseFinisher _databaseFinisher;
        private readonly ILifecycleService _lifecycleService;
        private readonly IAppFolderInfo _appFolderInfo;
        private readonly IOptions<PostgresOptions> _postgresOptions;
        private readonly Logger _logger;

        private readonly object _sync = new();
        private SetupProgress _progress = NewProgress();
        private SetupChoice _lastChoice;
        private bool _committed;

        public SetupWizardService(
            ISetupModeState setupModeState,
            IPreV5BackupService backupService,
            IConfig5Seeder config5Seeder,
            ISetupRunState runState,
            IV5DatabaseBuilder databaseBuilder,
            IMixedMovieResolver mixedMovieResolver,
            ISkyhookPreflight skyhookPreflight,
            IV5DatabaseFinisher databaseFinisher,
            ILifecycleService lifecycleService,
            IAppFolderInfo appFolderInfo,
            IOptions<PostgresOptions> postgresOptions)
        {
            _setupModeState = setupModeState;
            _backupService = backupService;
            _config5Seeder = config5Seeder;
            _runState = runState;
            _databaseBuilder = databaseBuilder;
            _mixedMovieResolver = mixedMovieResolver;
            _skyhookPreflight = skyhookPreflight;
            _databaseFinisher = databaseFinisher;
            _lifecycleService = lifecycleService;
            _appFolderInfo = appFolderInfo;
            _postgresOptions = postgresOptions;
            _logger = NzbDroneLogger.GetLogger(this);

            var detection = setupModeState.Detection;

            if (detection.PendingEnvSwitch)
            {
                // The migration is already done. The page only has the environment instructions left to show.
                _progress.Status = SetupStatus.Succeeded;
                _progress.Steps = _progress.Steps.Select(s => (s.Step, SetupStepState.Done)).ToList();
                _progress.EnvVarsToChange = detection.EnvTargets.Select(t => t.Name).ToList();
                _progress.EnvTargets = detection.EnvTargets.ToList();
            }
        }

        public SetupProgress Progress
        {
            get
            {
                lock (_sync)
                {
                    return Clone(_progress);
                }
            }
        }

        public void Start(SetupChoice choice)
        {
            LegacyInstall install;

            lock (_sync)
            {
                // Succeeded and Restarting are final, and a pending env switch starts out as Succeeded.
                // After a post-commit failure the v5 database is in place, so a second run would only undo it.
                if (_committed || _progress.Status is not (SetupStatus.Idle or SetupStatus.Failed))
                {
                    return;
                }

                _lastChoice = choice;
                _progress = NewProgress();

                var refusal = Validate(choice, out install);

                if (refusal != null)
                {
                    // Nothing has started, so the form stays usable with the error above it.
                    _progress.Status = SetupStatus.Idle;
                    _progress.ErrorKey = refusal.Value.Key;
                    _progress.ErrorMessage = refusal.Value.Message;
                    _logger.Warn("Setup refused: {0}", refusal.Value.Message);

                    return;
                }

                _progress.Status = SetupStatus.Running;
            }

            Task.Factory.StartNew(() => Run(choice, install), TaskCreationOptions.LongRunning);
        }

        public void Retry()
        {
            SetupChoice choice;

            lock (_sync)
            {
                if (_committed || _progress.Status != SetupStatus.Failed || !_progress.CanRetry || _lastChoice == null)
                {
                    return;
                }

                choice = _lastChoice;
            }

            Start(choice);
        }

        public void ShutDown()
        {
            _logger.Info("Whisparr v5 setup declined. To go back, reinstall or switch to your previous Whisparr version; your data was not changed.");
            _lifecycleService.Shutdown();
        }

        private (string Key, string Message)? Validate(SetupChoice choice, out LegacyInstall install)
        {
            install = null;

            if (choice == null)
            {
                return (SetupErrorKey.Unexpected, "No choice was made.");
            }

            install = _setupModeState.Detection.Installs.FirstOrDefault(i => i.Kind == choice.Kind);

            if (choice.Kind == LegacyKind.V2 || (install != null && choice.SceneSource != install.PreviousSceneSource))
            {
                return (SetupErrorKey.SetupNotAvailableYet, "Migrating this install, or changing its scene source, isn't available yet. Your existing data has not been changed.");
            }

            if (install == null)
            {
                return (SetupErrorKey.Unexpected, "No existing install of that kind was found.");
            }

            if (install.Refusal != LegacyRefusal.None)
            {
                return (SetupErrorKey.Unexpected, $"This install can't be migrated ({install.Refusal}). Your existing data has not been changed.");
            }

            return null;
        }

        private void Run(SetupChoice choice, LegacyInstall install)
        {
            var current = SetupStep.Backup;
            var sidecarBegun = false;
            var committed = false;
            var startedAt = DateTime.UtcNow;

            try
            {
                SetStep(current, SetupStepState.Running);

                var backupPath = _backupService.Backup(install, _setupModeState.Detection.LegacyConfigPath);

                lock (_sync)
                {
                    _progress.BackupPath = backupPath;
                }

                SetStep(current, SetupStepState.Done);

                _runState.Begin(_setupModeState.SeededConfig5);
                sidecarBegun = true;

                current = SetupStep.SeedConfig;
                SetStep(current, SetupStepState.Running);

                var config5Path = _appFolderInfo.GetConfigPath();

                if (!_setupModeState.SeededConfig5 && File.Exists(config5Path))
                {
                    // The user's own file is about to be overwritten, so keep it until the run is complete.
                    var copy = config5Path + ".pre-v5-setup";

                    // A retry after a failed restore finds the half-restored file here, and the copy is the real original.
                    if (!File.Exists(copy))
                    {
                        File.Copy(config5Path, copy);
                    }

                    _runState.RecordConfig5Backup(copy);
                }

                var targets = _config5Seeder.Seed(_setupModeState.Detection.LegacyConfigPath, config5Path, install);

                SetStep(current, SetupStepState.Done);

                current = SetupStep.CreateDatabase;
                SetStep(current, SetupStepState.Running);

                if (install.Database.Type == DatabaseType.SQLite)
                {
                    // Recorded before it exists, so a crash mid-build still has it cleaned up.
                    _runState.RecordSqlitePartial(Path.Combine(Path.GetDirectoryName(install.Database.SqlitePath), V5DatabaseBuilder.PartialFileName));
                }

                var built = _databaseBuilder.CopyEros(install, targets, _runState.RecordDatabase, _runState.RecordReusedEmptyTarget);

                SetStep(current, SetupStepState.Done);
                SetStep(SetupStep.Migrate, SetupStepState.Done);

                current = SetupStep.MixedMovies;
                SetStep(current, SetupStepState.Running);

                var mixed = ResolveMixedMovies(built, choice);

                SetStep(current, SetupStepState.Done);

                current = SetupStep.SkyhookPreflight;

                if (!mixed.NeedsPhase2)
                {
                    SetStep(current, SetupStepState.Skipped);
                }
                else
                {
                    SetStep(current, SetupStepState.Running);
                    _skyhookPreflight.Check();
                    SetStep(current, SetupStepState.Done);
                }

                current = SetupStep.LockSources;
                SetStep(current, SetupStepState.Running);
                SetStep(SetupStep.Finish, SetupStepState.Running);

                FinishDatabase(built, choice, install, mixed, backupPath, startedAt);

                SetStep(current, SetupStepState.Done);
                current = SetupStep.Finish;

                if (built.SqlitePartialPath != null)
                {
                    PutSqliteDatabaseInPlace(built.SqlitePartialPath);
                }

                committed = true;
                SetStep(current, SetupStepState.Done);

                CompleteRun(targets);
            }
            catch (Exception ex) when (!committed)
            {
                Fail(ex, current, install, sidecarBegun);
            }
            catch (Exception ex)
            {
                // The v5 database is in place, so nothing is undone, but the page must not keep spinning.
                _logger.Error(ex, "Setup finished the migration but could not complete its last steps");

                lock (_sync)
                {
                    _committed = true;
                    _progress.Status = SetupStatus.Failed;
                    _progress.ErrorKey = SetupErrorKey.Unexpected;
                    _progress.CanRetry = false;
                    _progress.ErrorMessage = "The v5 database was built, but setup could not finish its last steps. Restart Whisparr to continue. " + ex.Message;
                }
            }
        }

        private MixedMovieResult ResolveMixedMovies(BuiltDatabases built, SetupChoice choice)
        {
            using var connection = OpenMain(built);
            using var transaction = connection.BeginTransaction();

            var result = _mixedMovieResolver.Resolve(connection, transaction, choice.MovieSource);

            transaction.Commit();

            foreach (var path in result.SkippedPaths)
            {
                _logger.Warn("Left the movie at {0} untouched, it is outside every root folder", path);
            }

            lock (_sync)
            {
                _progress.SkippedMovies = result.Skipped;
                _progress.SkippedMoviePaths = result.SkippedPaths.ToList();
            }

            return result;
        }

        // One transaction: the locked sources, the renaming switch and the state row land together or not at all.
        private void FinishDatabase(BuiltDatabases built, SetupChoice choice, LegacyInstall install, MixedMovieResult mixed, string backupPath, DateTime startedAt)
        {
            var detached = mixed.NeedsPhase2;

            var state = new LegacyMigrationState
            {
                SourceKind = install.Kind,
                SceneSource = choice.SceneSource,
                MovieSource = choice.MovieSource,
                Phase = detached ? Phase2Pending : PhaseDone,
                Progress = "{}",
                Completed = !detached,
                StartedAt = startedAt,
                CompletedAt = detached ? null : DateTime.UtcNow,
                BackupPath = backupPath
            };

            using var connection = OpenMain(built);
            using var transaction = connection.BeginTransaction();

            _databaseFinisher.Finish(connection, transaction, choice.SceneSource, choice.MovieSource, state);

            transaction.Commit();
        }

        private static void PutSqliteDatabaseInPlace(string partialPath)
        {
            // The rename must not meet an open handle, pooled or not.
            SQLiteConnection.ClearAllPools();

            var final = Path.Combine(Path.GetDirectoryName(partialPath), V5SqliteFile);

            File.Move(partialPath, final);
        }

        private void CompleteRun(V5DatabaseTargets targets)
        {
            _runState.Complete();

            var envVars = targets.EnvVarsStillNamingLegacy ?? new List<string>();

            if (envVars.Count > 0)
            {
                // v5 can't start on its own databases until the user changes these, so a restart would come back here.
                var options = _postgresOptions.Value ?? PostgresOptions.GetOptions();

                lock (_sync)
                {
                    _progress.EnvVarsToChange = envVars.ToList();
                    _progress.EnvTargets = Config5Seeder.EnvTargetsFor(
                        envVars,
                        targets.MainDb,
                        targets.LogDb,
                        options.MainDbConnectionString,
                        options.LogDbConnectionString);
                    _progress.Status = SetupStatus.Succeeded;
                }

                return;
            }

            lock (_sync)
            {
                _progress.Status = SetupStatus.Restarting;
            }

            _lifecycleService.Restart();
        }

        private void Fail(Exception ex, SetupStep step, LegacyInstall install, bool sidecarBegun)
        {
            _logger.Error(ex, "Setup failed at {0}", step);

            if (sidecarBegun)
            {
                try
                {
                    _runState.DiscardIfPresent();
                }
                catch (Exception discardEx)
                {
                    _logger.Error(discardEx, "Setup could not undo everything it created, the rest is undone on the next start");
                }
            }

            var key = SetupErrorKey.Unexpected;

            if (step == SetupStep.Backup)
            {
                key = SetupErrorKey.BackupFailed;
            }
            else if (step is SetupStep.CreateDatabase or SetupStep.Migrate && install.Database.Type == DatabaseType.PostgreSQL)
            {
                key = SetupErrorKey.PostgresCreateFailed;
            }
            else if (step == SetupStep.SkyhookPreflight)
            {
                key = SetupErrorKey.SkyhookUnavailable;
            }

            lock (_sync)
            {
                // The current step, and any other step still running (LockSources and Finish run together).
                for (var i = 0; i < _progress.Steps.Count; i++)
                {
                    var (s, state) = _progress.Steps[i];

                    if (state == SetupStepState.Running || (s == step && state != SetupStepState.Done))
                    {
                        _progress.Steps[i] = (s, SetupStepState.Failed);
                    }
                }

                _progress.Status = SetupStatus.Failed;
                _progress.ErrorKey = key;
                _progress.ErrorMessage = ex is SetupStepException stepException
                    ? stepException.UserMessage
                    : $"Setup stopped unexpectedly: {ex.Message} Your existing data has not been changed.";
                _progress.CanRetry = key is SetupErrorKey.PostgresCreateFailed or SetupErrorKey.SkyhookUnavailable;
            }
        }

        private IDbConnection OpenMain(BuiltDatabases built)
        {
            IDbConnection connection;

            if (built.Type == DatabaseType.PostgreSQL)
            {
                connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(built.MainConnectionString) { Pooling = false, Enlist = false }.ConnectionString);
            }
            else
            {
                connection = new SQLiteConnection(built.MainConnectionString);
            }

            try
            {
                connection.Open();
            }
            catch
            {
                connection.Dispose();
                throw;
            }

            return connection;
        }

        private void SetStep(SetupStep step, string state)
        {
            lock (_sync)
            {
                var index = _progress.Steps.FindIndex(s => s.Step == step);

                _progress.Steps[index] = (step, state);
            }
        }

        private static SetupProgress NewProgress()
        {
            return new SetupProgress
            {
                Status = SetupStatus.Idle,
                Steps = Enum.GetValues<SetupStep>().Select(s => (s, SetupStepState.Pending)).ToList()
            };
        }

        private static SetupProgress Clone(SetupProgress source)
        {
            return new SetupProgress
            {
                Status = source.Status,
                Steps = source.Steps.ToList(),
                BackupPath = source.BackupPath,
                ErrorMessage = source.ErrorMessage,
                ErrorKey = source.ErrorKey,
                CanRetry = source.CanRetry,
                EnvVarsToChange = source.EnvVarsToChange.ToList(),
                EnvTargets = source.EnvTargets.Select(t => new EnvTarget { Name = t.Name, Value = t.Value }).ToList(),
                SkippedMovies = source.SkippedMovies,
                SkippedMoviePaths = source.SkippedMoviePaths.ToList()
            };
        }
    }
}
