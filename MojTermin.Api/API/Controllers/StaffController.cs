using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MojTermin.Api.Application.DTOs;
using MojTermin.Api.Application.Interfaces;
using MojTermin.Api.Infrastructure.Data;

namespace MojTermin.Api.API.Controllers;

[ApiController]
[Route("api/staff")]
[Authorize(Policy = "OwnerOnly")]
public class StaffController(
    MojTerminDbContext dbContext,
    ICurrentBusinessService currentBusinessService,
    IAdminAuditService adminAuditService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<StaffMemberDto>>> GetAll()
    {
        var businessId = currentBusinessService.GetRequiredBusinessId();
        var rows = await dbContext.StaffMembers
            .Where(x => x.BusinessId == businessId)
            .OrderBy(x => x.FullName)
            .Select(x => new StaffMemberDto
            {
                Id = x.Id,
                BusinessId = x.BusinessId,
                FullName = x.FullName,
                Title = x.Title,
                Phone = x.Phone,
                Email = x.Email,
                IsActive = x.IsActive,
                CreatedAt = x.CreatedAt
            })
            .ToListAsync();

        return Ok(rows);
    }

    [HttpGet("public/{businessSlug}")]
    [AllowAnonymous]
    public async Task<ActionResult<List<StaffMemberDto>>> GetPublicBySlug(string businessSlug)
    {
        var normalizedSlug = businessSlug.Trim().ToLowerInvariant();
        var businessId = await dbContext.Businesses
            .Where(x => x.Slug == normalizedSlug && x.IsActive)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync();

        if (businessId is null)
        {
            return NotFound("Business nije pronađen.");
        }

        var rows = await dbContext.StaffMembers
            .Where(x => x.BusinessId == businessId.Value && x.IsActive)
            .OrderBy(x => x.FullName)
            .Select(x => new StaffMemberDto
            {
                Id = x.Id,
                BusinessId = x.BusinessId,
                FullName = x.FullName,
                Title = x.Title,
                Phone = x.Phone,
                Email = x.Email,
                IsActive = x.IsActive,
                CreatedAt = x.CreatedAt
            })
            .ToListAsync();

        return Ok(rows);
    }

    [HttpPost]
    public async Task<ActionResult<StaffMemberDto>> Create([FromBody] CreateStaffMemberDto dto)
    {
        var businessId = currentBusinessService.GetRequiredBusinessId();
        var row = new Domain.Entities.StaffMember
        {
            Id = Guid.NewGuid(),
            BusinessId = businessId,
            FullName = dto.FullName.Trim(),
            Title = dto.Title?.Trim(),
            Phone = dto.Phone?.Trim(),
            Email = dto.Email?.Trim().ToLowerInvariant(),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        dbContext.StaffMembers.Add(row);
        await dbContext.SaveChangesAsync();
        await adminAuditService.LogAsync(
            businessId,
            action: "create",
            resourceType: "staff",
            resourceId: row.Id,
            summary: $"Dodan zaposlenik: {row.FullName}");

        return CreatedAtAction(nameof(GetAll), new { id = row.Id }, MapToDto(row));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<StaffMemberDto>> Update(Guid id, [FromBody] UpdateStaffMemberDto dto)
    {
        var businessId = currentBusinessService.GetRequiredBusinessId();
        var row = await dbContext.StaffMembers.FirstOrDefaultAsync(x => x.Id == id && x.BusinessId == businessId);
        if (row is null)
        {
            return NotFound("Zaposlenik nije pronađen.");
        }

        row.FullName = dto.FullName.Trim();
        row.Title = dto.Title?.Trim();
        row.Phone = dto.Phone?.Trim();
        row.Email = dto.Email?.Trim().ToLowerInvariant();
        row.IsActive = dto.IsActive;

        await dbContext.SaveChangesAsync();
        await adminAuditService.LogAsync(
            businessId,
            action: "update",
            resourceType: "staff",
            resourceId: row.Id,
            summary: $"Ažuriran zaposlenik: {row.FullName}",
            metadata: new { row.IsActive });
        return Ok(MapToDto(row));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var businessId = currentBusinessService.GetRequiredBusinessId();
        var row = await dbContext.StaffMembers.FirstOrDefaultAsync(x => x.Id == id && x.BusinessId == businessId);
        if (row is null)
        {
            return NotFound("Zaposlenik nije pronađen.");
        }

        var hasFutureAppointments = await dbContext.Appointments.AnyAsync(x =>
            x.BusinessId == businessId &&
            x.StaffMemberId == row.Id &&
            x.Status != Domain.Enums.AppointmentStatus.Cancelled &&
            x.AppointmentDate.Date >= DateTime.UtcNow.Date);
        if (hasFutureAppointments)
        {
            return BadRequest("Zaposlenik ima buduće termine. Prvo ih preraspodijelite.");
        }

        dbContext.StaffMembers.Remove(row);
        await dbContext.SaveChangesAsync();
        await adminAuditService.LogAsync(
            businessId,
            action: "delete",
            resourceType: "staff",
            resourceId: row.Id,
            summary: $"Obrisan zaposlenik: {row.FullName}");
        return NoContent();
    }

    [HttpGet("{id:guid}/time-offs")]
    public async Task<ActionResult<List<StaffTimeOffDto>>> GetTimeOffs(Guid id)
    {
        var businessId = currentBusinessService.GetRequiredBusinessId();
        var staffExists = await dbContext.StaffMembers.AnyAsync(x => x.Id == id && x.BusinessId == businessId);
        if (!staffExists)
        {
            return NotFound("Zaposlenik nije pronađen.");
        }

        var rows = await dbContext.StaffTimeOffs
            .Where(x => x.BusinessId == businessId && x.StaffMemberId == id)
            .OrderByDescending(x => x.DateFrom)
            .ThenByDescending(x => x.TimeFrom)
            .Select(x => new StaffTimeOffDto
            {
                Id = x.Id,
                BusinessId = x.BusinessId,
                StaffMemberId = x.StaffMemberId,
                DateFrom = x.DateFrom,
                DateTo = x.DateTo,
                TimeFrom = x.TimeFrom,
                TimeTo = x.TimeTo,
                Reason = x.Reason,
                CreatedAtUtc = x.CreatedAtUtc
            })
            .ToListAsync();

        return Ok(rows);
    }

    [HttpPost("{id:guid}/time-offs")]
    public async Task<ActionResult<StaffTimeOffDto>> CreateTimeOff(Guid id, [FromBody] CreateStaffTimeOffDto dto)
    {
        var businessId = currentBusinessService.GetRequiredBusinessId();
        var staff = await dbContext.StaffMembers.FirstOrDefaultAsync(x => x.Id == id && x.BusinessId == businessId);
        if (staff is null)
        {
            return NotFound("Zaposlenik nije pronađen.");
        }

        var dateFrom = dto.DateFrom.Date;
        var dateTo = dto.DateTo.Date;
        if (dateTo < dateFrom)
        {
            return BadRequest("DateTo mora biti isti ili nakon DateFrom.");
        }
        if (dto.TimeFrom.HasValue != dto.TimeTo.HasValue)
        {
            return BadRequest("Vremena moraju biti oba navedena ili oba prazna (cjelodnevno odsustvo).");
        }
        if (dto.TimeFrom.HasValue && dto.TimeTo.HasValue && dto.TimeTo <= dto.TimeFrom)
        {
            return BadRequest("Vrijeme završetka mora biti nakon vremena početka.");
        }

        var row = new Domain.Entities.StaffTimeOff
        {
            Id = Guid.NewGuid(),
            BusinessId = businessId,
            StaffMemberId = id,
            DateFrom = dateFrom,
            DateTo = dateTo,
            TimeFrom = dto.TimeFrom,
            TimeTo = dto.TimeTo,
            Reason = dto.Reason?.Trim(),
            CreatedAtUtc = DateTime.UtcNow
        };

        dbContext.StaffTimeOffs.Add(row);
        await dbContext.SaveChangesAsync();
        await adminAuditService.LogAsync(
            businessId,
            action: "create",
            resourceType: "staff-timeoff",
            resourceId: row.Id,
            summary: $"Dodan time-off za {staff.FullName}",
            metadata: new { row.DateFrom, row.DateTo, row.TimeFrom, row.TimeTo });

        return Ok(new StaffTimeOffDto
        {
            Id = row.Id,
            BusinessId = row.BusinessId,
            StaffMemberId = row.StaffMemberId,
            DateFrom = row.DateFrom,
            DateTo = row.DateTo,
            TimeFrom = row.TimeFrom,
            TimeTo = row.TimeTo,
            Reason = row.Reason,
            CreatedAtUtc = row.CreatedAtUtc
        });
    }

    [HttpDelete("{id:guid}/time-offs/{timeOffId:guid}")]
    public async Task<IActionResult> DeleteTimeOff(Guid id, Guid timeOffId)
    {
        var businessId = currentBusinessService.GetRequiredBusinessId();
        var row = await dbContext.StaffTimeOffs.FirstOrDefaultAsync(x =>
            x.Id == timeOffId &&
            x.StaffMemberId == id &&
            x.BusinessId == businessId);
        if (row is null)
        {
            return NotFound("Time-off nije pronađen.");
        }

        dbContext.StaffTimeOffs.Remove(row);
        await dbContext.SaveChangesAsync();
        await adminAuditService.LogAsync(
            businessId,
            action: "delete",
            resourceType: "staff-timeoff",
            resourceId: timeOffId,
            summary: "Obrisan staff time-off");
        return NoContent();
    }

    private static StaffMemberDto MapToDto(Domain.Entities.StaffMember x) => new()
    {
        Id = x.Id,
        BusinessId = x.BusinessId,
        FullName = x.FullName,
        Title = x.Title,
        Phone = x.Phone,
        Email = x.Email,
        IsActive = x.IsActive,
        CreatedAt = x.CreatedAt
    };
}
