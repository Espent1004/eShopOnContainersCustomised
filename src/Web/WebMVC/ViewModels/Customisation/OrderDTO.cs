using System;

namespace Microsoft.eShopOnContainers.WebMVC.ViewModels.Customisation
{
    public class OrderDTO
    {

        public int OrderId { get; set; }
        public String OrderNumber { get; set; }
        public String SavedEventId { get; set; }
        public bool RFIDScanned { get; set; }
    }
}