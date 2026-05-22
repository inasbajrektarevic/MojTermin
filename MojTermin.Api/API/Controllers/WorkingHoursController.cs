using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MojTermin.Api.Application.DTOs;
using MojTermin.Api.Application.Interfaces;
using MojTermin.Api.Infrastructure.Data;

namespace MojTermin.Api.API.Controllers;

[ApiController]
[Route("api/working-hours")]
[Authorize(Policy = "OwnerOnly")]
public class WorkingHoursController(
    MojTerminDbContext dbContext,
    ICurrentBusinessService currentBusinessService,
    IAdminAuditService adminAuditService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<WorkingHourDto>>> GetAll()
    {
        var businessId = currentBusinessService.GetRequiredBusinessId();
        var rows = await dbContext.WorkingHours
            .Where(x => x.BusinessId == businessId)
            .OrderBy(x => x.DayOfWeek)
            .Select(x => new WorkingHourDto
            {
                Id = x.Id,
                BusinessId = x.BusinessId,
                DayOfWeek = x.DayOfWeek,
                OpenTime = x.OpenTime,
                CloseTime = x.CloseTime,
                IsClosed = x.IsClosed
            })
            .ToListAsync();

        return Ok(rows);
    }

    [HttpGet("public/{businessSlug}")]
    [AllowAnonymous]
    public async Task<ActionResult<List<WorkingHourDto>>> GetPublicBySlug(string businessSlug)
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

        var rows = await dbContext.WorkingHours
            .Where(x => x.BusinessId == businessId.Value)
            .OrderBy(x => x.DayOfWeek)
            .Select(x => new WorkingHourDto
            {
                Id = x.Id,
                BusinessId = x.BusinessId,
                DayOfWeek = x.DayOfWeek,
                OpenTime = x.OpenTime,
                CloseTime = x.CloseTime,
                IsClosed = x.IsClosed
            })
            .ToListAsync();

        return Ok(rows);
    }

    [HttpPut]
    public async Task<ActionResult<List<WorkingHourDto>>> Upsert([FromBody] List<UpdateWorkingHourDto> items)
    {
        if (items.Count == 0)
        {
            return BadRequest("Lista radnih sati ne može biti prazna.");
        }

        if (items.Select(x => x.DayOfWeek).Distinct().Count() != items.Count)
        {
            return BadRequest("Lista ne smije sadržati duplikate dana u sedmici.");
        }

        var businessId = currentBusinessService.GetRequiredBusinessId();
        var existing = await dbContext.WorkingHours
            .Where(x => x.BusinessId == businessId)
            .ToListAsync();

        foreach (var item in items)
        {
            if (!item.IsClosed && item.OpenTime >= item.CloseTime)
            {
                return BadRequest($"Neispravno vrijeme za dan: {item.DayOfWeek}.");
            }

            var row = existing.FirstOrDefault(x => x.DayOfWeek == item.DayOfWeek);
            if (row is null)
            {
                dbContext.WorkingHours.Add(new Domain.Entities.WorkingHour
                {
                    Id = Guid.NewGuid(),
                    BusinessId = businessId,
                    DayOfWeek = item.DayOfWeek,
                    OpenTime = item.OpenTime,
                    CloseTime = item.CloseTime,
                    IsClosed = item.IsClosed
                });
            }
            else
            {
                row.OpenTime = item.OpenTime;
                row.CloseTime = item.CloseTime;
                row.IsClosed = item.IsClosed;
            }
        }

        await dbContext.SaveChangesAsync();
        await adminAuditService.LogAsync(
            businessId,
            action: "upsert",
            resourceType: "working-hours",
            summary: "Ažurirano radno vrijeme",
            metadata: new { Days = items.Count });

        var updated = await dbContext.WorkingHours
            .Where(x => x.BusinessId == businessId)
            .OrderBy(x => x.DayOfWeek)
            .Select(x => new WorkingHourDto
            {
                Id = x.Id,
                BusinessId = x.BusinessId,
                DayOfWeek = x.DayOfWeek,
                OpenTime = x.OpenTime,
                CloseTime = x.CloseTime,
                IsClosed = x.IsClosed
            })
            .ToListAsync();

        return Ok(updated);
    }

}
