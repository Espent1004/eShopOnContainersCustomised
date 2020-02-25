using System.Reflection;
using Autofac;
using Microsoft.eShopOnContainers.BuildingBlocks.EventBus.Abstractions;
using TenantARFIDService.IntegrationEvents.EventHandling;

namespace TenantARFIDService.Infrastructure.AutofacModules
{

    public class ApplicationModule
        :Autofac.Module
    {

        public string QueriesConnectionString { get; }

        public ApplicationModule(string qconstr)
        {
            QueriesConnectionString = qconstr;

        }

        protected override void Load(ContainerBuilder builder)
        {
            builder.RegisterAssemblyTypes(typeof(OrderStatusChangedToAwaitingValidationSavedIntegrationEventHandler).GetTypeInfo().Assembly)
                        .AsClosedTypesOf(typeof(IIntegrationEventHandler<>));
            
        }
    }
}
