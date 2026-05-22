using System.Threading.RateLimiting;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using MojTermin.Api.Application.Interfaces;
using MojTermin.Api.API.Middleware;
using MojTermin.Api.Domain.Entities;
using MojTermin.Api.Domain.Enums;
using MojTermin.Api.Infrastructure.Data;
using MojTermin.Api.Infrastructure.Services;

var builder = WebApplication.CreateBuilder(args);

// Sentry: wire up only when a DSN is configured so local dev (no DSN) stays
// 100% offline. When present, Sentry hooks into the request pipeline below
// AND auto-captures unhandled exceptions, background-service throws, and
// configured-level logs.
var sentryDsn = builder.Configuration["Sentry:Dsn"];
if (!string.IsNullOrWhiteSpace(sentryDsn))
{
    var sentryConfig = builder.Configuration.GetSection(MojTermin.Api.Infrastructure.Services.SentryOptions.SectionName)
        .Get<MojTermin.Api.Infrastructure.Services.SentryOptions>() ?? new MojTermin.Api.Infrastructure.Services.SentryOptions();
    builder.WebHost.UseSentry(options =>
    {
        options.Dsn = sentryDsn;
        options.Environment = sentryConfig.Environment ?? builder.Environment.EnvironmentName;
        options.TracesSampleRate = sentryConfig.TracesSampleRate;
        options.AttachStacktrace = true;
        // Strip PII at SDK level — we do NOT want booking emails or names
        // ending up in error reports. Frame metadata is enough for triage.
        options.SendDefaultPii = false;
    });
}

var configuredCorsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
if (!builder.Environment.IsDevelopment() && configuredCorsOrigins.Length == 0)
{
    throw new InvalidOperationException("Cors:AllowedOrigins mora biti konfigurisan u produkciji.");
}

builder.Services.AddControllers();
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var details = context.ModelState
            .Where(x => x.Value?.Errors.Count > 0)
            .ToDictionary(
                x => x.Key,
                x => x.Value!.Errors.Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage) ? "Neispravna vrijednost." : error.ErrorMessage).ToArray());

        var payload = new
        {
            code = "validation_error",
            message = "Validacija nije prošla.",
            details,
            traceId = context.HttpContext.TraceIdentifier
        };

        return new BadRequestObjectResult(payload);
    };
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendPolicy", policy =>
    {
        if (configuredCorsOrigins.Length > 0)
        {
            policy
                .WithOrigins(configuredCorsOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod();
            return;
        }

        if (builder.Environment.IsDevelopment())
        {
            policy
                .WithOrigins("http://localhost:4200", "http://127.0.0.1:4200")
                .AllowAnyHeader()
                .AllowAnyMethod();
            return;
        }

        // In production, fail-safe to no cross-origin access unless explicitly configured.
        policy.WithOrigins("https://invalid.localhost");
    });
});
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.SectionName));
builder.Services.Configure<NotificationOptions>(builder.Configuration.GetSection(NotificationOptions.SectionName));
builder.Services.Configure<ClientAppOptions>(builder.Configuration.GetSection(ClientAppOptions.SectionName));
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SecretKey)),
            ClockSkew = TimeSpan.FromSeconds(30),
            RoleClaimType = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role"
        };
    });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("OwnerOnly", policy => policy.RequireRole("Owner"));
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("auth", limiterOptions =>
    {
        limiterOptions.PermitLimit = 10;
        limiterOptions.Window = TimeSpan.FromMinutes(1);
        limiterOptions.QueueLimit = 0;
    });
    options.AddFixedWindowLimiter("api", limiterOptions =>
    {
        limiterOptions.PermitLimit = 120;
        limiterOptions.Window = TimeSpan.FromMinutes(1);
        limiterOptions.QueueLimit = 0;
    });
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var key = context.User.Identity?.IsAuthenticated == true
            ? context.User.Identity.Name ?? "auth-user"
            : context.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 240,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
    });
});
builder.Services.AddScoped<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<INotificationService, EmailNotificationService>();
// EmailQueue is a singleton work channel; EmailDispatcherHostedService drains
// it in the background so SMTP latency cannot block HTTP requests.
builder.Services.AddSingleton<EmailQueue>();
builder.Services.AddHostedService<EmailDispatcherHostedService>();
// Singleton because the configured zone never changes during the process lifetime
// and resolving it once avoids re-parsing the timezone database on every request.
builder.Services.AddSingleton<BusinessTimeProvider>();
builder.Services.AddHostedService<AppointmentAutoCompleteService>();
builder.Services.AddHostedService<AppointmentReminderService>();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "MojTermin API", Version = "v1" });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Unesi JWT token u formatu: Bearer {token}"
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentBusinessService, CurrentBusinessService>();
builder.Services.AddScoped<IAdminAuditService, AdminAuditService>();
builder.Services.AddHealthChecks();

// When the API runs behind a reverse proxy (Nginx, IIS, Azure App Service, Cloud Front)
// Request.Scheme and Request.Host must reflect the public-facing values, not the proxy's
// internal hop. Without this, generated URLs (e.g. uploaded logo) come out as http:// and
// HttpsRedirection/HSTS short-circuits incorrectly.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // KnownNetworks/KnownProxies are left to deployer config; default trusts loopback only.
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddDbContext<MojTerminDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sqlOptions =>
        {
            // Transparently retry transient SQL errors (network blips, Azure SQL
            // maintenance, deadlock victims). 3 retries, max 10s between attempts.
            sqlOptions.EnableRetryOnFailure(
                maxRetryCount: 3,
                maxRetryDelay: TimeSpan.FromSeconds(10),
                errorNumbersToAdd: null);
        }));

var app = builder.Build();
var jwt = app.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
var notifications = app.Configuration.GetSection(NotificationOptions.SectionName).Get<NotificationOptions>() ?? new NotificationOptions();
var connectionString = app.Configuration.GetConnectionString("DefaultConnection");
var looksLikeUnsafeSecret =
    jwt.SecretKey.Contains("Change_Immediately", StringComparison.OrdinalIgnoreCase) ||
    jwt.SecretKey.Contains("dev-localonly", StringComparison.OrdinalIgnoreCase) ||
    jwt.SecretKey.Contains("changeme", StringComparison.OrdinalIgnoreCase);
if (!app.Environment.IsDevelopment() && (string.IsNullOrWhiteSpace(jwt.SecretKey) || jwt.SecretKey.Length < 32 || looksLikeUnsafeSecret))
{
    throw new InvalidOperationException(
        "Jwt:SecretKey mora biti sigurno postavljen u produkciji preko env-var Jwt__SecretKey " +
        "(min 32 karaktera, bez dev/default vrijednosti).");
}

if (!app.Environment.IsDevelopment() && string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("ConnectionStrings:DefaultConnection mora biti postavljen u produkciji.");
}

if (!app.Environment.IsDevelopment() && notifications.Enabled)
{
    var missingSmtpConfig = string.IsNullOrWhiteSpace(notifications.SenderEmail) ||
                            string.IsNullOrWhiteSpace(notifications.SmtpHost) ||
                            string.IsNullOrWhiteSpace(notifications.SmtpUsername) ||
                            string.IsNullOrWhiteSpace(notifications.SmtpPassword);
    if (missingSmtpConfig)
    {
        throw new InvalidOperationException("Notifications su uključene, ali SMTP konfiguracija nije kompletna.");
    }
}

if (notifications.Enabled)
{
    var host = notifications.SmtpHost ?? string.Empty;
    var sender = notifications.SenderEmail ?? string.Empty;
    var hostLooksLikeGmail = host.Contains("gmail", StringComparison.OrdinalIgnoreCase);
    var senderLooksLikeGmail = sender.Contains("@gmail.", StringComparison.OrdinalIgnoreCase);
    if (hostLooksLikeGmail && !senderLooksLikeGmail)
    {
        app.Logger.LogWarning(
            "SMTP host je Gmail, ali Notifications:SenderEmail ({SenderEmail}) nije Gmail adresa. " +
            "Poruke često završe u spamu dok ne koristite SMTP provajdera za vašu domenu (npr. Resend) i SPF/DKIM za {SenderEmail}.",
            sender,
            sender);
    }
}

// ForwardedHeaders MUST run before everything that consumes Scheme/Host/RemoteIp.
app.UseForwardedHeaders();

app.UseMiddleware<GlobalExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    // HSTS: tell browsers to pin HTTPS for 60 days. Only enabled in non-dev because
    // local development typically runs http://.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseCors("FrontendPolicy");

// Sentry request tracing — only attaches when the SDK was initialised above.
if (!string.IsNullOrWhiteSpace(sentryDsn))
{
    app.UseSentryTracing();
}

// Rate limiter is bypassed in integration tests so that sequential test runs do not
// hit per-minute caps. Production and developer runs always have it enabled.
var rateLimiterDisabled = string.Equals(
    app.Configuration["RateLimiter:Disabled"],
    "true",
    StringComparison.OrdinalIgnoreCase);
if (!rateLimiterDisabled)
{
    app.UseRateLimiter();
}
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health");

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<MojTerminDbContext>();
    var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AppUser>>();

    if (dbContext.Database.IsRelational())
    {
        dbContext.Database.Migrate();
    }
    else
    {
        dbContext.Database.EnsureCreated();
    }

    // Demo seed (business, owner, services, working hours) runs ONLY in Development.
    // Production tenants: keep Auth:AllowPublicRegistration=false and onboard manually
    // (see README). When you temporarily enable registration to create a tenant,
    // use POST /api/businesses/register then turn the flag off again.
    if (!app.Environment.IsDevelopment())
    {
        goto endSeed;
    }

    var demoBusiness = dbContext.Businesses.FirstOrDefault(x => x.Slug == "demo-salon");
    if (demoBusiness is null)
    {
        demoBusiness = new Business
        {
            Id = Guid.NewGuid(),
            Name = "MojTermin Demo Salon",
            Slug = "demo-salon",
            BusinessType = BusinessType.BeautySalon,
            Phone = "+387 61 111 222",
            Email = "demo@mojtermin.ai",
            Address = "Zmaja od Bosne 12, Sarajevo",
            Description = "Moderan salon za šišanje, bojenje i stilizovanje sa online rezervacijama.",
            CoverImageUrl = "https://images.unsplash.com/photo-1521590832167-7bcbfaa6381f?w=1600&q=80&auto=format&fit=crop",
            ThemePreset = "beauty",
            PrimaryColor = "#7c3aed",
            SecondaryColor = "#ec4899",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        dbContext.Businesses.Add(demoBusiness);
    }
    else
    {
        demoBusiness.Name = "MojTermin Demo Salon";
        demoBusiness.BusinessType = BusinessType.BeautySalon;
        demoBusiness.Phone = "+387 61 111 222";
        demoBusiness.Email = "demo@mojtermin.ai";
        demoBusiness.Address = "Zmaja od Bosne 12, Sarajevo";
        demoBusiness.Description = "Moderan salon za šišanje, bojenje i stilizovanje sa online rezervacijama.";
        demoBusiness.CoverImageUrl = "https://images.unsplash.com/photo-1521590832167-7bcbfaa6381f?w=1600&q=80&auto=format&fit=crop";
        demoBusiness.ThemePreset = "beauty";
        demoBusiness.PrimaryColor = "#7c3aed";
        demoBusiness.SecondaryColor = "#ec4899";
        demoBusiness.IsActive = true;
    }

    dbContext.SaveChanges();

    var ownerUser = dbContext.AppUsers.FirstOrDefault(x => x.BusinessId == demoBusiness.Id && x.Role == "Owner");
    if (ownerUser is null)
    {
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            BusinessId = demoBusiness.Id,
            FullName = "Demo Owner",
            Email = "owner@mojtermin.ai",
            Username = "owner",
            IsActive = true,
            Role = "Owner",
            CreatedAt = DateTime.UtcNow,
            // Demo owner is pre-verified so devs can log in without going
            // through the email-verification flow on a fresh DB.
            EmailVerified = true,
            EmailVerifiedAtUtc = DateTime.UtcNow
        };
        user.PasswordHash = hasher.HashPassword(user, "Owner123!");
        dbContext.AppUsers.Add(user);
    }
    else if (!ownerUser.EmailVerified)
    {
        // Older DBs (before the strict-verification migration ran) will have
        // the demo owner with EmailVerified=false because the new column
        // defaults to 0. Flip it on so the demo nalog ne ostane zaključan.
        ownerUser.EmailVerified = true;
        ownerUser.EmailVerifiedAtUtc = DateTime.UtcNow;
    }

    // Demo service images use stable Unsplash photo IDs (free license).
    // If any of them is removed upstream, business-page.component.ts has an
    // onError fallback that swaps in the local SVG, so the page never breaks.
    var demoServices = new[]
    {
        new { Name = "Muško šišanje", Description = "Precizno šišanje mašinicom i makazama uz završno stilizovanje.", DurationMinutes = 35, Price = 18m, ImageUrl = "https://images.unsplash.com/photo-1582771498000-8ad44e6c84db?w=800&q=80&auto=format&fit=crop" },
        new { Name = "Žensko šišanje", Description = "Konsultacija, šišanje, feniranje i osnovno stilizovanje.", DurationMinutes = 70, Price = 35m, ImageUrl = "https://images.unsplash.com/photo-1634449571010-02389ed0f9b0?w=800&q=80&auto=format&fit=crop" },
        new { Name = "Farbanje kose", Description = "Profesionalno farbanje sa završnom njegom i sjajem.", DurationMinutes = 100, Price = 65m, ImageUrl = "https://images.unsplash.com/photo-1605980766335-d3a41c7332a1?w=800&q=80&auto=format&fit=crop" }
    };
    var existingServices = dbContext.Services.Where(x => x.BusinessId == demoBusiness.Id).ToList();
    foreach (var template in demoServices)
    {
        var service = existingServices.FirstOrDefault(x => x.Name == template.Name);
        if (service is null)
        {
            dbContext.Services.Add(new Service
            {
                Id = Guid.NewGuid(),
                BusinessId = demoBusiness.Id,
                Name = template.Name,
                Description = template.Description,
                ImageUrl = template.ImageUrl,
                DurationMinutes = template.DurationMinutes,
                Price = template.Price,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
            continue;
        }

        service.Description = template.Description;
        service.ImageUrl = template.ImageUrl;
        service.DurationMinutes = template.DurationMinutes;
        service.Price = template.Price;
        service.IsActive = true;
    }

    var demoWorkingHours = new[]
    {
        new { Day = DayOfWeek.Monday, Open = new TimeSpan(9, 0, 0), Close = new TimeSpan(19, 0, 0), IsClosed = false },
        new { Day = DayOfWeek.Tuesday, Open = new TimeSpan(9, 0, 0), Close = new TimeSpan(19, 0, 0), IsClosed = false },
        new { Day = DayOfWeek.Wednesday, Open = new TimeSpan(9, 0, 0), Close = new TimeSpan(19, 0, 0), IsClosed = false },
        new { Day = DayOfWeek.Thursday, Open = new TimeSpan(9, 0, 0), Close = new TimeSpan(19, 0, 0), IsClosed = false },
        new { Day = DayOfWeek.Friday, Open = new TimeSpan(9, 0, 0), Close = new TimeSpan(19, 0, 0), IsClosed = false },
        new { Day = DayOfWeek.Saturday, Open = new TimeSpan(9, 0, 0), Close = new TimeSpan(15, 0, 0), IsClosed = false },
        new { Day = DayOfWeek.Sunday, Open = new TimeSpan(7, 0, 0), Close = new TimeSpan(12, 0, 0), IsClosed = true }
    };
    var existingWorkingHours = dbContext.WorkingHours.Where(x => x.BusinessId == demoBusiness.Id).ToList();
    foreach (var template in demoWorkingHours)
    {
        var workingHour = existingWorkingHours.FirstOrDefault(x => x.DayOfWeek == template.Day);
        if (workingHour is null)
        {
            dbContext.WorkingHours.Add(new WorkingHour
            {
                Id = Guid.NewGuid(),
                BusinessId = demoBusiness.Id,
                DayOfWeek = template.Day,
                OpenTime = template.Open,
                CloseTime = template.Close,
                IsClosed = template.IsClosed
            });
            continue;
        }

        workingHour.OpenTime = template.Open;
        workingHour.CloseTime = template.Close;
        workingHour.IsClosed = template.IsClosed;
    }

    dbContext.SaveChanges();

    endSeed: ;
}

app.Run();

public partial class Program;
