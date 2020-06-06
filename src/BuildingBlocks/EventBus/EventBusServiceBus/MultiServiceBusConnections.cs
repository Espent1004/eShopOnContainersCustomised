using System.Collections.Generic;

namespace Microsoft.eShopOnContainers.BuildingBlocks.EventBusServiceBus
{
    public class MultiServiceBusConnections : IMultiServiceBusConnections
    {
        public List<IServiceBusPersisterConnection> Connections;

        public MultiServiceBusConnections()
        {
            Connections = new List<IServiceBusPersisterConnection>();
        }

        public List<IServiceBusPersisterConnection> GetConnections()
        {
            return Connections;
        }

        public void AddConnection(IServiceBusPersisterConnection connection)
        {
            Connections.Add((connection));
        }

        public void RemoveConnection(IServiceBusPersisterConnection connection)
        {
            Connections.Remove(connection);
        }
    }
}