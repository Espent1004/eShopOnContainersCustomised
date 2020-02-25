using System;
using Microsoft.eShopOnContainers.BuildingBlocks.EventBus.Events;

namespace TenantARFIDService.IntegrationEvents.Events
{
    public class RFIDScannedIntegrationEvent : IntegrationEvent
    {
        public String OrderNumber { get; set; }
        public String SavedEventId { get; set; }
    }
}