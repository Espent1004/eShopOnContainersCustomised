using System;
using Microsoft.eShopOnContainers.BuildingBlocks.EventBus.Events;

namespace TenantACustomisations.IntegrationEvents.Events
{
    public class OrderStatusChangedToAwaitingValidationSavedIntegrationEvent : IntegrationEvent
    {
        public OrderStatusChangedToAwaitingValidationSavedIntegrationEvent(String orderNumber, String savedEventId)
        {
            OrderNumber = orderNumber;
            SavedEventId = savedEventId;
        }

        public String OrderNumber { get; set; }
        public String SavedEventId { get; set; }

    }
}