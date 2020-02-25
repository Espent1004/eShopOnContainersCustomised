using System;
using System.Threading.Tasks;
using Microsoft.eShopOnContainers.BuildingBlocks.EventBus.Abstractions;
using Microsoft.Extensions.Logging;
using Serilog.Context;
using TenantARFIDService.Database;
using TenantARFIDService.IntegrationEvents.Events;

namespace TenantARFIDService.IntegrationEvents.EventHandling
{
    public class OrderStatusChangedToSubmittedIntegrationEventHandler :
        IIntegrationEventHandler<OrderStatusChangedToSubmittedIntegrationEvent>
    {
        private readonly ILogger<OrderStatusChangedToSubmittedIntegrationEventHandler> _logger;

        public OrderStatusChangedToSubmittedIntegrationEventHandler(ILogger<OrderStatusChangedToSubmittedIntegrationEventHandler> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task Handle(OrderStatusChangedToSubmittedIntegrationEvent @event)
        {
        }
    }
}