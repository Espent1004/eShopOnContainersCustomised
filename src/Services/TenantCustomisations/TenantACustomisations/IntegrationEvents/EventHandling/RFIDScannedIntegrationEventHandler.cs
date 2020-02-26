using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.eShopOnContainers.BuildingBlocks.EventBus.Abstractions;
using Microsoft.eShopOnContainers.BuildingBlocks.EventBus.Events;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using TenantACustomisations.Database;
using TenantACustomisations.IntegrationEvents.Events;

namespace Microsoft.eShopOnContainers.Services.TenantACustomisations.IntegrationEvents.EventHandling
{
    public class RFIDScannedIntegrationEventHandler : IIntegrationEventHandler<RFIDScannedIntegrationEvent>
    {
        private readonly TenantAContext _context;
        private readonly ILogger<RFIDScannedIntegrationEventHandler> _logger;
        private readonly IEventBus _eventBus;

        private List<Type> types = new List<Type>()
        {
            typeof(OrderStatusChangedToAwaitingValidationIntegrationEvent)
        };

        public RFIDScannedIntegrationEventHandler(TenantAContext context,
            ILogger<RFIDScannedIntegrationEventHandler> logger, IEventBus eventBus)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        }

        public async Task Handle(RFIDScannedIntegrationEvent @event)
        {
            var savedEvent = _context.SavedEvent.Find(@event.SavedEventId);
            if (savedEvent == null)
            {
                throw new Exception();
            }

            var integrationEvent =
                JsonConvert.DeserializeObject(savedEvent.Content, GetEventTypeByName(savedEvent.EventName));
            IntegrationEvent evt = (IntegrationEvent) integrationEvent;
            try
            {
                _logger.LogInformation(
                    "----- Publishing integration event: {IntegrationEventId} from RFIDScannedIntegrationEventHandler - ({@IntegrationEvent})",
                    evt.Id, evt);
                evt.CheckForCustomisation = false;
                _eventBus.Publish(evt);
                _context.SavedEvent.Remove(savedEvent);
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "ERROR Publishing integration event: {IntegrationEventId} from RFIDScannedIntegrationEventHandler",
                    evt.Id);

                throw;
            }
        }

        private Type GetEventTypeByName(string eventName) => types.SingleOrDefault(t => t.Name == eventName);
    }
}