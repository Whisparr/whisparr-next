using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using DryIoc;
using DryIoc.Microsoft.DependencyInjection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using NLog;
using Npgsql;
using NzbDrone.Common.Composition.Extensions;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Exceptions;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Common.Options;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore.Extensions;
using NzbDrone.Core.Datastore.Migration.Setup;
using PostgresOptions = NzbDrone.Core.Datastore.PostgresOptions;

namespace NzbDrone.Host
{
    public static class Bootstrap
    {
        private static readonly Logger Logger = NzbDroneLogger.GetLogger(typeof(Bootstrap));

        public static readonly List<string> ASSEMBLIES = new List<string>
        {
            "Whisparr.Host",
            "Whisparr.Core",
            "Whisparr.SignalR",
            "Whisparr.Api.V3",
            "Whisparr.Api.V5",
            "Whisparr.Http"
        };

        public static void Start(string[] args, Action<IHostBuilder> trayCallback = null)
        {
            try
            {
                Logger.Info("Starting Whisparr - {0} - Version {1}",
                            Environment.ProcessPath,
                            Assembly.GetExecutingAssembly().GetName().Version);

                var startupContext = new StartupContext(args);

                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

                var appMode = GetApplicationMode(startupContext);
                var config = GetConfiguration(startupContext);

                switch (appMode)
                {
                    case ApplicationModes.Service:
                        {
                            Logger.Debug("Service selected");

                            CreateConsoleHostBuilder(args, startupContext).UseWindowsService().Build().Run();
                            break;
                        }

                    case ApplicationModes.Interactive:
                        {
                            Logger.Debug(trayCallback != null ? "Tray selected" : "Console selected");
                            var builder = CreateConsoleHostBuilder(args, startupContext);

                            if (trayCallback != null)
                            {
                                trayCallback(builder);
                            }

                            builder.Build().Run();
                            break;
                        }

                    // Utility mode
                    default:
                        {
                            new HostBuilder()
                                .UseServiceProviderFactory(new DryIocServiceProviderFactory(new Container(rules => rules.WithNzbDroneRules())))
                                .ConfigureContainer<IContainer>(c =>
                                {
                                    c.AutoAddServices(Bootstrap.ASSEMBLIES)
                                        .AddNzbDroneLogger()
                                        .AddDatabase()
                                        .AddStartupContext(startupContext)
                                        .Resolve<UtilityModeRouter>()
                                        .Route(appMode);

                                    if (config.GetValue(nameof(ConfigFileProvider.LogDbEnabled), true))
                                    {
                                        c.AddLogDatabase();
                                    }
                                    else
                                    {
                                        c.AddDummyLogDatabase();
                                    }
                                })
                                .ConfigureServices(services =>
                                {
                                    services.Configure<PostgresOptions>(config.GetSection("Whisparr:Postgres"));
                                    services.Configure<AppOptions>(config.GetSection("Whisparr:App"));
                                    services.Configure<AuthOptions>(config.GetSection("Whisparr:Auth"));
                                    services.Configure<ServerOptions>(config.GetSection("Whisparr:Server"));
                                    services.Configure<LogOptions>(config.GetSection("Whisparr:Log"));
                                    services.Configure<UpdateOptions>(config.GetSection("Whisparr:Update"));
                                }).Build();

                            break;
                        }
                }
            }
            catch (InvalidConfigFileException ex)
            {
                throw new WhisparrStartupException(ex);
            }
            catch (TerminateApplicationException e)
            {
                Logger.Info(e.Message);
                LogManager.Configuration = null;
            }

            // Make sure there are no lingering database connections
            GC.Collect();
            GC.WaitForPendingFinalizers();
            SQLiteConnection.ClearAllPools();
            NpgsqlConnection.ClearAllPools();
        }

        public static IHostBuilder CreateConsoleHostBuilder(string[] args, StartupContext context)
        {
            // Before the configuration is read, so a seeded config5.xml supplies the port, bind address and URL base.
            var setupModeState = DetectSetupMode(new AppFolderInfo(context));
            var config = GetConfiguration(context);

            var bindAddress = config.GetValue<string>($"Whisparr:Server:{nameof(ServerOptions.BindAddress)}") ?? config.GetValue(nameof(ConfigFileProvider.BindAddress), "*");
            var port = config.GetValue<int?>($"Whisparr:Server:{nameof(ServerOptions.Port)}") ?? config.GetValue(nameof(ConfigFileProvider.Port), 6969);
            var sslPort = config.GetValue<int?>($"Whisparr:Server:{nameof(ServerOptions.SslPort)}") ?? config.GetValue(nameof(ConfigFileProvider.SslPort), 9898);
            var enableSsl = config.GetValue<bool?>($"Whisparr:Server:{nameof(ServerOptions.EnableSsl)}") ?? config.GetValue(nameof(ConfigFileProvider.EnableSsl), false);
            var sslCertPath = config.GetValue<string>($"Whisparr:Server:{nameof(ServerOptions.SslCertPath)}") ?? config.GetValue<string>(nameof(ConfigFileProvider.SslCertPath));
            var sslCertPassword = config.GetValue<string>($"Whisparr:Server:{nameof(ServerOptions.SslCertPassword)}") ?? config.GetValue<string>(nameof(ConfigFileProvider.SslCertPassword));
            var logDbEnabled = config.GetValue<bool?>($"Whisparr:Log:{nameof(LogOptions.DbEnabled)}") ?? config.GetValue(nameof(ConfigFileProvider.LogDbEnabled), true);

            var urls = new List<string> { BuildUrl("http", bindAddress, port) };

            if (enableSsl && sslCertPath.IsNotNullOrWhiteSpace())
            {
                urls.Add(BuildUrl("https", bindAddress, sslPort));
            }

            return new HostBuilder()
                .UseContentRoot(Directory.GetCurrentDirectory())
                .UseServiceProviderFactory(new DryIocServiceProviderFactory(new Container(rules => rules.WithNzbDroneRules())))
                .ConfigureContainer<IContainer>(c =>
                {
                    c.AutoAddServices(Bootstrap.ASSEMBLIES)
                        .AddNzbDroneLogger();

                    if (setupModeState.IsActive)
                    {
                        // A legacy install is waiting to be migrated, so there is no v5 main database yet.
                        c.AddDummyDatabase().AddSetupMode(setupModeState);
                    }
                    else
                    {
                        c.AddDatabase();
                    }

                    c.AddStartupContext(context);

                    // Only register GithubUpdatePackageProvider for IUpdatePackageProvider
                    c.Register<NzbDrone.Core.Update.GithubUpdatePackageProvider>(Reuse.Singleton);
                    c.Register<NzbDrone.Core.Update.IUpdatePackageProvider, NzbDrone.Core.Update.GithubUpdatePackageProvider>(Reuse.Singleton, ifAlreadyRegistered: IfAlreadyRegistered.Replace);

                    if (UseLogDatabase(logDbEnabled, setupModeState))
                    {
                        c.AddLogDatabase();
                    }
                    else
                    {
                        c.AddDummyLogDatabase();
                    }
                })
                .ConfigureServices(services =>
                {
                    services.Configure<PostgresOptions>(config.GetSection("Whisparr:Postgres"));
                    services.Configure<AppOptions>(config.GetSection("Whisparr:App"));
                    services.Configure<AuthOptions>(config.GetSection("Whisparr:Auth"));
                    services.Configure<ServerOptions>(config.GetSection("Whisparr:Server"));
                    services.Configure<LogOptions>(config.GetSection("Whisparr:Log"));
                    services.Configure<UpdateOptions>(config.GetSection("Whisparr:Update"));
                })
                .ConfigureWebHost(builder =>
                {
                    builder.UseConfiguration(config);
                    builder.UseUrls(urls.ToArray());
                    builder.UseKestrel(options =>
                    {
                        if (enableSsl && sslCertPath.IsNotNullOrWhiteSpace())
                        {
                            options.ConfigureHttpsDefaults(configureOptions =>
                            {
                                configureOptions.ServerCertificate = ValidateSslCertificate(sslCertPath, sslCertPassword);
                            });
                        }
                    });
                    builder.ConfigureKestrel(serverOptions =>
                    {
                        serverOptions.AllowSynchronousIO = false;
                        serverOptions.Limits.MaxRequestBodySize = null;
                    });
                    builder.UseStartup<Startup>();
                });
        }

        // Setup mode runs before the wizard has created the v5 databases, and on PostgreSQL the log database
        // can't be migrated until it exists, so setup mode logs to the console and files only.
        public static bool UseLogDatabase(bool logDbEnabled, ISetupModeState setupModeState)
        {
            return logDbEnabled && !setupModeState.IsActive;
        }

        // Looks for a Whisparr v2 or Eros install that v5 would otherwise ignore. If there is one, config5.xml is
        // seeded from it (when there is none yet) and the app starts in setup mode.
        private static ISetupModeState DetectSetupMode(AppFolderInfo appFolder)
        {
            // An interrupted phase 1 is undone, so the halt page shows again.
            new SetupRunState(appFolder).DiscardIfPresent();

            var detection = new LegacyInstallDetector(appFolder, Microsoft.Extensions.Options.Options.Create(PostgresOptions.GetOptions())).Detect();

            var config5Path = appFolder.GetConfigPath();

            if (!detection.SetupModeRequired)
            {
                // Setup is over, so a marker left behind must not let a later config5.xml be mistaken for setup's own.
                SetupConfig5Seeding.ClearMarker(config5Path);

                return SetupModeState.Inactive;
            }

            // Eros wins when both exist. There may be no install at all, when the legacy database is unreadable.
            var install = detection.Installs.FirstOrDefault(i => i.Kind == LegacyKind.Eros) ?? detection.Installs.FirstOrDefault();

            // The legacy config.xml decides the sign-in settings on every setup-mode start, so a config5.xml an earlier
            // setup-mode start seeded is seeded again. One the user made is never touched.
            if (install != null && !File.Exists(detection.LegacyConfigPath))
            {
                Logger.Warn("{0} was not found, so {1} was not seeded from it", detection.LegacyConfigPath, config5Path);
            }

            var seeded = SetupConfig5Seeding.Seed(new Config5Seeder(), detection.LegacyConfigPath, config5Path, install);

            if (seeded)
            {
                Logger.Info("Seeded {0} from {1}", config5Path, detection.LegacyConfigPath);
            }

            if (detection.PendingEnvSwitch)
            {
                // The v5 database is already built, so no migration is offered, only the environment change.
                Logger.Info("The v5 database is ready, but {0} still names a legacy database, starting in setup mode", string.Join(", ", detection.EnvTargets.Select(t => t.Name)));
            }
            else
            {
                Logger.Info("A legacy install was found, starting in setup mode");
            }

            return new SetupModeState(detection, seeded);
        }

        public static ApplicationModes GetApplicationMode(IStartupContext startupContext)
        {
            if (startupContext.Help)
            {
                return ApplicationModes.Help;
            }

            if (OsInfo.IsWindows && startupContext.RegisterUrl)
            {
                return ApplicationModes.RegisterUrl;
            }

            if (OsInfo.IsWindows && startupContext.InstallService)
            {
                return ApplicationModes.InstallService;
            }

            if (OsInfo.IsWindows && startupContext.UninstallService)
            {
                return ApplicationModes.UninstallService;
            }

            // IsWindowsService can throw sometimes, so wrap it
            var isWindowsService = false;
            try
            {
                isWindowsService = WindowsServiceHelpers.IsWindowsService();
            }
            catch (Exception e)
            {
                Logger.Error(e, "Failed to get service status");
            }

            if (OsInfo.IsWindows && isWindowsService)
            {
                return ApplicationModes.Service;
            }

            return ApplicationModes.Interactive;
        }

        private static IConfiguration GetConfiguration(StartupContext context)
        {
            var appFolder = new AppFolderInfo(context);
            var configPath = appFolder.GetConfigPath();

            try
            {
                return new ConfigurationBuilder()
                    .AddXmlFile(configPath, optional: true, reloadOnChange: false)
                    .AddInMemoryCollection(new List<KeyValuePair<string, string>>
                    {
                            new ("dataProtectionFolder", appFolder.GetDataProtectionPath())
                    })
                    .AddEnvironmentVariables()
                    .Build();
            }
            catch (InvalidDataException ex)
            {
                Logger.Error(ex, ex.Message);

                throw new InvalidConfigFileException($"{configPath} is corrupt or invalid. Please delete the config file and Whisparr will recreate it.", ex);
            }
        }

        private static string BuildUrl(string scheme, string bindAddress, int port)
        {
            return $"{scheme}://{bindAddress}:{port}";
        }

        private static X509Certificate2 ValidateSslCertificate(string cert, string password)
        {
            X509Certificate2 certificate;

            try
            {
                var certBytes = System.IO.File.ReadAllBytes(cert);
#pragma warning disable SYSLIB0057 // Suppress obsolete warning for X509Certificate2 constructor
                certificate = new X509Certificate2(certBytes, password, X509KeyStorageFlags.DefaultKeySet);
#pragma warning restore SYSLIB0057
            }
            catch (CryptographicException ex)
            {
                if (ex.HResult == 0x2 || ex.HResult == 0x2006D080)
                {
                    throw new WhisparrStartupException(ex,
                        $"The SSL certificate file {cert} does not exist");
                }

                throw new WhisparrStartupException(ex);
            }

            return certificate;
        }
    }
}
