using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.IO;
using Wintime.Control.Infrastructure.Data;

namespace Wintime.Control.Infrastructure.Migrations
{
    public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ControlDbContext>
    {
        public ControlDbContext CreateDbContext(string[] args)
        {
            // Получаем строку подключения из appsettings.json
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json")
                .Build();

            var connectionString = configuration.GetConnectionString("DefaultConnection") ?? 
                                 "Host=localhost;Database=WintimeControlDb;Username=postgres;Password=password";

            var optionsBuilder = new DbContextOptionsBuilder<ControlDbContext>();
            optionsBuilder.UseNpgsql(connectionString);

            return new ControlDbContext(optionsBuilder.Options);
        }
    }
}