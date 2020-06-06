using System.Collections.Generic;

namespace Microsoft.eShopOnContainers.BuildingBlocks.EventBusServiceBus
{
    public interface IMultiServiceBusConnections
    {
        List<IServiceBusPersisterConnection> GetConnections();
        void AddConnection(IServiceBusPersisterConnection connection);
        void RemoveConnection(IServiceBusPersisterConnection connection);
    }
}