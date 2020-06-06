﻿using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ApplicationInsights.ServiceFabric;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Azure.ServiceBus;
using Microsoft.eShopOnContainers.BuildingBlocks.EventBus;
using Microsoft.eShopOnContainers.BuildingBlocks.EventBus.Abstractions;
using Microsoft.eShopOnContainers.BuildingBlocks.EventBusRabbitMQ;
using Microsoft.eShopOnContainers.BuildingBlocks.EventBusServiceBus;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Payment.API.IntegrationEvents.EventHandling;
using Payment.API.IntegrationEvents.Events;
using RabbitMQ.Client;
using System;
using System.Collections.Generic;
using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Payment.API
{
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
            services.AddCustomHealthCheck(Configuration);
            services.Configure<PaymentSettings>(Configuration);

            RegisterAppInsights(services);

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


            RegisterEventBus(services);

            var container = new ContainerBuilder();
            container.Populate(services);
            return new AutofacServiceProvider(container.Build());
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

        private void RegisterEventBus(IServiceCollection services)
        {
            var subscriptionClientName = Configuration["SubscriptionClientName"];

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

                    eventBuses.Add(new EventBusRabbitMQ(multiRabbitMqPersistentConnections.GetConnections()[0], logger,
                        iLifetimeScope, eventBusSubcriptionsManager, "TenantA", subscriptionClientName, retryCount));
                    eventBuses.Add(new EventBusRabbitMQ(multiRabbitMqPersistentConnections.GetConnections()[1], logger,
                        iLifetimeScope, eventBusSubcriptionsManager, "TenantB", subscriptionClientName, retryCount));
                    Dictionary<int, String> tenants = new Dictionary<int, string>();
                    tenants.Add(1, "TenantA");
                    tenants.Add(2, "TenantB");

                    return new MultiEventBus(eventBuses, tenants);
                });
            }

            services.AddTransient<OrderStatusChangedToStockConfirmedIntegrationEventHandler>();
            services.AddSingleton<IEventBusSubscriptionsManager, InMemoryEventBusSubscriptionsManager>();
        }

        private void ConfigureEventBus(IApplicationBuilder app)
        {
            var eventBus = app.ApplicationServices.GetRequiredService<IMultiEventBus>();
            eventBus
                .Subscribe<OrderStatusChangedToStockConfirmedIntegrationEvent,
                    OrderStatusChangedToStockConfirmedIntegrationEventHandler>();
        }
    }

    public static class CustomExtensionMethods
    {
        public static IServiceCollection AddCustomHealthCheck(this IServiceCollection services,
            IConfiguration configuration)
        {
            var hcBuilder = services.AddHealthChecks();

            hcBuilder.AddCheck("self", () => HealthCheckResult.Healthy());

            if (configuration.GetValue<bool>("AzureServiceBusEnabled"))
            {
                hcBuilder
                    .AddAzureServiceBusTopic(
                        configuration["EventBusConnection"],
                        topicName: "eshop_event_bus",
                        name: "payment-servicebus-check",
                        tags: new string[] {"servicebus"});
            }
            else
            {
                hcBuilder
                    .AddRabbitMQ(
                        $"amqp://{configuration["EventBusConnection"]}",
                        name: "payment-rabbitmqbus-check",
                        tags: new string[] {"rabbitmqbus"});
            }

            return services;
        }
    }
}