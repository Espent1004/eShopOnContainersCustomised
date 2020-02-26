using System;
using System.Linq;
using TenantARFIDService.Model;

namespace TenantARFIDService.Database
{
    public class DbInitializer
    {
        public void Initialize(TenantAContext context)
        {
            context.Database.EnsureCreated();

            if (context.Orders.Any())
            {
                return;
            }

            Order order = new Order();
            order.OrderNumber = "9999999";
            context.Orders.Add(order);
            context.SaveChanges();

        }
    }
}
