﻿namespace Microsoft.eShopOnContainers.Services.Ordering.API
{
    using AspNetCore.Http;
    using Autofac;
    using Autofac.Extensions.DependencyInjection;
    using global::Ordering.API.Application.IntegrationEvents;
    using global::Ordering.API.Application.IntegrationEvents.Events;
    using global::Ordering.API.Infrastructure.Filters;
    using global::Ordering.API.Infrastructure.Middlewares;
    using Infrastructure.AutofacModules;
    using Infrastructure.Filters;
    using Infrastructure.Services;
    using Microsoft.ApplicationInsights.Extensibility;
    using Microsoft.ApplicationInsights.ServiceFabric;
    using Microsoft.AspNetCore.Authentication.JwtBearer;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.Azure.ServiceBus;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.eShopOnContainers.BuildingBlocks.EventBus;
    using Microsoft.eShopOnContainers.BuildingBlocks.EventBus.Abstractions;
    using Microsoft.eShopOnContainers.BuildingBlocks.EventBusRabbitMQ;
    using Microsoft.eShopOnContainers.BuildingBlocks.EventBusServiceBus;
    using Microsoft.eShopOnContainers.BuildingBlocks.IntegrationEventLogEF;
    using Microsoft.eShopOnContainers.BuildingBlocks.IntegrationEventLogEF.Services;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using Ordering.Infrastructure;
    using RabbitMQ.Client;
    using Swashbuckle.AspNetCore.Swagger;
    using System;
    using System.Collections.Generic;
    using System.Data.Common;
    using System.IdentityModel.Tokens.Jwt;
    using System.Reflection;
    using HealthChecks.UI.Client;
    using Microsoft.AspNetCore.Diagnostics.HealthChecks;
    using Microsoft.Extensions.Diagnostics.HealthChecks;

    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        public IServiceProvider ConfigureServices(IServiceCollection services)
        {
            services.AddApplicationInsights(Configuration)
                .AddCustomMvc()
                .AddHealthChecks(Configuration)
                .AddCustomDbContext(Configuration)
                .AddCustomSwagger(Configuration)
                .AddCustomIntegrations(Configuration)
                .AddCustomConfiguration(Configuration)
                .AddEventBus(Configuration)
                .AddCustomAuthentication(Configuration);

            //configure autofac

            var container = new ContainerBuilder();
            container.Populate(services);

            container.RegisterModule(new MediatorModule());
            container.RegisterModule(new ApplicationModule(Configuration["ConnectionString"]));

            return new AutofacServiceProvider(container.Build());
        }


        public void Configure(IApplicationBuilder app, ILoggerFactory loggerFactory)
        {
            //loggerFactory.AddAzureWebAppDiagnostics();
            //loggerFactory.AddApplicationInsights(app.ApplicationServices, LogLevel.Trace);

            var pathBase = Configuration["PATH_BASE"];
            if (!string.IsNullOrEmpty(pathBase))
            {
                loggerFactory.CreateLogger<Startup>().LogDebug("Using PATH BASE '{pathBase}'", pathBase);
                app.UsePathBase(pathBase);
            }

            app.UseCors("CorsPolicy");

            app.UseHealthChecks("/liveness", new HealthCheckOptions
            {
                Predicate = r => r.Name.Contains("self")
            });

            app.UseHealthChecks("/hc", new HealthCheckOptions()
            {
                Predicate = _ => true,
                ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
            });

            ConfigureAuth(app);

            app.UseMvcWithDefaultRoute();

            app.UseSwagger()
                .UseSwaggerUI(c =>
                {
                    c.SwaggerEndpoint(
                        $"{(!string.IsNullOrEmpty(pathBase) ? pathBase : string.Empty)}/swagger/v1/swagger.json",
                        "Ordering.API V1");
                    c.OAuthClientId("orderingswaggerui");
                    c.OAuthAppName("Ordering Swagger UI");
                });

            ConfigureEventBus(app);
        }


        private void ConfigureEventBus(IApplicationBuilder app)
        {
            var eventBus = app.ApplicationServices.GetRequiredService<IMultiEventBus>();

            eventBus
                .Subscribe<UserCheckoutAcceptedIntegrationEvent,
                    IIntegrationEventHandler<UserCheckoutAcceptedIntegrationEvent>>();
            eventBus
                .Subscribe<GracePeriodConfirmedIntegrationEvent,
                    IIntegrationEventHandler<GracePeriodConfirmedIntegrationEvent>>();
            eventBus
                .Subscribe<OrderStockConfirmedIntegrationEvent,
                    IIntegrationEventHandler<OrderStockConfirmedIntegrationEvent>>();
            eventBus
                .Subscribe<OrderStockRejectedIntegrationEvent,
                    IIntegrationEventHandler<OrderStockRejectedIntegrationEvent>>();
            eventBus
                .Subscribe<OrderPaymentFailedIntegrationEvent,
                    IIntegrationEventHandler<OrderPaymentFailedIntegrationEvent>>();
            eventBus
                .Subscribe<OrderPaymentSucceededIntegrationEvent,
                    IIntegrationEventHandler<OrderPaymentSucceededIntegrationEvent>>();
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

    static class CustomExtensionsMethods
    {
        public static IServiceCollection AddApplicationInsights(this IServiceCollection services,
            IConfiguration configuration)
        {
            services.AddApplicationInsightsTelemetry(configuration);
            var orchestratorType = configuration.GetValue<string>("OrchestratorType");

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

            return services;
        }

        public static IServiceCollection AddCustomMvc(this IServiceCollection services)
        {
            // Add framework services.
            services.AddMvc(options => { options.Filters.Add(typeof(HttpGlobalExceptionFilter)); })
                .SetCompatibilityVersion(CompatibilityVersion.Version_2_2)
                .AddControllersAsServices(); //Injecting Controllers themselves thru DI
            //For further info see: http://docs.autofac.org/en/latest/integration/aspnetcore.html#controllers-as-services

            services.AddCors(options =>
            {
                options.AddPolicy("CorsPolicy",
                    builder => builder
                        .SetIsOriginAllowed((host) => true)
                        .AllowAnyMethod()
                        .AllowAnyHeader()
                        .AllowCredentials());
            });

            return services;
        }

        public static IServiceCollection AddHealthChecks(this IServiceCollection services, IConfiguration configuration)
        {
            var hcBuilder = services.AddHealthChecks();

            hcBuilder.AddCheck("self", () => HealthCheckResult.Healthy());

            hcBuilder
                .AddSqlServer(
                    configuration["ConnectionString"],
                    name: "OrderingDB-check",
                    tags: new string[] {"orderingdb"});

            if (configuration.GetValue<bool>("AzureServiceBusEnabled"))
            {
                hcBuilder
                    .AddAzureServiceBusTopic(
                        configuration["EventBusConnection"],
                        topicName: "eshop_event_bus",
                        name: "ordering-servicebus-check",
                        tags: new string[] {"servicebus"});
            }
            else
            {
                hcBuilder
                    .AddRabbitMQ(
                        $"amqp://{configuration["EventBusConnection"]}",
                        name: "ordering-rabbitmqbus-check",
                        tags: new string[] {"rabbitmqbus"});
            }

            return services;
        }

        public static IServiceCollection AddCustomDbContext(this IServiceCollection services,
            IConfiguration configuration)
        {
            services.AddEntityFrameworkSqlServer()
                .AddDbContext<OrderingContext>(options =>
                    {
                        options.UseSqlServer(configuration["ConnectionString"],
                            sqlServerOptionsAction: sqlOptions =>
                            {
                                sqlOptions.MigrationsAssembly(typeof(Startup).GetTypeInfo().Assembly.GetName().Name);
                                sqlOptions.EnableRetryOnFailure(maxRetryCount: 10,
                                    maxRetryDelay: TimeSpan.FromSeconds(30), errorNumbersToAdd: null);
                            });
                    },
                    ServiceLifetime
                        .Scoped //Showing explicitly that the DbContext is shared across the HTTP request scope (graph of objects started in the HTTP request)
                );

            services.AddDbContext<IntegrationEventLogContext>(options =>
            {
                options.UseSqlServer(configuration["ConnectionString"],
                    sqlServerOptionsAction: sqlOptions =>
                    {
                        sqlOptions.MigrationsAssembly(typeof(Startup).GetTypeInfo().Assembly.GetName().Name);
                        //Configuring Connection Resiliency: https://docs.microsoft.com/en-us/ef/core/miscellaneous/connection-resiliency 
                        sqlOptions.EnableRetryOnFailure(maxRetryCount: 10, maxRetryDelay: TimeSpan.FromSeconds(30),
                            errorNumbersToAdd: null);
                    });
            });

            return services;
        }

        public static IServiceCollection AddCustomSwagger(this IServiceCollection services,
            IConfiguration configuration)
        {
            services.AddSwaggerGen(options =>
            {
                options.DescribeAllEnumsAsStrings();
                options.SwaggerDoc("v1", new Swashbuckle.AspNetCore.Swagger.Info
                {
                    Title = "Ordering HTTP API",
                    Version = "v1",
                    Description = "The Ordering Service HTTP API",
                    TermsOfService = "Terms Of Service"
                });

                options.AddSecurityDefinition("oauth2", new OAuth2Scheme
                {
                    Type = "oauth2",
                    Flow = "implicit",
                    AuthorizationUrl = $"{configuration.GetValue<string>("IdentityUrlExternal")}/connect/authorize",
                    TokenUrl = $"{configuration.GetValue<string>("IdentityUrlExternal")}/connect/token",
                    Scopes = new Dictionary<string, string>()
                    {
                        {"orders", "Ordering API"}
                    }
                });

                options.OperationFilter<AuthorizeCheckOperationFilter>();
            });

            return services;
        }

        public static IServiceCollection AddCustomIntegrations(this IServiceCollection services,
            IConfiguration configuration)
        {
            services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
            services.AddTransient<IIdentityService, IdentityService>();
            services.AddTransient<Func<DbConnection, IIntegrationEventLogService>>(
                sp => (DbConnection c) => new IntegrationEventLogService(c));

            services.AddTransient<IOrderingIntegrationEventService, OrderingIntegrationEventService>();

            if (configuration.GetValue<bool>("AzureServiceBusEnabled"))
            {
                services.AddSingleton<IMultiServiceBusConnections>(sp =>
                {
                    IMultiServiceBusConnections connections = new MultiServiceBusConnections();
                    var logger = sp.GetRequiredService<ILogger<DefaultServiceBusPersisterConnection>>();

                    var serviceBusConnectionStringTenantA = configuration["EventBusConnection"];
                    var serviceBusConnectionTenantA =
                        new ServiceBusConnectionStringBuilder(serviceBusConnectionStringTenantA);

                    var serviceBusConnectionStringTenantB = configuration["EventBusConnection"];
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
                    connections.AddConnection(GenerateConnection("TenantA", sp, configuration));
                    connections.AddConnection(GenerateConnection("TenantB", sp, configuration));

                    return connections;
                });
            }


            return services;
        }

        private static IRabbitMQPersistentConnection GenerateConnection(String vHost, IServiceProvider sp,
            IConfiguration configuration)
        {
            var logger = sp.GetRequiredService<ILogger<DefaultRabbitMQPersistentConnection>>();

            var factory = new ConnectionFactory()
            {
                HostName = configuration["EventBusConnection"],
                DispatchConsumersAsync = true
            };

            if (!string.IsNullOrEmpty(configuration["EventBusUserName"]))
            {
                factory.UserName = configuration["EventBusUserName"];
            }

            if (!string.IsNullOrEmpty(configuration["EventBusPassword"]))
            {
                factory.Password = configuration["EventBusPassword"];
            }

            factory.VirtualHost = vHost;

            var retryCount = 5;
            if (!string.IsNullOrEmpty(configuration["EventBusRetryCount"]))
            {
                retryCount = int.Parse(configuration["EventBusRetryCount"]);
            }

            return new DefaultRabbitMQPersistentConnection(factory, logger, retryCount);
        }

        public static IServiceCollection AddCustomConfiguration(this IServiceCollection services,
            IConfiguration configuration)
        {
            services.AddOptions();
            services.Configure<OrderingSettings>(configuration);
            services.Configure<ApiBehaviorOptions>(options =>
            {
                options.InvalidModelStateResponseFactory = context =>
                {
                    var problemDetails = new ValidationProblemDetails(context.ModelState)
                    {
                        Instance = context.HttpContext.Request.Path,
                        Status = StatusCodes.Status400BadRequest,
                        Detail = "Please refer to the errors property for additional details."
                    };

                    return new BadRequestObjectResult(problemDetails)
                    {
                        ContentTypes = {"application/problem+json", "application/problem+xml"}
                    };
                };
            });

            return services;
        }

        public static IServiceCollection AddEventBus(this IServiceCollection services, IConfiguration configuration)
        {
            var subscriptionClientName = configuration["SubscriptionClientName"];

            {
                if (configuration.GetValue<bool>("AzureServiceBusEnabled"))
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
                        if (!string.IsNullOrEmpty(configuration["EventBusRetryCount"]))
                        {
                            retryCount = int.Parse(configuration["EventBusRetryCount"]);
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

            return services;
        }

        public static IServiceCollection AddCustomAuthentication(this IServiceCollection services,
            IConfiguration configuration)
        {
            // prevent from mapping "sub" claim to nameidentifier.
            JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Remove("sub");

            var identityUrl = configuration.GetValue<string>("IdentityUrl");

            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            }).AddJwtBearer(options =>
            {
                options.Authority = identityUrl;
                options.RequireHttpsMetadata = false;
                options.Audience = "orders";
            });

            return services;
        }
    }
}