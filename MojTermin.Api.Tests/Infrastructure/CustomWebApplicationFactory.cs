using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MojTermin.Api.Domain.Entities;
using MojTermin.Api.Domain.Enums;
using MojTermin.Api.Infrastructure.Data;

namespace MojTermin.Api.Tests.Infrastructure;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            // These overrides force a deterministic test environment regardless of
            // what is in appsettings, user secrets, or environment variables on
            // the developer's machine. Notifications must be disabled so the email
            // path writes a synchronous "Skipped" log instead of asynchronously
            // attempting (and failing) to talk to a real SMTP server.
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimiter:Disabled"] = "true",
                ["Notifications:Enabled"] = "false",
                ["Notifications:SenderEmail"] = "",
                ["Notifications:SmtpHost"] = "",
                ["Notifications:SmtpUsername"] = "",
                ["Notifications:SmtpPassword"] = ""
            });
        });
        builder.ConfigureServices(services =>
        {
            var databaseName = $"MojTerminTests-{Guid.NewGuid()}";
            var dbDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<MojTerminDbContext>));
            if (dbDescriptor is not null)
            {
                services.Remove(dbDescriptor);
            }

            services.AddDbContext<MojTerminDbContext>(options =>
            {
                options.UseInMemoryDatabase(databaseName);
            });

            var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MojTerminDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AppUser>>();
            db.Database.EnsureCreated();

            if (!db.Businesses.Any())
            {
                var businessId = Guid.NewGuid();
                var business = new Business
                {
                    Id = businessId,
                    Name = "Test Salon",
                    Slug = "test-salon",
                    BusinessType = BusinessType.BeautySalon,
                    Phone = "123",
                    Email = "test@local",
                    Address = "Test",
                    Description = "Test",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };

                var user = new AppUser
                {
                    Id = Guid.NewGuid(),
                    BusinessId = businessId,
                    FullName = "Owner",
                    Email = "owner@local.test",
                    Username = "owner",
                    Role = "Owner",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    // Seeded test owner is "already verified" so existing login
                    // integration tests don't have to walk through the strict
                    // email-verification flow we now enforce on /api/auth/login.
                    EmailVerified = true,
                    EmailVerifiedAtUtc = DateTime.UtcNow
                };
                user.PasswordHash = hasher.HashPassword(user, "Owner123!");

                db.Businesses.Add(business);
                db.AppUsers.Add(user);

                var serviceId = Guid.NewGuid();
                db.Services.Add(new Service
                {
                    Id = serviceId,
                    BusinessId = businessId,
                    Name = "Test usluga",
                    Description = null,
                    DurationMinutes = 30,
                    Price = 25,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                });

                db.WorkingHours.AddRange(
                    new WorkingHour { Id = Guid.NewGuid(), BusinessId = businessId, DayOfWeek = DayOfWeek.Monday, OpenTime = new TimeSpan(8, 0, 0), CloseTime = new TimeSpan(16, 0, 0), IsClosed = false },
                    new WorkingHour { Id = Guid.NewGuid(), BusinessId = businessId, DayOfWeek = DayOfWeek.Tuesday, OpenTime = new TimeSpan(8, 0, 0), CloseTime = new TimeSpan(16, 0, 0), IsClosed = false },
                    new WorkingHour { Id = Guid.NewGuid(), BusinessId = businessId, DayOfWeek = DayOfWeek.Wednesday, OpenTime = new TimeSpan(8, 0, 0), CloseTime = new TimeSpan(16, 0, 0), IsClosed = false },
                    new WorkingHour { Id = Guid.NewGuid(), BusinessId = businessId, DayOfWeek = DayOfWeek.Thursday, OpenTime = new TimeSpan(8, 0, 0), CloseTime = new TimeSpan(16, 0, 0), IsClosed = false },
                    new WorkingHour { Id = Guid.NewGuid(), BusinessId = businessId, DayOfWeek = DayOfWeek.Friday, OpenTime = new TimeSpan(8, 0, 0), CloseTime = new TimeSpan(16, 0, 0), IsClosed = false },
                    new WorkingHour { Id = Guid.NewGuid(), BusinessId = businessId, DayOfWeek = DayOfWeek.Saturday, OpenTime = new TimeSpan(9, 0, 0), CloseTime = new TimeSpan(14, 0, 0), IsClosed = false },
                    new WorkingHour { Id = Guid.NewGuid(), BusinessId = businessId, DayOfWeek = DayOfWeek.Sunday, OpenTime = TimeSpan.Zero, CloseTime = TimeSpan.Zero, IsClosed = true });

                db.SaveChanges();
            }
        });
    }
}
