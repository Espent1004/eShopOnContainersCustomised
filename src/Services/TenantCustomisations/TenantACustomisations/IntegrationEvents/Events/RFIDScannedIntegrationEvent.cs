using System;
using Microsoft.eShopOnContainers.BuildingBlocks.EventBus.Events;

namespace TenantACustomisations.IntegrationEvents.Events
{
    public class RFIDScannedIntegrationEvent : IntegrationEvent
    {
        public String OrderNumber { get; set; }
        public string SavedEventId { get; set; }
    }
}