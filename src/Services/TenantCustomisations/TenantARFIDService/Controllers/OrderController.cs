using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopOnContainers.BuildingBlocks.EventBus.Abstractions;
using Microsoft.Extensions.Logging;
using TenantARFIDService.Database;
using TenantARFIDService.IntegrationEvents.Events;
using TenantARFIDService.Model;

namespace TenantARFIDService.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class OrderController : ControllerBase
    {
        private readonly TenantAContext _context;
        private readonly IEventBus _eventBus;
        private readonly ILogger<OrderController> _logger;

        public OrderController(TenantAContext context, IEventBus eventBus, ILogger<OrderController> logger)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        //GET ALL
        // GET: api/order
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Order>>> GetOrders()
        {
            return await _context.Orders.ToListAsync();
        }

        //SET SCANNED
        // PUT: api/Order/5
        [HttpPut("{id}")]
        public async Task<IActionResult> PutOrder(int id)
        {
            Order order = await _context.Orders.FindAsync(id);

            if (order.RFIDScanned)
            {
                return BadRequest();
            }

            order.RFIDScanned = true;
            await _context.SaveChangesAsync();
            RFIDScannedIntegrationEvent rfidScannedIntegrationEvent = new RFIDScannedIntegrationEvent();
            rfidScannedIntegrationEvent.OrderNumber = order.OrderNumber;
            rfidScannedIntegrationEvent.SavedEventId = order.SavedEventId;
            rfidScannedIntegrationEvent.CheckForCustomisation = false;

            try
            {
                _logger.LogInformation(
                    "----- Publishing integration event: {IntegrationEventId} from SavedEventsController - ({@IntegrationEvent})",
                    rfidScannedIntegrationEvent.Id, rfidScannedIntegrationEvent);
                _eventBus.Publish(rfidScannedIntegrationEvent);
            }
            catch (Exception e)
            {
                _logger.LogError(e,
                    "ERROR Publishing integration event: {IntegrationEventId} from OrderStatusChangedToSubmittedIntegrationEventsController",
                    rfidScannedIntegrationEvent.Id);
                throw;
            }

            return NoContent();
        }
    }
}