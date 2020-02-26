using Autofac;
using Microsoft.eShopOnContainers.BuildingBlocks.EventBus.Abstractions;
using Microsoft.eShopOnContainers.Services.TenantACustomisations.IntegrationEvents.EventHandling;
using System.Reflection;
using TenantACustomisations.ExternalServices;
using TenantACustomisations.IntegrationEvents.Events;

namespace Microsoft.eShopOnContainers.Services.TenantACustomisations.Infrastructure.AutofacModules
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
            builder.RegisterAssemblyTypes(typeof(RFIDScannedIntegrationEventHandler).GetTypeInfo().Assembly)
                           .AsClosedTypesOf(typeof(IIntegrationEventHandler<>));

        }
    }
}
