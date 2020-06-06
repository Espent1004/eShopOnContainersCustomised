﻿namespace Microsoft.eShopOnContainers.Services.Marketing.API
{
    using AspNetCore.Builder;
    using AspNetCore.Hosting;
    using AspNetCore.Http;
    using Autofac;
    using Autofac.Extensions.DependencyInjection;
    using Azure.ServiceBus;
    using BuildingBlocks.EventBus;
    using BuildingBlocks.EventBus.Abstractions;
    using BuildingBlocks.EventBusRabbitMQ;
    using BuildingBlocks.EventBusServiceBus;
    using EntityFrameworkCore;
    using Extensions.Configuration;
    using Extensions.DependencyInjection;
    using Extensions.Logging;
    using HealthChecks.UI.Client;
    using Infrastructure;
    using Infrastructure.Filters;
    using Infrastructure.Repositories;
    using Infrastructure.Services;
    using IntegrationEvents.Events;
    using Marketing.API.IntegrationEvents.Handlers;
    using Microsoft.ApplicationInsights.Extensibility;
    using Microsoft.ApplicationInsights.ServiceFabric;
    using Microsoft.AspNetCore.Authentication.JwtBearer;
    using Microsoft.AspNetCore.Diagnostics.HealthChecks;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.EntityFrameworkCore.Diagnostics;
    using Microsoft.eShopOnContainers.Services.Marketing.API.Infrastructure.Middlewares;
    using Microsoft.Extensions.Diagnostics.HealthChecks;
    using RabbitMQ.Client;
    using Swashbuckle.AspNetCore.Swagger;
    using System;
    using System.Collections.Generic;
    using System.IdentityModel.Tokens.Jwt;
    using System.Reflection;

    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }


        // This method gets called by the runtime. Use this method to add services to the container.
        public IServiceProvider ConfigureServices(IServiceCollection services)
        {
            RegisterAppInsights(services);

            // Add framework services.
            services
                .AddCustomHealthCheck(Configuration)
                .AddMvc(options => { options.Filters.Add(typeof(HttpGlobalExceptionFilter)); })
                .SetCompatibilityVersion(CompatibilityVersion.Version_2_2)
                .AddControllersAsServices(); //Injecting Controllers themselves thru DIFor further info see: http://docs.autofac.org/en/latest/integration/aspnetcore.html#controllers-as-services

            services.Configure<MarketingSettings>(Configuration);

            ConfigureAuthService(services);

            services.AddDbContext<MarketingContext>(options =>
            {
                options.UseSqlServer(Configuration["ConnectionString"],
                    sqlServerOptionsAction: sqlOptions =>
                    {
                        sqlOptions.MigrationsAssembly(typeof(Startup).GetTypeInfo().Assembly.GetName().Name);
                        //Configuring Connection Resiliency: https://docs.microsoft.com/en-us/ef/core/miscellaneous/connection-resiliency 
                        sqlOptions.EnableRetryOnFailure(maxRetryCount: 10, maxRetryDelay: TimeSpan.FromSeconds(30),
                            errorNumbersToAdd: null);
                    });

                // Changing default behavior when client evaluation occurs to throw. 
                // Default in EF Core would be to log a warning when client evaluation is performed.
                options.ConfigureWarnings(warnings => warnings.Throw(RelationalEventId.QueryClientEvaluationWarning));
                //Check Client vs. Server evaluation: https://docs.microsoft.com/en-us/ef/core/querying/client-eval
            });

            if (Configuration.GetValue<bool>("AzureServiceBusEnabled"))
            {
                services.AddSingleton<IMultiServiceBusConnections>(sp =>
                {
                    IMultiServiceBusConnections connections = new MultiServiceBusConnections();
                    var logger = sp.GetRequiredService<ILogger<DefaultServiceBusPersisterConnection>>();

                    var serviceBusConnectionStringTenantA = Configuration["EventBusConnection"];
                    var serviceBusConnectionTenantA =
                        new ServiceBusConnectionStringBuilder(serviceBusConnectionStringTenantA);

                    var serviceBusConnectionStringTenantB = Configuration["EventBusConnection"];
                    var serviceBusConnectionTenantB =
                        new ServiceBusConnectionStringBuilder(serviceBusConnectionStringTenantB);

                    connections.AddConnection(
                        new DefaultServiceBusPersisterConnection(serviceBusConnectionTenantA, logger));
                    connections.AddConnection(
                        new DefaultServiceBusPersisterConnection(serviceBusConnectionTenantB, logger));

                    return connections;
                });
            }

            else
            {
                services.AddSingleton<IMultiRabbitMQPersistentConnections>(sp =>
                {
                    IMultiRabbitMQPersistentConnections connections = new MultiRabbitMQPersistentConnections();
                    connections.AddConnection(GenerateConnection("TenantA", sp));
                    connections.AddConnection(GenerateConnection("TenantB", sp));

                    return connections;
                });
            }


            // Add framework services.
            services.AddSwaggerGen(options =>
            {
                options.DescribeAllEnumsAsStrings();
                options.SwaggerDoc("v1", new Swashbuckle.AspNetCore.Swagger.Info
                {
                    Title = "Marketing HTTP API",
                    Version = "v1",
                    Description = "The Marketing Service HTTP API",
                    TermsOfService = "Terms Of Service"
                });

                options.AddSecurityDefinition("oauth2", new OAuth2Scheme
                {
                    Type = "oauth2",
                    Flow = "implicit",
                    AuthorizationUrl = $"{Configuration.GetValue<string>("IdentityUrlExternal")}/connect/authorize",
                    TokenUrl = $"{Configuration.GetValue<string>("IdentityUrlExternal")}/connect/token",
                    Scopes = new Dictionary<string, string>()
                    {
                        {"marketing", "Marketing API"}
                    }
                });

                options.OperationFilter<AuthorizeCheckOperationFilter>();
            });

            services.AddCors(options =>
            {
                options.AddPolicy("CorsPolicy",
                    builder => builder
                        .SetIsOriginAllowed((host) => true)
                        .AllowAnyMethod()
                        .AllowAnyHeader()
                        .AllowCredentials());
            });

            RegisterEventBus(services);

            services.AddTransient<IMarketingDataRepository, MarketingDataRepository>();
            services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
            services.AddTransient<IIdentityService, IdentityService>();

            services.AddOptions();

            //configure autofac
            var container = new ContainerBuilder();
            container.Populate(services);

            return new AutofacServiceProvider(container.Build());
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory)
        {
            //loggerFactory.AddAzureWebAppDiagnostics();
            //loggerFactory.AddApplicationInsights(app.ApplicationServices, LogLevel.Trace);

            var pathBase = Configuration["PATH_BASE"];

            if (!string.IsNullOrEmpty(pathBase))
            {
                app.UsePathBase(pathBase);
            }

            app.UseHealthChecks("/hc", new HealthCheckOptions()
            {
                Predicate = _ => true,
                ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
            });

            app.UseHealthChecks("/liveness", new HealthCheckOptions
            {
                Predicate = r => r.Name.Contains("self")
            });

            app.UseCors("CorsPolicy");

            ConfigureAuth(app);

            app.UseMvcWithDefaultRoute();

            app.UseSwagger()
                .UseSwaggerUI(c =>
                {
                    c.SwaggerEndpoint(
                        $"{(!string.IsNullOrEmpty(pathBase) ? pathBase : string.Empty)}/swagger/v1/swagger.json",
                        "Marketing.API V1");
                    c.OAuthClientId("marketingswaggerui");
                    c.OAuthAppName("Marketing Swagger UI");
                });

            ConfigureEventBus(app);
        }

        private void RegisterAppInsights(IServiceCollection services)
        {
            services.AddApplicationInsightsTelemetry(Configuration);
            var orchestratorType = Configuration.GetValue<string>("OrchestratorType");

            if (orchestratorType?.ToUpper() == "K8S")
            {
                // Enable K8s telemetry initializer
                services.AddApplicationInsightsKubernetesEnricher();
            }

            if (orchestratorType?.ToUpper() == "SF")
            {
                // Enable SF telemetry initializer
                services.AddSingleton<ITelemetryInitializer>((serviceProvider) =>
                    new FabricTelemetryInitializer());
            }
        }

        private void ConfigureAuthService(IServiceCollection services)
        {
            // prevent from mapping "sub" claim to nameidentifier.
            JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();

            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            }).AddJwtBearer(options =>
            {
                options.Authority = Configuration.GetValue<string>("IdentityUrl");
                options.Audience = "marketing";
                options.RequireHttpsMetadata = false;
            });
        }

        private void RegisterEventBus(IServiceCollection services)
        {
            var subscriptionClientName = Configuration["SubscriptionClientName"];
            {
                if (Configuration.GetValue<bool>("AzureServiceBusEnabled"))
                {
                    services.AddSingleton<IMultiEventBus, MultiEventBus>(sp =>
                    {
                        var multiServiceBusConnections = sp.GetRequiredService<IMultiServiceBusConnections>();
                        var iLifetimeScope = sp.GetRequiredService<ILifetimeScope>();
                        var logger = sp.GetRequiredService<ILogger<EventBusServiceBus>>();
                        var eventBusSubcriptionsManager = sp.GetRequiredService<IEventBusSubscriptionsManager>();
                        List<IEventBus> eventBuses = new List<IEventBus>();
                        eventBuses.Add(new EventBusServiceBus(multiServiceBusConnections.GetConnections()[0], logger,
                            eventBusSubcriptionsManager, subscriptionClientName, iLifetimeScope, "TenantA"));
                        eventBuses.Add(new EventBusServiceBus(multiServiceBusConnections.GetConnections()[1], logger,
                            eventBusSubcriptionsManager, subscriptionClientName, iLifetimeScope, "TenantB"));
                        Dictionary<int, String> tenants = new Dictionary<int, string>();
                        tenants.Add(1, "TenantA");
                        tenants.Add(2, "TenantB");
                        return new MultiEventBus(eventBuses, tenants);
                    });
                }

                else
                {
                    services.AddSingleton<IMultiEventBus, MultiEventBus>(sp =>
                    {
                        var multiRabbitMqPersistentConnections =
                            sp.GetRequiredService<IMultiRabbitMQPersistentConnections>();
                        var iLifetimeScope = sp.GetRequiredService<ILifetimeScope>();
                        var logger = sp.GetRequiredService<ILogger<EventBusRabbitMQ>>();
                        var eventBusSubcriptionsManager = sp.GetRequiredService<IEventBusSubscriptionsManager>();

                        var retryCount = 5;
                        if (!string.IsNullOrEmpty(Configuration["EventBusRetryCount"]))
                        {
                            retryCount = int.Parse(Configuration["EventBusRetryCount"]);
                        }

                        List<IEventBus> eventBuses = new List<IEventBus>();

                        eventBuses.Add(new EventBusRabbitMQ(multiRabbitMqPersistentConnections.GetConnections()[0],
                            logger,
                            iLifetimeScope, eventBusSubcriptionsManager, "TenantA", subscriptionClientName,
                            retryCount));
                        eventBuses.Add(new EventBusRabbitMQ(multiRabbitMqPersistentConnections.GetConnections()[1],
                            logger,
                            iLifetimeScope, eventBusSubcriptionsManager, "TenantB", subscriptionClientName,
                            retryCount));
                        Dictionary<int, String> tenants = new Dictionary<int, string>();
                        tenants.Add(1, "TenantA");
                        tenants.Add(2, "TenantB");

                        return new MultiEventBus(eventBuses, tenants);
                    });
                }
            }

            services.AddSingleton<IEventBusSubscriptionsManager, InMemoryEventBusSubscriptionsManager>();
            services.AddTransient<UserLocationUpdatedIntegrationEventHandler>();
        }

        private IRabbitMQPersistentConnection GenerateConnection(String vHost, IServiceProvider sp)
        {
            var logger = sp.GetRequiredService<ILogger<DefaultRabbitMQPersistentConnection>>();

            var factory = new ConnectionFactory()
            {
                HostName = Configuration["EventBusConnection"],
                DispatchConsumersAsync = true
            };

            if (!string.IsNullOrEmpty(Configuration["EventBusUserName"]))
            {
                factory.UserName = Configuration["EventBusUserName"];
            }

            if (!string.IsNullOrEmpty(Configuration["EventBusPassword"]))
            {
                factory.Password = Configuration["EventBusPassword"];
            }

            factory.VirtualHost = vHost;

            var retryCount = 5;
            if (!string.IsNullOrEmpty(Configuration["EventBusRetryCount"]))
            {
                retryCount = int.Parse(Configuration["EventBusRetryCount"]);
            }

            return new DefaultRabbitMQPersistentConnection(factory, logger, retryCount);
        }

        private void ConfigureEventBus(IApplicationBuilder app)
        {
            var eventBus = app.ApplicationServices.GetRequiredService<IMultiEventBus>();
            eventBus.Subscribe<UserLocationUpdatedIntegrationEvent, UserLocationUpdatedIntegrationEventHandler>();
        }

        protected virtual void ConfigureAuth(IApplicationBuilder app)
        {
            if (Configuration.GetValue<bool>("UseLoadTest"))
            {
                app.UseMiddleware<ByPassAuthMiddleware>();
            }

            app.UseAuthentication();
        }
    }

    public static class CustomExtensionMethods
    {
        public static IServiceCollection AddCustomHealthCheck(this IServiceCollection services,
            IConfiguration configuration)
        {
            var hcBuilder = services.AddHealthChecks();

            hcBuilder.AddCheck("self", () => HealthCheckResult.Healthy());

            hcBuilder
                .AddSqlServer(
                    configuration["ConnectionString"],
                    name: "MarketingDB-check",
                    tags: new string[] {"marketingdb"})
                .AddMongoDb(
                    configuration["MongoConnectionString"],
                    name: "MarketingDB-mongodb-check",
                    tags: new string[] {"mongodb"});

            var accountName = configuration.GetValue<string>("AzureStorageAccountName");
            var accountKey = configuration.GetValue<string>("AzureStorageAccountKey");
            if (!string.IsNullOrEmpty(accountName) && !string.IsNullOrEmpty(accountKey))
            {
                hcBuilder
                    .AddAzureBlobStorage(
                        $"DefaultEndpointsProtocol=https;AccountName={accountName};AccountKey={accountKey};EndpointSuffix=core.windows.net",
                        name: "marketing-storage-check",
                        tags: new string[] {"marketingstorage"});
            }

            if (configuration.GetValue<bool>("AzureServiceBusEnabled"))
            {
                hcBuilder
                    .AddAzureServiceBusTopic(
                        configuration["EventBusConnection"],
                        topicName: "eshop_event_bus",
                        name: "marketing-servicebus-check",
                        tags: new string[] {"servicebus"});
            }
            else
            {
                hcBuilder
                    .AddRabbitMQ(
                        $"amqp://{configuration["EventBusConnection"]}",
                        name: "marketing-rabbitmqbus-check",
                        tags: new string[] {"rabbitmqbus"});
            }

            return services;
        }
    }
}