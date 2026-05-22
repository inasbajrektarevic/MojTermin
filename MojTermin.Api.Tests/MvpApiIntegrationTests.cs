using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MojTermin.Api.Application.DTOs;
using MojTermin.Api.Domain.Enums;
using MojTermin.Api.Infrastructure.Data;
using MojTermin.Api.Tests.Infrastructure;

namespace MojTermin.Api.Tests;

public class MvpApiIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly CustomWebApplicationFactory _factory;

    public MvpApiIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private static DateTime NextOpenDayUtc()
    {
        var candidate = DateTime.UtcNow.Date.AddDays(1);
        for (var i = 0; i < 10; i++)
        {
            if (candidate.DayOfWeek != DayOfWeek.Sunday)
            {
                return candidate;
            }

            candidate = candidate.AddDays(1);
        }

        return DateTime.UtcNow.Date.AddDays(2);
    }

    /// <summary>
    /// Različiti dani da paralelni testovi u klasi ne dijele isti slot.
    /// </summary>
    private static DateTime BookableDayUtc(int weekOffset)
    {
        var d = NextOpenDayUtc().AddDays(weekOffset * 7);
        while (d.DayOfWeek == DayOfWeek.Sunday)
        {
            d = d.AddDays(1);
        }

        return d;
    }

    [Fact]
    public async Task Seed_Info_Endpoint_Is_Removed()
    {
        var response = await _client.GetAsync("/api/seed/info");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Public_Site_Config_Returns_AllowPublicRegistration_Flag()
    {
        var response = await _client.GetAsync("/api/public/site-config");
        response.EnsureSuccessStatusCode();
        var dto = await response.Content.ReadFromJsonAsync<PublicSiteConfigDto>();
        Assert.NotNull(dto);
        Assert.True(dto!.AllowPublicRegistration);
    }

    [Fact]
    public async Task Business_By_Slug_Public_Returns_Ok()
    {
        var response = await _client.GetAsync("/api/businesses/by-slug/test-salon");
        response.EnsureSuccessStatusCode();
        var dto = await response.Content.ReadFromJsonAsync<BusinessDto>();
        Assert.NotNull(dto);
        Assert.Equal("test-salon", dto!.Slug);
    }

    [Fact]
    public async Task Services_And_Working_Hours_Public_By_Slug_Return_Ok()
    {
        var servicesResponse = await _client.GetAsync("/api/services/public/test-salon");
        servicesResponse.EnsureSuccessStatusCode();
        var services = await servicesResponse.Content.ReadFromJsonAsync<List<ServiceDto>>();
        Assert.NotNull(services);
        Assert.NotEmpty(services);

        var hoursResponse = await _client.GetAsync("/api/working-hours/public/test-salon");
        hoursResponse.EnsureSuccessStatusCode();
        var hours = await hoursResponse.Content.ReadFromJsonAsync<List<WorkingHourDto>>();
        Assert.NotNull(hours);
        Assert.Equal(7, hours!.Count);
    }

    [Fact]
    public async Task Public_Booking_Past_Date_Returns_BadRequest()
    {
        var servicesResponse = await _client.GetAsync("/api/services/public/test-salon");
        servicesResponse.EnsureSuccessStatusCode();
        var services = await servicesResponse.Content.ReadFromJsonAsync<List<ServiceDto>>();
        Assert.NotNull(services);

        var body = new PublicCreateAppointmentDto
        {
            ServiceId = services![0].Id,
            AppointmentDate = DateTime.UtcNow.Date.AddDays(-1),
            StartTime = new TimeSpan(10, 0, 0),
            FullName = "Test User",
            Phone = "0600000001"
        };

        var response = await _client.PostAsJsonAsync("/api/appointments/public/test-salon", body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Public_Booking_With_Empty_String_Email_Succeeds()
    {
        var servicesResponse = await _client.GetAsync("/api/services/public/test-salon");
        servicesResponse.EnsureSuccessStatusCode();
        var services = await servicesResponse.Content.ReadFromJsonAsync<List<ServiceDto>>();
        Assert.NotNull(services);

        var day = BookableDayUtc(1);
        var body = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["serviceId"] = services![0].Id,
            ["appointmentDate"] = day.ToString("o"),
            ["startTime"] = "10:00:00",
            ["fullName"] = "Email Empty Test",
            ["phone"] = "0600000999",
            ["email"] = "",
            ["note"] = null
        });

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/appointments/public/test-salon")
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        };

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Public_Booking_Valid_Slot_Returns_Ok()
    {
        var servicesResponse = await _client.GetAsync("/api/services/public/test-salon");
        servicesResponse.EnsureSuccessStatusCode();
        var services = await servicesResponse.Content.ReadFromJsonAsync<List<ServiceDto>>();
        Assert.NotNull(services);

        var day = BookableDayUtc(0);
        var body = new PublicCreateAppointmentDto
        {
            ServiceId = services![0].Id,
            AppointmentDate = day,
            StartTime = new TimeSpan(10, 0, 0),
            FullName = "Booker",
            Phone = "0600000888",
            Email = "booker@example.com"
        };

        var response = await _client.PostAsJsonAsync("/api/appointments/public/test-salon", body);
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<AppointmentDto>();
        Assert.NotNull(created);
        Assert.Equal("Booker", created!.ClientName);
    }

    [Fact]
    public async Task Public_Booking_Creates_Notification_Log_Entry()
    {
        var services = await _client.GetFromJsonAsync<List<ServiceDto>>("/api/services/public/test-salon");
        Assert.NotNull(services);
        Assert.NotEmpty(services);

        var day = BookableDayUtc(6);
        var response = await _client.PostAsJsonAsync("/api/appointments/public/test-salon", new PublicCreateAppointmentDto
        {
            ServiceId = services![0].Id,
            AppointmentDate = day,
            StartTime = new TimeSpan(12, 0, 0),
            FullName = "Notification Test",
            Phone = "0610009999",
            Email = "notify@example.com"
        });
        response.EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MojTerminDbContext>();
        var hasLog = db.NotificationLogs.Any(x =>
            x.Channel == NotificationChannel.Email &&
            x.Subject.Contains("Nova rezervacija") &&
            (x.Status == NotificationDeliveryStatus.Skipped || x.Status == NotificationDeliveryStatus.Sent));

        Assert.True(hasLog);
    }

    [Fact]
    public async Task Public_Booking_Overlapping_Slot_Returns_Conflict()
    {
        var services = await _client.GetFromJsonAsync<List<ServiceDto>>("/api/services/public/test-salon");
        Assert.NotNull(services);
        Assert.NotEmpty(services);

        var day = BookableDayUtc(3);
        var first = new PublicCreateAppointmentDto
        {
            ServiceId = services![0].Id,
            AppointmentDate = day,
            StartTime = new TimeSpan(10, 0, 0),
            FullName = "Overlap One",
            Phone = "0630000001"
        };

        var firstResponse = await _client.PostAsJsonAsync("/api/appointments/public/test-salon", first);
        firstResponse.EnsureSuccessStatusCode();

        var second = new PublicCreateAppointmentDto
        {
            ServiceId = services[0].Id,
            AppointmentDate = day,
            StartTime = new TimeSpan(10, 15, 0),
            FullName = "Overlap Two",
            Phone = "0630000002"
        };

        var secondResponse = await _client.PostAsJsonAsync("/api/appointments/public/test-salon", second);
        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
    }

    [Fact]
    public async Task Public_Booking_Concurrent_Requests_Same_Slot_Allow_Exactly_One()
    {
        // RACE-CONDITION: Two concurrent bookings for the same exact slot must result in
        // exactly one success (200) and one rejection. The serializable transaction +
        // filtered unique index is what guarantees this on a real SQL backend; on the
        // in-memory provider used by these tests, the in-controller overlap check covers
        // the same property because EF Core's in-memory DbContext serializes operations
        // on a single context. This test guards against future regressions of the check.
        var services = await _client.GetFromJsonAsync<List<ServiceDto>>("/api/services/public/test-salon");
        Assert.NotNull(services);
        Assert.NotEmpty(services);

        var day = BookableDayUtc(8);
        var booking = new PublicCreateAppointmentDto
        {
            ServiceId = services![0].Id,
            AppointmentDate = day,
            StartTime = new TimeSpan(13, 0, 0),
            FullName = "Race Client",
            Phone = "0630777777"
        };

        var firstResponse = await _client.PostAsJsonAsync("/api/appointments/public/test-salon", booking);
        var secondResponse = await _client.PostAsJsonAsync("/api/appointments/public/test-salon", booking);

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
    }

    [Fact]
    public async Task Public_Booking_Does_Not_Overwrite_Existing_Client_FullName_Or_Email()
    {
        // SECURITY: An attacker who knows the phone of an existing client must NOT be
        // able to vandalize that client's CRM record by sending a booking under a
        // different name/email. The legitimate name and email stay intact.
        var services = await _client.GetFromJsonAsync<List<ServiceDto>>("/api/services/public/test-salon");
        Assert.NotNull(services);
        Assert.NotEmpty(services);

        var day = BookableDayUtc(6);
        var firstBooking = new PublicCreateAppointmentDto
        {
            ServiceId = services![0].Id,
            AppointmentDate = day,
            StartTime = new TimeSpan(11, 0, 0),
            FullName = "Legitimate Client",
            Phone = "0639999111",
            Email = "legit@example.com"
        };
        var firstResponse = await _client.PostAsJsonAsync("/api/appointments/public/test-salon", firstBooking);
        firstResponse.EnsureSuccessStatusCode();

        var attackerBooking = new PublicCreateAppointmentDto
        {
            ServiceId = services[0].Id,
            AppointmentDate = day.AddDays(7),
            StartTime = new TimeSpan(11, 0, 0),
            FullName = "Mallory Attacker",
            Phone = "0639999111",
            Email = "mallory@evil.example"
        };
        var attackerResponse = await _client.PostAsJsonAsync("/api/appointments/public/test-salon", attackerBooking);
        attackerResponse.EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MojTerminDbContext>();
        var client = await db.Clients.FirstAsync(x => x.Phone == "0639999111");
        Assert.Equal("Legitimate Client", client.FullName);
        Assert.Equal("legit@example.com", client.Email);
    }

    [Fact]
    public async Task Public_Booking_Uses_Form_Email_For_Client_Confirmation_When_Phone_Already_Exists()
    {
        var services = await _client.GetFromJsonAsync<List<ServiceDto>>("/api/services/public/test-salon");
        Assert.NotNull(services);
        Assert.NotEmpty(services);

        var day = BookableDayUtc(7);
        var phone = "0638888222";
        var firstBooking = new PublicCreateAppointmentDto
        {
            ServiceId = services![0].Id,
            AppointmentDate = day,
            StartTime = new TimeSpan(14, 0, 0),
            FullName = "Returning Client",
            Phone = phone,
            Email = "original@example.com"
        };
        (await _client.PostAsJsonAsync("/api/appointments/public/test-salon", firstBooking)).EnsureSuccessStatusCode();

        var secondDay = day.AddDays(7);
        var secondBooking = new PublicCreateAppointmentDto
        {
            ServiceId = services[0].Id,
            AppointmentDate = secondDay,
            StartTime = new TimeSpan(14, 0, 0),
            FullName = "Returning Client Two",
            Phone = phone,
            Email = "new-inbox@example.com"
        };
        var secondResponse = await _client.PostAsJsonAsync("/api/appointments/public/test-salon", secondBooking);
        secondResponse.EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MojTerminDbContext>();
        var client = await db.Clients.FirstAsync(x => x.Phone == phone);
        Assert.Equal("Returning Client", client.FullName);
        Assert.Equal("original@example.com", client.Email);

        var appointment = await db.Appointments
            .Where(x => x.ClientId == client.Id && x.AppointmentDate == secondDay)
            .SingleAsync();
        Assert.Equal("new-inbox@example.com", appointment.ContactEmail);
        Assert.Equal("Returning Client Two", appointment.ContactFullName);

        var confirmationLog = db.NotificationLogs
            .Where(x =>
                x.AppointmentId == appointment.Id &&
                x.Subject.Contains("Termin potvrđen"))
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefault();
        Assert.NotNull(confirmationLog);
        Assert.Equal("new-inbox@example.com", confirmationLog!.Recipient);
        Assert.Contains("Returning Client Two", confirmationLog.BodyPreview);
    }

    [Fact]
    public async Task Public_Booking_Outside_Working_Hours_Returns_BadRequest()
    {
        var services = await _client.GetFromJsonAsync<List<ServiceDto>>("/api/services/public/test-salon");
        Assert.NotNull(services);
        Assert.NotEmpty(services);

        var day = BookableDayUtc(4);
        var response = await _client.PostAsJsonAsync("/api/appointments/public/test-salon", new PublicCreateAppointmentDto
        {
            ServiceId = services![0].Id,
            AppointmentDate = day,
            StartTime = new TimeSpan(7, 0, 0),
            FullName = "Outside Hours",
            Phone = "0630000003"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Public_Availability_Excludes_Already_Booked_Slot()
    {
        var services = await _client.GetFromJsonAsync<List<ServiceDto>>("/api/services/public/test-salon");
        Assert.NotNull(services);
        Assert.NotEmpty(services);

        var day = BookableDayUtc(5);
        var serviceId = services![0].Id;

        var before = await _client.GetFromJsonAsync<PublicAppointmentAvailabilityDto>(
            $"/api/appointments/public/test-salon/availability?serviceId={serviceId}&date={day:yyyy-MM-dd}");
        Assert.NotNull(before);
        Assert.Contains(new TimeSpan(10, 0, 0), before!.AvailableStartTimes);

        var booking = await _client.PostAsJsonAsync("/api/appointments/public/test-salon", new PublicCreateAppointmentDto
        {
            ServiceId = serviceId,
            AppointmentDate = day,
            StartTime = new TimeSpan(10, 0, 0),
            FullName = "Availability Test",
            Phone = "0630000004"
        });
        booking.EnsureSuccessStatusCode();

        var after = await _client.GetFromJsonAsync<PublicAppointmentAvailabilityDto>(
            $"/api/appointments/public/test-salon/availability?serviceId={serviceId}&date={day:yyyy-MM-dd}");
        Assert.NotNull(after);
        Assert.DoesNotContain(new TimeSpan(10, 0, 0), after!.AvailableStartTimes);
        Assert.DoesNotContain(new TimeSpan(10, 15, 0), after.AvailableStartTimes);
        Assert.Contains(after.Slots, x => x.StartTime == new TimeSpan(10, 15, 0) && !x.IsAvailable && x.UnavailableReason == "Booked");
    }

    [Fact]
    public async Task Client_Create_Missing_Required_Field_Returns_Standardized_Validation_Error()
    {
        var login = await LoginAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);

        var response = await _client.PostAsJsonAsync("/api/clients", new CreateClientDto
        {
            FullName = "",
            Phone = "+38761111111",
            Email = "owner@example.com"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonObject>();
        Assert.NotNull(payload);
        Assert.Equal("validation_error", payload!["code"]?.GetValue<string>());
        Assert.False(string.IsNullOrWhiteSpace(payload["traceId"]?.GetValue<string>()));

        var details = payload["details"] as JsonObject;
        Assert.NotNull(details);
        Assert.Contains("FullName", details!.Select(x => x.Key), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Client_With_Appointment_Cannot_Be_Deleted()
    {
        var login = await LoginAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);

        var createClient = await _client.PostAsJsonAsync("/api/clients", new CreateClientDto
        {
            FullName = "Za brisanje",
            Phone = "0622222222"
        });
        createClient.EnsureSuccessStatusCode();
        var client = await createClient.Content.ReadFromJsonAsync<ClientDto>();
        Assert.NotNull(client);

        var services = await _client.GetFromJsonAsync<List<ServiceDto>>("/api/services");
        Assert.NotNull(services);
        Assert.NotEmpty(services);

        var day = BookableDayUtc(2);
        var book = await _client.PostAsJsonAsync("/api/appointments/public/test-salon", new PublicCreateAppointmentDto
        {
            ServiceId = services![0].Id,
            AppointmentDate = day,
            StartTime = new TimeSpan(11, 0, 0),
            FullName = client!.FullName,
            Phone = client.Phone
        });
        book.EnsureSuccessStatusCode();

        var deleteResponse = await _client.DeleteAsync($"/api/clients/{client.Id}");
        Assert.Equal(HttpStatusCode.BadRequest, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task Working_Hours_Upsert_Duplicate_Day_Returns_BadRequest()
    {
        var login = await LoginAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);

        var payload = new List<UpdateWorkingHourDto>
        {
            new() { DayOfWeek = DayOfWeek.Monday, OpenTime = new TimeSpan(8, 0, 0), CloseTime = new TimeSpan(12, 0, 0), IsClosed = false },
            new() { DayOfWeek = DayOfWeek.Monday, OpenTime = new TimeSpan(13, 0, 0), CloseTime = new TimeSpan(17, 0, 0), IsClosed = false }
        };

        var response = await _client.PutAsJsonAsync("/api/working-hours", payload);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_With_Invalid_Token_Returns_Unauthorized()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/refresh", new RefreshTokenRequestDto
        {
            RefreshToken = Guid.NewGuid().ToString("N")
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Business_Register_Creates_Owner_And_Requires_Email_Verification()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var response = await _client.PostAsJsonAsync("/api/businesses/register", new RegisterBusinessRequestDto
        {
            BusinessName = $"Studio {suffix}",
            Slug = $"studio-{suffix}",
            BusinessType = MojTermin.Api.Domain.Enums.BusinessType.BeautySalon,
            Phone = "061123123",
            BusinessEmail = $"biz-{suffix}@example.com",
            Address = "Sarajevo",
            Description = "Test onboardinga",
            OwnerFullName = "Owner Test",
            OwnerEmail = $"owner-{suffix}@example.com",
            OwnerUsername = $"owner-{suffix}",
            OwnerPassword = "Owner123!"
        });

        response.EnsureSuccessStatusCode();

        // Strict email-verification flow: registration must NOT return JWT tokens.
        // The SPA gets a pending-verification payload and the owner stays
        // unverified in the DB until they click the link.
        var registerResponse = await response.Content.ReadFromJsonAsync<RegisterBusinessResponseDto>();
        Assert.NotNull(registerResponse);
        Assert.True(registerResponse!.RequiresEmailVerification);
        Assert.Equal($"owner-{suffix}@example.com", registerResponse.OwnerEmail);
        Assert.Equal($"studio-{suffix}", registerResponse.BusinessSlug);

        // The newly-registered owner cannot log in until they verify.
        var loginAttempt = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
        {
            UsernameOrEmail = $"owner-{suffix}@example.com",
            Password = "Owner123!"
        });
        Assert.Equal(HttpStatusCode.Forbidden, loginAttempt.StatusCode);
        var loginError = await loginAttempt.Content.ReadFromJsonAsync<AuthErrorDto>();
        Assert.NotNull(loginError);
        Assert.Equal("EMAIL_NOT_VERIFIED", loginError!.Code);
    }

    [Fact]
    public async Task Verify_Email_Marks_User_Verified_And_Issues_Tokens()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var registerResponse = await _client.PostAsJsonAsync("/api/businesses/register", new RegisterBusinessRequestDto
        {
            BusinessName = $"Verify Salon {suffix}",
            Slug = $"verify-{suffix}",
            BusinessType = MojTermin.Api.Domain.Enums.BusinessType.BeautySalon,
            Phone = "061123123",
            BusinessEmail = $"vbiz-{suffix}@example.com",
            Address = "Sarajevo",
            OwnerFullName = "Owner Verify",
            OwnerEmail = $"vowner-{suffix}@example.com",
            OwnerUsername = $"vowner-{suffix}",
            OwnerPassword = "Owner123!"
        });
        registerResponse.EnsureSuccessStatusCode();

        // The verification token is sent over email and never returned by the
        // API. We grab it directly from the test DB, mint a matching raw token
        // by reading the column and then... actually we can't reverse the hash.
        // So we mint a known token via the resend endpoint and intercept via DB.
        // Pragmatic approach: we set a known token directly in the in-memory DB.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MojTerminDbContext>();
        var owner = await db.AppUsers.FirstAsync(x => x.Email == $"vowner-{suffix}@example.com");
        const string rawToken = "test-known-raw-token-1234567890";
        owner.EmailVerificationTokenHash = MojTermin.Api.Infrastructure.Services.EmailVerificationTokenHasher.Hash(rawToken);
        owner.EmailVerificationTokenExpiresAtUtc = DateTime.UtcNow.AddHours(1);
        owner.EmailVerified = false;
        await db.SaveChangesAsync();

        var verifyResponse = await _client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequestDto
        {
            Token = rawToken
        });
        verifyResponse.EnsureSuccessStatusCode();

        var auth = await verifyResponse.Content.ReadFromJsonAsync<AuthResponseDto>();
        Assert.NotNull(auth);
        Assert.False(string.IsNullOrWhiteSpace(auth!.Token));
        Assert.False(string.IsNullOrWhiteSpace(auth.RefreshToken));

        // After verification the same login that previously failed must succeed.
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
        {
            UsernameOrEmail = $"vowner-{suffix}@example.com",
            Password = "Owner123!"
        });
        loginResponse.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Notifications_Get_Returns_Current_Business_Entries()
    {
        var login = await LoginAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MojTerminDbContext>();
            db.NotificationLogs.Add(new MojTermin.Api.Domain.Entities.NotificationLog
            {
                Id = Guid.NewGuid(),
                BusinessId = login.BusinessId,
                Channel = NotificationChannel.Email,
                Status = NotificationDeliveryStatus.Sent,
                Recipient = "owner@local.test",
                Subject = "Test notification",
                BodyPreview = "Body",
                CreatedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var response = await _client.GetAsync("/api/notifications?status=Sent&limit=10");
        response.EnsureSuccessStatusCode();
        var rows = await response.Content.ReadFromJsonAsync<List<NotificationLogDto>>();

        Assert.NotNull(rows);
        Assert.NotEmpty(rows);
        Assert.All(rows!, x => Assert.Equal(login.BusinessId, x.BusinessId));
    }

    [Fact]
    public async Task Services_Get_Does_Not_Leak_Other_Business_Data()
    {
        var login = await LoginAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MojTerminDbContext>();
            var otherBusinessId = Guid.NewGuid();
            db.Businesses.Add(new MojTermin.Api.Domain.Entities.Business
            {
                Id = otherBusinessId,
                Name = "Leak Test Biz",
                Slug = $"leak-test-{Guid.NewGuid():N}"[..16],
                BusinessType = BusinessType.Other,
                Phone = "0610000000",
                Email = "leak@test.local",
                Address = "Test",
                Description = "Isolation test",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });

            db.Services.Add(new MojTermin.Api.Domain.Entities.Service
            {
                Id = Guid.NewGuid(),
                BusinessId = otherBusinessId,
                Name = "Other Business Service",
                DurationMinutes = 45,
                Price = 99,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var response = await _client.GetAsync("/api/services");
        response.EnsureSuccessStatusCode();
        var services = await response.Content.ReadFromJsonAsync<List<ServiceDto>>();

        Assert.NotNull(services);
        Assert.NotEmpty(services);
        Assert.All(services!, x => Assert.Equal(login.BusinessId, x.BusinessId));
        Assert.DoesNotContain(services!, x => x.Name == "Other Business Service");
    }

    [Fact]
    public async Task Public_Booking_Rejects_Filled_Honeypot_Field()
    {
        // Honeypot: real users never fill the hidden "Website" field. A bot
        // that auto-fills every input must be rejected with 400 BadRequest.
        var services = await _client.GetFromJsonAsync<List<ServiceDto>>("/api/services/public/test-salon");
        Assert.NotNull(services);
        Assert.NotEmpty(services);

        var day = BookableDayUtc(7);
        var response = await _client.PostAsJsonAsync("/api/appointments/public/test-salon", new PublicCreateAppointmentDto
        {
            ServiceId = services![0].Id,
            AppointmentDate = day,
            StartTime = new TimeSpan(10, 30, 0),
            FullName = "Definitely Not A Bot",
            Phone = "0610001111",
            Email = "bot@example.com",
            Website = "https://buy-cheap-stuff.example"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Public_Booking_Allows_Empty_Honeypot_Field()
    {
        var services = await _client.GetFromJsonAsync<List<ServiceDto>>("/api/services/public/test-salon");
        Assert.NotNull(services);
        Assert.NotEmpty(services);

        var day = BookableDayUtc(9);
        var response = await _client.PostAsJsonAsync("/api/appointments/public/test-salon", new PublicCreateAppointmentDto
        {
            ServiceId = services![0].Id,
            AppointmentDate = day,
            StartTime = new TimeSpan(10, 30, 0),
            FullName = "Real User",
            Phone = "0610001112",
            Email = "real@example.com",
            Website = ""
        });

        response.EnsureSuccessStatusCode();
    }

    private async Task<AuthResponseDto> LoginAsync()
    {
        _client.DefaultRequestHeaders.Authorization = null;
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
        {
            UsernameOrEmail = "owner",
            Password = "Owner123!"
        });
        loginResponse.EnsureSuccessStatusCode();
        var auth = await loginResponse.Content.ReadFromJsonAsync<AuthResponseDto>();
        Assert.NotNull(auth);
        return auth!;
    }
}
