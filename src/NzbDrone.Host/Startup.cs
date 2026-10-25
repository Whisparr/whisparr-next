using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DryIoc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi;
using NLog.Extensions.Logging;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Common.Processes;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Migration.Setup;
using NzbDrone.Core.Instrumentation;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Host.AccessControl;
using NzbDrone.Host.OpenApi;
using NzbDrone.Http.Authentication;
using NzbDrone.SignalR;
using Whisparr.Api.V3.System;
using Whisparr.Http;
using Whisparr.Http.Authentication;
using Whisparr.Http.ClientSchema;
using Whisparr.Http.ErrorManagement;
using Whisparr.Http.Frontend;
using Whisparr.Http.Middleware;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace NzbDrone.Host
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddLogging(b =>
            {
                b.ClearProviders();
                b.SetMinimumLevel(LogLevel.Trace);
                b.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
                b.AddFilter("Microsoft.AspNetCore.HostFiltering", LogLevel.Information);
                b.AddFilter("Microsoft.AspNetCore.HttpOverrides", LogLevel.Debug);
                b.AddFilter("Whisparr.Http.Authentication", LogLevel.Information);
                b.AddFilter("Microsoft.AspNetCore.DataProtection.KeyManagement.XmlKeyManager", LogLevel.Error);
                b.AddNLog();
            });

            services.AddOptions<ForwardedHeadersOptions>()
                    .Configure<IConfigFileProvider>(ForwardedHeadersConfigurator.Configure);

            services.AddRouting(options => options.LowercaseUrls = true);

            services.AddResponseCompression(options => options.EnableForHttps = true);

            services.AddCors(options =>
            {
                options.AddPolicy(VersionedApiControllerAttribute.API_CORS_POLICY,
                    builder =>
                    builder.AllowAnyOrigin()
                    .AllowAnyMethod()
                    .AllowAnyHeader());

                options.AddPolicy("AllowGet",
                    builder =>
                    builder.AllowAnyOrigin()
                    .WithMethods("GET", "OPTIONS")
                    .AllowAnyHeader());
            });

            services
            .AddControllers(options =>
            {
                options.ReturnHttpNotAcceptable = true;
            })
            .AddApplicationPart(typeof(SystemController).Assembly)
            .AddApplicationPart(typeof(Whisparr.Api.V5.System.SystemController).Assembly)
            .AddApplicationPart(typeof(StaticResourceController).Assembly)
            .AddJsonOptions(options =>
            {
                STJson.ApplySerializerSettings(options.JsonSerializerOptions);
            })
            .AddControllersAsServices();

            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v5", new OpenApiInfo
                {
                    Version = "5.0.0",
                    Title = "Whisparr",
                    Description = "Whisparr API docs",
                    License = new OpenApiLicense
                    {
                        Name = "GPL-3.0",
                        Url = new Uri("https://github.com/Whisparr/Whisparr/blob/develop/LICENSE")
                    }
                });

                c.SwaggerDoc("v3", new OpenApiInfo
                {
                    Version = "3.0.0",
                    Title = "Whisparr",
                    Description = "Whisparr API docs",
                    License = new OpenApiLicense
                    {
                        Name = "GPL-3.0",
                        Url = new Uri("https://github.com/Whisparr/Whisparr/blob/develop/LICENSE")
                    }
                });

                // Include XML comments for better documentation
                foreach (var xmlName in new[] { "Whisparr.Api.V3.xml", "Whisparr.Api.V5.xml" })
                {
                    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlName);
                    if (File.Exists(xmlPath))
                    {
                        c.IncludeXmlComments(xmlPath);
                    }
                }

                var apiKeyHeader = new OpenApiSecurityScheme
                {
                    Name = "X-Api-Key",
                    Type = SecuritySchemeType.ApiKey,
                    Scheme = "apiKey",
                    Description = "Apikey passed as header",
                    In = ParameterLocation.Header
                };

                c.CustomOperationIds(api => OperationIds.FromRoute(api.HttpMethod, api.RelativePath));
                c.AddSecurityDefinition("X-Api-Key", apiKeyHeader);

                c.AddSecurityRequirement(document => new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference(apiKeyHeader.Name, document)] = new List<string>(),
                });

                var apikeyQuery = new OpenApiSecurityScheme
                {
                    Name = "apikey",
                    Type = SecuritySchemeType.ApiKey,
                    Scheme = "apiKey",
                    Description = "Apikey passed as query parameter",
                    In = ParameterLocation.Query
                };

                c.AddServer(new OpenApiServer
                {
                    Url = "{protocol}://{hostpath}",
                    Variables = new Dictionary<string, OpenApiServerVariable>
                    {
                        { "protocol", new OpenApiServerVariable { Default = "http", Enum = new List<string> { "http", "https" } } },
                        { "hostpath", new OpenApiServerVariable { Default = "localhost:6969" } }
                    }
                });

                c.AddSecurityDefinition("apikey", apikeyQuery);

                c.AddSecurityRequirement(document => new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference(apikeyQuery.Name, document)] = new List<string>(),
                });

                c.OperationFilter<SuccessStatusCodeOperationFilter>();
                c.OperationFilter<RequiredRequestBodyOperationFilter>();
                c.OperationFilter<AllowAnonymousOperationFilter>();
                c.OperationFilter<BackupRestoreUploadOperationFilter>();
                c.SchemaFilter<CommandResourceSchemaFilter>();

                c.DescribeAllParametersInCamelCase();

                // Generate docs based on the controller's API version
                c.DocInclusionPredicate((docName, apiDesc) =>
                {
                    if (apiDesc.ActionDescriptor is not ControllerActionDescriptor controllerActionDescriptor)
                    {
                        return false;
                    }

                    var attributes = controllerActionDescriptor.ControllerTypeInfo.GetCustomAttributes(true);

                    var versions = attributes.OfType<VersionedApiControllerAttribute>().Select(attr => attr.Version)
                        .Concat(attributes.OfType<VersionedFeedControllerAttribute>().Select(attr => attr.Version))
                        .ToList();

                    // Return anything with no version or a matching version
                    return versions.Count == 0 || versions.Any(v => $"v{v}" == docName);
                });
            });

            services
            .AddSignalR()
            .AddJsonProtocol(options =>
            {
                options.PayloadSerializerOptions = STJson.GetSerializerSettings();
            });

            services.AddDataProtection()
                .PersistKeysToFileSystem(new DirectoryInfo(Configuration["dataProtectionFolder"]));

            services.AddSingleton<IAuthorizationPolicyProvider, UiAuthorizationPolicyProvider>();
            services.AddSingleton<IAuthorizationHandler, UiAuthorizationHandler>();

            services.AddAuthorization(options =>
            {
                options.AddPolicy("SignalR", policy =>
                {
                    policy.AuthenticationSchemes.Add("SignalR");
                    policy.RequireAuthenticatedUser();
                });

                // Require auth on everything except those marked [AllowAnonymous]
                options.FallbackPolicy = new AuthorizationPolicyBuilder("API")
                .RequireAuthenticatedUser()
                .Build();
            });

            services.AddAppAuthentication();

            services.PostConfigure<ApiBehaviorOptions>(options =>
            {
                var builtInFactory = options.InvalidModelStateResponseFactory;

                options.InvalidModelStateResponseFactory = context =>
                {
                    var loggerFactory = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>();
                    var logger = loggerFactory.CreateLogger(context.ActionDescriptor.DisplayName);

                    logger.LogError(STJson.ToJson(context.ModelState));

                    return builtInFactory(context);
                };
            });
        }

        public void Configure(IApplicationBuilder app,
                              IContainer container,
                              IStartupContext startupContext,
                              Lazy<IMainDatabase> mainDatabaseFactory,
                              Lazy<ILogDatabase> logDatabaseFactory,
                              DatabaseTarget dbTarget,
                              ISingleInstancePolicy singleInstancePolicy,
                              InitializeLogger initializeLogger,
                              ReconfigureLogging reconfigureLogging,
                              IAppFolderFactory appFolderFactory,
                              IProvidePidFile pidFileProvider,
                              IConfigFileProvider configFileProvider,
                              IRuntimeInfo runtimeInfo,
                              IFirewallAdapter firewallAdapter,
                              IEventAggregator eventAggregator,
                              WhisparrErrorPipeline errorHandler,
                              ISetupModeState setupModeState)
        {
            initializeLogger.Initialize();
            appFolderFactory.Register();
            pidFileProvider.Write();

            configFileProvider.EnsureDefaultConfigFile();

            reconfigureLogging.Reconfigure();

            EnsureSingleInstance(false, startupContext, singleInstancePolicy);

            // instantiate the databases to initialize/migrate them. Setup mode has no main database yet.
            if (!setupModeState.IsActive)
            {
                _ = mainDatabaseFactory.Value;
            }

            if (configFileProvider.LogDbEnabled)
            {
                _ = logDatabaseFactory.Value;
                dbTarget.Register();
            }

            if (!setupModeState.IsActive)
            {
                SchemaBuilder.Initialize(container);
            }

            if (OsInfo.IsNotWindows)
            {
                Console.CancelKeyPress += (sender, eventArgs) => NLog.LogManager.Configuration = null;
            }

            if (!setupModeState.IsActive)
            {
                eventAggregator.PublishEvent(new ApplicationStartingEvent());
            }

            if (OsInfo.IsWindows && runtimeInfo.IsAdmin)
            {
                firewallAdapter.MakeAccessible();
            }

            app.Use(async (context, next) =>
            {
                var sw = Stopwatch.StartNew();
                context.Response.OnStarting(() =>
                {
                    context.Response.Headers["X-Total-Request-Time"] = sw.ElapsedMilliseconds.ToString();
                    return Task.CompletedTask;
                });

                await next();

                sw.Stop();
            });

            app.UseForwardedHeaders();
            app.UseHostFiltering();
            app.UseMiddleware<LoggingMiddleware>();
            app.UsePathBase(new PathString(configFileProvider.UrlBase));
            app.UseExceptionHandler(new ExceptionHandlerOptions
            {
                AllowStatusCode404Response = true,
                ExceptionHandler = errorHandler.HandleException
            });

            app.UseRouting();
            app.UseCors();

            // Enable middleware to serve generated Swagger as a JSON endpoint.
            // Place before authentication to allow anonymous access
            // Setup mode leaves them out, so SetupModeMiddleware answers /docs like the rest of the API.
            if (!setupModeState.IsActive)
            {
                app.UseSwagger(c =>
                {
                    c.RouteTemplate = "docs/{documentName}/openapi.json";
                });

                // Enable middleware to serve swagger-ui (HTML, JS, CSS, etc.)
                app.UseSwaggerUI(c =>
                {
                    c.SwaggerEndpoint("/docs/v5/openapi.json", "Whisparr API v5");
                    c.SwaggerEndpoint("/docs/v3/openapi.json", "Whisparr API v3");
                    c.RoutePrefix = "docs";
                    c.DocumentTitle = "Whisparr API";
                    c.DefaultModelsExpandDepth(-1);

                    // Disable the top bar and configure for standalone operation
                    c.ConfigObject.AdditionalItems["displayOperationId"] = false;
                    c.ConfigObject.AdditionalItems["displayRequestDuration"] = true;
                });
            }

            app.UseAuthentication();
            app.UseBrowserRedirect();  // workaround to convert 401 to 302 for browser requests
            app.UseAuthorization();
            app.UseMiddleware<SetupModeMiddleware>();
            app.UseResponseCompression();
            app.Properties["host.AppName"] = BuildInfo.AppName;

            app.UseMiddleware<VersionMiddleware>();
            app.UseMiddleware<UrlBaseMiddleware>(configFileProvider.UrlBase);
            app.UseMiddleware<StartingUpMiddleware>();
            app.UseMiddleware<CacheHeaderMiddleware>();
            app.UseMiddleware<IfModifiedMiddleware>();
            app.UseMiddleware<BufferingMiddleware>(new List<string> { "/api/v3/command", "/api/v5/command" });

            app.UseWebSockets();

            app.UseEndpoints(x =>
            {
                x.MapHub<MessageHub>("/signalr/messages").RequireAuthorization("SignalR");
                x.MapControllers();
            });
        }

        private void EnsureSingleInstance(bool isService, IStartupContext startupContext, ISingleInstancePolicy instancePolicy)
        {
            if (startupContext.Flags.Contains(StartupContext.NO_SINGLE_INSTANCE_CHECK))
            {
                return;
            }

            if (startupContext.Flags.Contains(StartupContext.TERMINATE))
            {
                instancePolicy.KillAllOtherInstance();
            }
            else if (startupContext.Args.ContainsKey(StartupContext.APPDATA))
            {
                instancePolicy.WarnIfAlreadyRunning();
            }
            else if (isService)
            {
                instancePolicy.KillAllOtherInstance();
            }
            else
            {
                instancePolicy.PreventStartIfAlreadyRunning();
            }
        }
    }
}
