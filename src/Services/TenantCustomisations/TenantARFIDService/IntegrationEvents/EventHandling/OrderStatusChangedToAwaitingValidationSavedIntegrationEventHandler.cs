using System;
using System.Threading.Tasks;
using Microsoft.eShopOnContainers.BuildingBlocks.EventBus.Abstractions;
using Microsoft.Extensions.Logging;
using Serilog.Context;
using TenantARFIDService.Database;
using TenantARFIDService.IntegrationEvents.Events;
using TenantARFIDService.Model;

namespace TenantARFIDService.IntegrationEvents.EventHandling
{
    public class OrderStatusChangedToAwaitingValidationSavedIntegrationEventHandler :
        IIntegrationEventHandler<OrderStatusChangedToAwaitingValidationSavedIntegrationEvent>
    {
        private readonly ILogger<OrderStatusChangedToAwaitingValidationSavedIntegrationEventHandler> _logger;
        private readonly TenantAContext _context;

        public OrderStatusChangedToAwaitingValidationSavedIntegrationEventHandler(ILogger<OrderStatusChangedToAwaitingValidationSavedIntegrationEventHandler> logger, TenantAContext context)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public async Task Handle(OrderStatusChangedToAwaitingValidationSavedIntegrationEvent @event)
        {
            Order order = new Order();
            order.OrderNumber = @event.OrderNumber;
            order.RFIDScanned = false;
            order.SavedEventId = @event.SavedEventId;
            _context.Orders.Add(order);
            _logger.LogInformation("----- Saving order: {OrderNumber} at TenantA - ({@IntegrationEvent}) - {@Order}", @event.OrderNumber, @event, order);
            await _context.SaveChangesAsync();
        }
    }
}