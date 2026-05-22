using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MojTermin.Api.Application;
using MojTermin.Api.Application.DTOs;
using MojTermin.Api.Application.Interfaces;
using MojTermin.Api.Application.Validation;
using MojTermin.Api.Domain.Enums;
using MojTermin.Api.Infrastructure.Data;
using MojTermin.Api.Infrastructure.Services;
using System.Security.Cryptography;

namespace MojTermin.Api.API.Controllers;

[ApiController]
[Route("api/appointments")]
[Authorize(Policy = "OwnerOnly")]
public class AppointmentsController(
    MojTerminDbContext dbContext,
    ICurrentBusinessService currentBusinessService,
    IAdminAuditService adminAuditService,
    INotificationService notificationService,
    BusinessTimeProvider timeProvider,
    IOptions<ClientAppOptions> clientAppOptions) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<AppointmentDto>>> GetAll(
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null)
    {
        var businessId = currentBusinessService.GetRequiredBusinessId();
        var rows = await QueryByBusiness(businessId)
            .OrderByDescending(x => x.AppointmentDate)
            .ThenByDescending(x => x.StartTime)
            .ToPagedListAsync(page, pageSize);
        return Ok(rows);
    }

    [HttpGet("today")]
    public async Task<ActionResult<List<AppointmentDto>>> GetToday()
    {
        // "today" is a single day and naturally bounded; no pagination needed.
        // Use business-local "today" (not UTC) — otherwise around midnight we
        // would show yesterday's appointments to a tenant that is already in
        // the next calendar day.
        var businessId = currentBusinessService.GetRequiredBusinessId();
        var today = timeProvider.LocalNow.Date;
        var rows = await QueryByBusiness(businessId)
            .Where(x => x.AppointmentDate.Date == today)
            .ToListAsync();

        return Ok(rows);
    }

    [HttpGet("upcoming")]
    public async Task<ActionResult<List<AppointmentDto>>> GetUpcoming(
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null)
    {
        var businessId = currentBusinessService.GetRequiredBusinessId();
        var today = timeProvider.LocalNow.Date;
        var rows = await QueryByBusiness(businessId)
            .Where(x => x.AppointmentDate.Date >= today && x.Status != AppointmentStatus.Cancelled)
            .OrderBy(x => x.AppointmentDate)
            .ThenBy(x => x.StartTime)
            .ToPagedListAsync(page, pageSize);

        return Ok(rows);
    }

    [HttpGet("public/{businessSlug}/availability")]
    [AllowAnonymous]
    [EnableRateLimiting("api")]
    public async Task<ActionResult<PublicAppointmentAvailabilityDto>> GetPublicAvailability(
        string businessSlug,
        [FromQuery] Guid serviceId,
        [FromQuery] DateTime date,
        [FromQuery] Guid? staffMemberId = null,
        [FromQuery] int slotIntervalMinutes = 15)
    {
        var normalizedSlug = businessSlug.Trim().ToLowerInvariant();
        var business = await dbContext.Businesses
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Slug == normalizedSlug && x.IsActive);
        if (business is null)
        {
            return NotFound("Business nije pronađen.");
        }

        var service = await dbContext.Services
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == serviceId && x.BusinessId == business.Id && x.IsActive);
        if (service is null)
        {
            return BadRequest("Usluga nije dostupna.");
        }

        var appointmentDate = date.Date;
        if (appointmentDate < timeProvider.LocalNow.Date)
        {
            return BadRequest("Nije moguće prikazati termine u prošlosti.");
        }

        if (slotIntervalMinutes is < 5 or > 60 || slotIntervalMinutes % 5 != 0)
        {
            return BadRequest("Parametar slotIntervalMinutes mora biti između 5 i 60 i djeljiv sa 5.");
        }

        if (staffMemberId.HasValue)
        {
            var staffExists = await dbContext.StaffMembers.AnyAsync(x =>
                x.Id == staffMemberId.Value &&
                x.BusinessId == business.Id &&
                x.IsActive);
            if (!staffExists)
            {
                return BadRequest("Odabrani zaposlenik nije dostupan.");
            }
        }

        var slots = await GetSlotsAsync(business.Id, appointmentDate, service.DurationMinutes, slotIntervalMinutes, staffMemberId);

        return Ok(new PublicAppointmentAvailabilityDto
        {
            AppointmentDate = appointmentDate,
            ServiceId = service.Id,
            AvailableStartTimes = slots.Where(x => x.IsAvailable).Select(x => x.StartTime).ToList(),
            Slots = slots
        });
    }

    [HttpPost("public/{businessSlug}")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<AppointmentDto>> CreatePublic(string businessSlug, [FromBody] PublicCreateAppointmentDto dto)
    {
        // Honeypot: real users can't see `Website` (hidden via CSS in the form).
        // Any value here is almost certainly a naive bot scraping the page and
        // filling every input. We return the same success-looking 400 a normal
        // validation failure would, so bots can't distinguish "blocked" from
        // "your email is malformed" — discourages tuning around the trap.
        if (!string.IsNullOrWhiteSpace(dto.Website))
        {
            return BadRequest("Zahtjev nije ispravan.");
        }

        var normalizedSlug = businessSlug.Trim().ToLowerInvariant();
        var business = await dbContext.Businesses
            .FirstOrDefaultAsync(x => x.Slug == normalizedSlug && x.IsActive);
        if (business is null)
        {
            return NotFound("Business nije pronađen.");
        }

        if (!OptionalEmail.IsValid(dto.Email))
        {
            return BadRequest("Email nije ispravan.");
        }

        if (!RegionalPhone.IsValid(dto.Phone))
        {
            return BadRequest("Telefon mora biti u međunarodnom formatu sa pozivnim brojem (npr. +387...).");
        }

        var service = await dbContext.Services.FirstOrDefaultAsync(x =>
            x.Id == dto.ServiceId &&
            x.BusinessId == business.Id &&
            x.IsActive);

        if (service is null)
        {
            return BadRequest("Usluga nije dostupna.");
        }

        var startTime = dto.StartTime;
        var endTime = startTime.Add(TimeSpan.FromMinutes(service.DurationMinutes));
        var appointmentDate = dto.AppointmentDate.Date;
        var nowLocal = timeProvider.LocalNow;
        Guid? staffMemberId = null;
        string staffMemberName = string.Empty;
        if (dto.StaffMemberId.HasValue)
        {
            var staff = await dbContext.StaffMembers.FirstOrDefaultAsync(x =>
                x.Id == dto.StaffMemberId.Value &&
                x.BusinessId == business.Id &&
                x.IsActive);
            if (staff is null)
            {
                return BadRequest("Odabrani zaposlenik nije dostupan.");
            }
            staffMemberId = staff.Id;
            staffMemberName = staff.FullName;
        }

        if (appointmentDate < nowLocal.Date)
        {
            return BadRequest("Nije moguće rezervisati termin u prošlosti.");
        }

        if (appointmentDate == nowLocal.Date && startTime <= nowLocal.TimeOfDay)
        {
            return BadRequest("Vrijeme termina mora biti u budućnosti.");
        }

        var day = appointmentDate.DayOfWeek;
        var workingHour = await dbContext.WorkingHours.FirstOrDefaultAsync(x =>
            x.BusinessId == business.Id &&
            x.DayOfWeek == day);

        if (workingHour is null)
        {
            return BadRequest("Biznis nema podešeno radno vrijeme za odabrani dan.");
        }

        if (workingHour.IsClosed)
        {
            return BadRequest("Odabrani dan je neradni.");
        }

        if (startTime < workingHour.OpenTime || endTime > workingHour.CloseTime)
        {
            return BadRequest("Termin mora biti unutar radnog vremena.");
        }
        if (staffMemberId.HasValue)
        {
            var blockedByTimeOff = await IsBlockedByStaffTimeOffAsync(
                business.Id,
                staffMemberId.Value,
                appointmentDate,
                startTime,
                endTime);
            if (blockedByTimeOff)
            {
                return BadRequest("Odabrani zaposlenik nije dostupan u tom terminu.");
            }
        }

        var phone = RegionalPhone.Normalize(dto.Phone);

        // Application-level overlap check gives the common case a fast 409 without
        // touching the unique-index error path. The DB-level filtered unique index
        // UX_Appointments_Slot_Active is the actual race-safe authority (see the
        // catch block below). We deliberately do NOT wrap this in a user-initiated
        // transaction because EF's retrying execution strategy disallows them; the
        // index-driven 409 below is the real safety net.
        var overlapExists = await dbContext.Appointments.AnyAsync(x =>
            x.BusinessId == business.Id &&
            x.AppointmentDate.Date == appointmentDate &&
            x.Status != AppointmentStatus.Cancelled &&
            x.StaffMemberId == staffMemberId &&
            startTime < x.EndTime &&
            endTime > x.StartTime);

        if (overlapExists)
        {
            return Conflict("Odabrani termin je zauzet.");
        }

        var existingClient = await dbContext.Clients
            .FirstOrDefaultAsync(x => x.BusinessId == business.Id && x.Phone == phone);

        Domain.Entities.Client client;
        if (existingClient is null)
        {
            client = new Domain.Entities.Client
            {
                Id = Guid.NewGuid(),
                BusinessId = business.Id,
                FullName = dto.FullName.Trim(),
                Phone = phone,
                Email = OptionalEmail.Normalize(dto.Email),
                Note = dto.Note?.Trim(),
                CreatedAt = DateTime.UtcNow
            };
            dbContext.Clients.Add(client);
        }
        else
        {
            // SECURITY: NEVER overwrite FullName or Email of an existing client on a
            // public-anonymous booking just because the phone matches. Knowing a phone
            // number must not be enough to vandalize CRM contact data. Only fill in
            // Email when the existing record has none, which is a benign improvement.
            client = existingClient;
            if (string.IsNullOrWhiteSpace(client.Email))
            {
                client.Email = OptionalEmail.Normalize(dto.Email);
            }
        }

        // Client self-service cancellation: mint a one-time token whose hash is
        // stored on the appointment. The raw value goes in the confirmation email
        // link. Expiration matches the appointment start so the link stops working
        // the moment the slot begins.
        var rawCancelToken = GenerateCancellationTokenString();
        var appointmentStartUtc = appointmentDate.Add(startTime);
        var bookingContactEmail = OptionalEmail.Normalize(dto.Email);
        var bookingContactName = dto.FullName.Trim();

        var appointment = new Domain.Entities.Appointment
        {
            Id = Guid.NewGuid(),
            BusinessId = business.Id,
            ServiceId = service.Id,
            ClientId = client.Id,
            StaffMemberId = staffMemberId,
            AppointmentDate = appointmentDate,
            StartTime = startTime,
            EndTime = endTime,
            Status = AppointmentStatus.Confirmed,
            Note = dto.Note?.Trim(),
            ContactFullName = bookingContactName,
            ContactEmail = bookingContactEmail,
            CreatedAt = DateTime.UtcNow,
            CancellationTokenHash = AppointmentCancellationTokenHasher.Hash(rawCancelToken),
            CancellationTokenExpiresAtUtc = appointmentStartUtc
        };

        dbContext.Appointments.Add(appointment);

        try
        {
            await dbContext.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            // Another concurrent booking landed first and the filtered unique index rejected ours.
            return Conflict("Odabrani termin je zauzet.");
        }

        var cancelUrl = BuildCancelAppointmentUrl(rawCancelToken);

        await notificationService.NotifyNewAppointmentRequestAsync(
            business.Id,
            appointment.Id,
            business.Name,
            business.Email,
            bookingContactName,
            client.Phone,
            bookingContactEmail,
            service.Name,
            appointment.AppointmentDate,
            appointment.StartTime,
            appointment.EndTime,
            appointment.Note);

        await notificationService.NotifyAppointmentConfirmedToClientAsync(
            business.Id,
            appointment.Id,
            business.Name,
            business.Email,
            business.Phone,
            business.Address,
            bookingContactName,
            bookingContactEmail,
            service.Name,
            appointment.AppointmentDate,
            appointment.StartTime,
            appointment.EndTime,
            cancelUrl);

        return Ok(new AppointmentDto
        {
            Id = appointment.Id,
            BusinessId = appointment.BusinessId,
            ServiceId = appointment.ServiceId,
            ClientId = appointment.ClientId,
            StaffMemberId = appointment.StaffMemberId,
            AppointmentDate = appointment.AppointmentDate,
            StartTime = appointment.StartTime,
            EndTime = appointment.EndTime,
            Status = appointment.Status,
            Note = appointment.Note,
            CreatedAt = appointment.CreatedAt,
            ServiceName = service.Name,
            ClientName = client.FullName,
            StaffMemberName = staffMemberName
        });
    }

    [HttpPut("{id:guid}/confirm")]
    public async Task<IActionResult> Confirm(Guid id) => await UpdateStatus(id, AppointmentStatus.Confirmed);

    [HttpPut("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id) => await UpdateStatus(id, AppointmentStatus.Cancelled);

    [HttpPut("{id:guid}/complete")]
    public async Task<IActionResult> Complete(Guid id) => await UpdateStatus(id, AppointmentStatus.Completed);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var businessId = currentBusinessService.GetRequiredBusinessId();
        var appointment = await dbContext.Appointments
            .FirstOrDefaultAsync(x => x.Id == id && x.BusinessId == businessId);
        if (appointment is null)
        {
            return NotFound("Termin nije pronađen.");
        }

        dbContext.Appointments.Remove(appointment);
        try
        {
            await dbContext.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict("Termin je u međuvremenu izmijenjen ili obrisan. Osvježite listu i pokušajte ponovo.");
        }
        await adminAuditService.LogAsync(
            businessId,
            action: "delete",
            resourceType: "appointment",
            resourceId: appointment.Id,
            summary: $"Obrisan termin {appointment.AppointmentDate:yyyy-MM-dd} {appointment.StartTime:hh\\:mm}");
        return NoContent();
    }

    // ----------------------------------------------------------------------
    // Public client-side cancellation. Token comes from the confirmation email
    // link. Two endpoints intentionally:
    //   - GET /public/cancel/lookup?token=... — preview (no state change), so
    //     the SPA can render details before asking for confirm.
    //   - POST /public/cancel — actually flips the row to Cancelled.
    // Token is single-use: a successful cancel clears the hash so a replayed
    // link no longer matches anything.
    // ----------------------------------------------------------------------

    [HttpGet("public/cancel/lookup")]
    [AllowAnonymous]
    [EnableRateLimiting("api")]
    public async Task<ActionResult<PublicAppointmentSummaryDto>> LookupPublicCancel([FromQuery] string token)
    {
        var raw = (token ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(raw))
        {
            return BadRequest("Token nije prosljeđen.");
        }

        var hash = AppointmentCancellationTokenHasher.Hash(raw);
        var appointment = await dbContext.Appointments
            .AsNoTracking()
            .Include(x => x.Business)
            .Include(x => x.Service)
            .Include(x => x.Client)
            .FirstOrDefaultAsync(x => x.CancellationTokenHash == hash);

        if (appointment is null || appointment.Service is null || appointment.Business is null || appointment.Client is null)
        {
            return NotFound("Link nije validan ili je već iskorišten.");
        }

        var startUtc = appointment.AppointmentDate.Date.Add(appointment.StartTime);
        var alreadyCancelled = appointment.Status == AppointmentStatus.Cancelled;
        var tooLate = appointment.CancellationTokenExpiresAtUtc is not null
            && DateTime.UtcNow >= appointment.CancellationTokenExpiresAtUtc.Value;

        // Take only the first name for the greeting — avoids overflowing the
        // UI when a client typed both first+last name.
        var firstName = (appointment.Client.FullName ?? string.Empty).Trim().Split(' ').FirstOrDefault() ?? string.Empty;

        return Ok(new PublicAppointmentSummaryDto
        {
            BusinessName = appointment.Business.Name,
            ServiceName = appointment.Service.Name,
            ClientFirstName = firstName,
            AppointmentDate = appointment.AppointmentDate,
            StartTime = appointment.StartTime,
            EndTime = appointment.EndTime,
            AlreadyCancelled = alreadyCancelled,
            TooLateToCancel = tooLate && !alreadyCancelled
        });
    }

    [HttpPost("public/cancel")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<PublicAppointmentSummaryDto>> PublicCancel([FromBody] PublicCancelAppointmentRequestDto dto)
    {
        var raw = (dto.Token ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(raw))
        {
            return BadRequest("Token nije prosljeđen.");
        }

        var hash = AppointmentCancellationTokenHasher.Hash(raw);
        var appointment = await dbContext.Appointments
            .Include(x => x.Business)
            .Include(x => x.Service)
            .Include(x => x.Client)
            .FirstOrDefaultAsync(x => x.CancellationTokenHash == hash);

        if (appointment is null || appointment.Service is null || appointment.Business is null || appointment.Client is null)
        {
            return NotFound("Link nije validan ili je već iskorišten.");
        }

        if (appointment.Status == AppointmentStatus.Cancelled)
        {
            // Idempotent: re-clicking the link on an already-cancelled appointment
            // should not look like an error to the user. We still clear the hash
            // because the token has now been seen "in use" by the client.
            appointment.CancellationTokenHash = null;
            appointment.CancellationTokenExpiresAtUtc = null;
            await dbContext.SaveChangesAsync();
            return BuildCancelSummary(appointment, alreadyCancelled: true, tooLate: false);
        }

        if (appointment.CancellationTokenExpiresAtUtc is not null &&
            DateTime.UtcNow >= appointment.CancellationTokenExpiresAtUtc.Value)
        {
            return BuildCancelSummary(appointment, alreadyCancelled: false, tooLate: true);
        }

        if (appointment.Status != AppointmentStatus.Pending &&
            appointment.Status != AppointmentStatus.Confirmed)
        {
            // Completed / Rejected appointments cannot transition to Cancelled.
            // Mirrors IsTransitionAllowed used by the admin Cancel endpoint.
            return BadRequest("Ovaj termin više ne može biti otkazan.");
        }

        appointment.Status = AppointmentStatus.Cancelled;
        appointment.CancellationTokenHash = null;
        appointment.CancellationTokenExpiresAtUtc = null;

        try
        {
            await dbContext.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict("Termin je u međuvremenu izmijenjen. Osvježi stranicu i pokušaj ponovo.");
        }

        // Notify the salon (best-effort) so they know the slot is freed.
        try
        {
            await notificationService.NotifyAppointmentStatusChangedAsync(
                appointment.BusinessId,
                appointment.Id,
                AppointmentStatus.Cancelled,
                appointment.Business.Name,
                appointment.Business.Email,
                appointment.Business.Phone,
                appointment.Business.Address,
                ResolveAppointmentContactName(appointment),
                ResolveAppointmentContactEmail(appointment),
                appointment.Service.Name,
                appointment.AppointmentDate,
                appointment.StartTime,
                appointment.EndTime);
        }
        catch
        {
            // Email failure must not break the user-visible cancel result.
        }

        return BuildCancelSummary(appointment, alreadyCancelled: true, tooLate: false);
    }

    private static ActionResult<PublicAppointmentSummaryDto> BuildCancelSummary(
        Domain.Entities.Appointment appointment,
        bool alreadyCancelled,
        bool tooLate)
    {
        var displayName = ResolveAppointmentContactName(appointment) ?? string.Empty;
        var firstName = displayName.Trim().Split(' ').FirstOrDefault() ?? string.Empty;
        return new OkObjectResult(new PublicAppointmentSummaryDto
        {
            BusinessName = appointment.Business?.Name ?? string.Empty,
            ServiceName = appointment.Service?.Name ?? string.Empty,
            ClientFirstName = firstName,
            AppointmentDate = appointment.AppointmentDate,
            StartTime = appointment.StartTime,
            EndTime = appointment.EndTime,
            AlreadyCancelled = alreadyCancelled,
            TooLateToCancel = tooLate
        });
    }

    private string BuildCancelAppointmentUrl(string rawToken)
    {
        var baseUrl = (clientAppOptions.Value.BaseUrl ?? string.Empty).TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            baseUrl = "http://localhost:4200";
        }
        return $"{baseUrl}/cancel-appointment?token={Uri.EscapeDataString(rawToken)}";
    }

    private static string GenerateCancellationTokenString()
    {
        Span<byte> bytes = stackalloc byte[48];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    private async Task<IActionResult> UpdateStatus(Guid id, AppointmentStatus status)
    {
        var businessId = currentBusinessService.GetRequiredBusinessId();
        var appointment = await dbContext.Appointments
            .Include(x => x.Service)
            .Include(x => x.Client)
            .Include(x => x.Business)
            .FirstOrDefaultAsync(x => x.Id == id && x.BusinessId == businessId);
        if (appointment is null)
        {
            return NotFound("Termin nije pronađen.");
        }

        if (!IsTransitionAllowed(appointment.Status, status))
        {
            return BadRequest($"Promjena statusa sa '{appointment.Status}' na '{status}' nije dozvoljena.");
        }

        appointment.Status = status;
        try
        {
            await dbContext.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another admin (or the auto-complete background job) changed this
            // row first. Force a fresh fetch on the client so they don't
            // overwrite a "Cancelled" with "Completed" or similar.
            return Conflict("Termin je u međuvremenu izmijenjen. Osvježite listu i pokušajte ponovo.");
        }
        await adminAuditService.LogAsync(
            businessId,
            action: "status-change",
            resourceType: "appointment",
            resourceId: appointment.Id,
            summary: $"Promjena statusa: {status}",
            metadata: new
            {
                appointment.AppointmentDate,
                appointment.StartTime,
                appointment.EndTime
            });

        if (appointment.Service is not null && appointment.Client is not null && appointment.Business is not null)
        {
            await notificationService.NotifyAppointmentStatusChangedAsync(
                businessId,
                appointment.Id,
                status,
                appointment.Business.Name,
                appointment.Business.Email,
                appointment.Business.Phone,
                appointment.Business.Address,
                ResolveAppointmentContactName(appointment),
                ResolveAppointmentContactEmail(appointment),
                appointment.Service.Name,
                appointment.AppointmentDate,
                appointment.StartTime,
                appointment.EndTime);
        }

        return NoContent();
    }

    private static string ResolveAppointmentContactName(Domain.Entities.Appointment appointment)
    {
        if (!string.IsNullOrWhiteSpace(appointment.ContactFullName))
        {
            return appointment.ContactFullName.Trim();
        }

        return (appointment.Client?.FullName ?? string.Empty).Trim();
    }

    private static string? ResolveAppointmentContactEmail(Domain.Entities.Appointment appointment)
    {
        if (!string.IsNullOrWhiteSpace(appointment.ContactEmail))
        {
            return appointment.ContactEmail.Trim();
        }

        return appointment.Client?.Email;
    }

    private static bool IsTransitionAllowed(AppointmentStatus current, AppointmentStatus target)
    {
        if (current == target)
        {
            return false;
        }

        return current switch
        {
            AppointmentStatus.Pending => target is AppointmentStatus.Confirmed or AppointmentStatus.Cancelled,
            AppointmentStatus.Confirmed => target is AppointmentStatus.Completed or AppointmentStatus.Cancelled,
            AppointmentStatus.Rejected => false,
            AppointmentStatus.Cancelled => false,
            AppointmentStatus.Completed => false,
            _ => false
        };
    }

    private IQueryable<AppointmentDto> QueryByBusiness(Guid businessId)
    {
        return dbContext.Appointments
            .AsNoTracking()
            .Where(x => x.BusinessId == businessId)
            .Include(x => x.Service)
            .Include(x => x.Client)
            .Include(x => x.StaffMember)
            .OrderBy(x => x.AppointmentDate)
            .ThenBy(x => x.StartTime)
            .Select(x => new AppointmentDto
            {
                Id = x.Id,
                BusinessId = x.BusinessId,
                ServiceId = x.ServiceId,
                ClientId = x.ClientId,
                StaffMemberId = x.StaffMemberId,
                AppointmentDate = x.AppointmentDate,
                StartTime = x.StartTime,
                EndTime = x.EndTime,
                Status = x.Status,
                Note = x.Note,
                CreatedAt = x.CreatedAt,
                ServiceName = x.Service != null ? x.Service.Name : string.Empty,
                ClientName = x.Client != null ? x.Client.FullName : string.Empty,
                StaffMemberName = x.StaffMember != null ? x.StaffMember.FullName : string.Empty
            });
    }

    private async Task<List<PublicAppointmentSlotDto>> GetSlotsAsync(
        Guid businessId,
        DateTime appointmentDate,
        int durationMinutes,
        int slotIntervalMinutes,
        Guid? staffMemberId)
    {
        var workingHour = await dbContext.WorkingHours
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.BusinessId == businessId && x.DayOfWeek == appointmentDate.DayOfWeek);
        if (workingHour is null || workingHour.IsClosed)
        {
            return new List<PublicAppointmentSlotDto>();
        }

        var existingAppointments = await dbContext.Appointments
            .AsNoTracking()
            .Where(x =>
                x.BusinessId == businessId &&
                x.AppointmentDate.Date == appointmentDate.Date &&
                x.StaffMemberId == staffMemberId &&
                x.Status != AppointmentStatus.Cancelled)
            .Select(x => new { x.StartTime, x.EndTime })
            .ToListAsync();

        List<(DateTime DateFrom, DateTime DateTo, TimeSpan? TimeFrom, TimeSpan? TimeTo)> timeOffs = [];
        if (staffMemberId.HasValue)
        {
            timeOffs = await dbContext.StaffTimeOffs
                .AsNoTracking()
                .Where(x =>
                    x.BusinessId == businessId &&
                    x.StaffMemberId == staffMemberId.Value &&
                    x.DateFrom.Date <= appointmentDate.Date &&
                    x.DateTo.Date >= appointmentDate.Date)
                .Select(x => new ValueTuple<DateTime, DateTime, TimeSpan?, TimeSpan?>(
                    x.DateFrom,
                    x.DateTo,
                    x.TimeFrom,
                    x.TimeTo))
                .ToListAsync();
        }

        var slots = new List<PublicAppointmentSlotDto>();
        var nowLocal = timeProvider.LocalNow;
        var current = workingHour.OpenTime;
        while (current.Add(TimeSpan.FromMinutes(durationMinutes)) <= workingHour.CloseTime)
        {
            var candidateEnd = current.Add(TimeSpan.FromMinutes(durationMinutes));
            // Wall-clock slots (working hours) vs business-local "now" — not UTC.
            var isPastToday = appointmentDate.Date == nowLocal.Date && current <= nowLocal.TimeOfDay;
            var overlaps = existingAppointments.Any(x => current < x.EndTime && candidateEnd > x.StartTime);
            string? unavailableReason = null;
            if (isPastToday)
            {
                unavailableReason = "Past";
            }
            else if (overlaps)
            {
                unavailableReason = "Booked";
            }
            else if (staffMemberId.HasValue && IsBlockedByAnyTimeOff(timeOffs, appointmentDate, current, candidateEnd))
            {
                unavailableReason = "Booked";
            }

            slots.Add(new PublicAppointmentSlotDto
            {
                StartTime = current,
                IsAvailable = !isPastToday && !overlaps,
                UnavailableReason = unavailableReason
            });

            current = current.Add(TimeSpan.FromMinutes(slotIntervalMinutes));
        }

        return slots;
    }

    private async Task<bool> IsBlockedByStaffTimeOffAsync(
        Guid businessId,
        Guid staffMemberId,
        DateTime appointmentDate,
        TimeSpan startTime,
        TimeSpan endTime)
    {
        var rows = await dbContext.StaffTimeOffs
            .AsNoTracking()
            .Where(x =>
                x.BusinessId == businessId &&
                x.StaffMemberId == staffMemberId &&
                x.DateFrom.Date <= appointmentDate.Date &&
                x.DateTo.Date >= appointmentDate.Date)
            .ToListAsync();

        return IsBlockedByAnyTimeOff(
            rows.Select(x => (x.DateFrom, x.DateTo, x.TimeFrom, x.TimeTo)).ToList(),
            appointmentDate,
            startTime,
            endTime);
    }

    private static bool IsBlockedByAnyTimeOff(
        List<(DateTime DateFrom, DateTime DateTo, TimeSpan? TimeFrom, TimeSpan? TimeTo)> rows,
        DateTime appointmentDate,
        TimeSpan startTime,
        TimeSpan endTime)
    {
        foreach (var row in rows)
        {
            if (appointmentDate.Date < row.DateFrom.Date || appointmentDate.Date > row.DateTo.Date)
            {
                continue;
            }

            // Full-day block when no times are specified.
            if (!row.TimeFrom.HasValue || !row.TimeTo.HasValue)
            {
                return true;
            }

            if (startTime < row.TimeTo.Value && endTime > row.TimeFrom.Value)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        // SQL Server raises error 2601 (Cannot insert duplicate key row in object 'X' with unique index)
        // or 2627 (Violation of PRIMARY KEY/UNIQUE constraint) when a uniqueness rule is hit.
        if (ex.InnerException is SqlException sqlEx)
        {
            return sqlEx.Number is 2601 or 2627;
        }

        return false;
    }
}
