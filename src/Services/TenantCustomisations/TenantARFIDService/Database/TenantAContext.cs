using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using TenantARFIDService.Model;

namespace TenantARFIDService.Database
{
    public class TenantAContext : DbContext
    {
        public TenantAContext(DbContextOptions<TenantAContext> options)
            : base(options)
        {
        }

        public DbSet<Order> Orders { get; set; }
        

    }
    public class TenantAContextDesignFactory : IDesignTimeDbContextFactory<TenantAContext>
    {
        public TenantAContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<TenantAContext>()
                            .UseSqlServer("Server=.;Initial Catalog=Microsoft.eShopOnContainers.Services.TenantARFIDDb;Integrated Security=true");

            return new TenantAContext(optionsBuilder.Options);
        }
    }
}
