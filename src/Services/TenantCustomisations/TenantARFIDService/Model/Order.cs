using System;

namespace TenantARFIDService.Model
{
    public class Order
    {

        public int OrderId { get; set; }
        public String OrderNumber { get; set; }
        public String SavedEventId { get; set; }
        public bool RFIDScanned { get; set; }
    }
}