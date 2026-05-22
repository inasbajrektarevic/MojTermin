using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MojTermin.Api.Application.DTOs;
using MojTermin.Api.Application.Interfaces;
using MojTermin.Api.Infrastructure.Data;

namespace MojTermin.Api.API.Controllers;

[ApiController]
[Route("api/services")]
[Authorize(Policy = "OwnerOnly")]
public class ServicesController(
    MojTerminDbContext dbContext,
    ICurrentBusinessService currentBusinessService,
    IAdminAuditService adminAuditService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<ServiceDto>>> GetAll()
    {
        var businessId = currentBusinessService.GetRequiredBusinessId();
        var services = await dbContext.Services
            .Where(x => x.BusinessId == businessId)
            .OrderBy(x => x.Name)
            .Select(x => new ServiceDto
            {
                Id = x.Id,
                BusinessId = x.BusinessId,
                Name = x.Name,
                Description = x.Description,
                ImageUrl = x.ImageUrl,
                DurationMinutes = x.DurationMinutes,
                Price = x.Price,
                IsActive = x.IsActive,
                CreatedAt = x.CreatedAt
            })
            .ToListAsync();

        return Ok(services);
    }

    [HttpGet("public/{businessSlug}")]
    [AllowAnonymous]
    public async Task<ActionResult<List<ServiceDto>>> GetPublicBySlug(string businessSlug)
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

        var services = await dbContext.Services
            .Where(x => x.BusinessId == businessId && x.IsActive)
            .OrderBy(x => x.Name)
            .Select(x => new ServiceDto
            {
                Id = x.Id,
                BusinessId = x.BusinessId,
                Name = x.Name,
                Description = x.Description,
                ImageUrl = x.ImageUrl,
                DurationMinutes = x.DurationMinutes,
                Price = x.Price,
                IsActive = x.IsActive,
                CreatedAt = x.CreatedAt
            })
            .ToListAsync();

        return Ok(services);
    }

    [HttpPost]
    public async Task<ActionResult<ServiceDto>> Create([FromBody] CreateServiceDto dto)
    {
        var businessId = currentBusinessService.GetRequiredBusinessId();
        var service = new Domain.Entities.Service
        {
            Id = Guid.NewGuid(),
            BusinessId = businessId,
            Name = dto.Name.Trim(),
            Description = dto.Description?.Trim(),
            ImageUrl = dto.ImageUrl?.Trim(),
            DurationMinutes = dto.DurationMinutes,
            Price = dto.Price,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        dbContext.Services.Add(service);
        await dbContext.SaveChangesAsync();
        await adminAuditService.LogAsync(
            businessId,
            action: "create",
            resourceType: "service",
            resourceId: service.Id,
            summary: $"Kreirana usluga: {service.Name}",
            metadata: new { service.DurationMinutes, service.Price });

        return CreatedAtAction(nameof(GetAll), new { id = service.Id }, MapToDto(service));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ServiceDto>> Update(Guid id, [FromBody] UpdateServiceDto dto)
    {
        var businessId = currentBusinessService.GetRequiredBusinessId();
        var service = await dbContext.Services.FirstOrDefaultAsync(x => x.Id == id && x.BusinessId == businessId);
        if (service is null)
        {
            return NotFound("Usluga nije pronađena.");
        }

        service.Name = dto.Name.Trim();
        service.Description = dto.Description?.Trim();
        service.ImageUrl = dto.ImageUrl?.Trim();
        service.DurationMinutes = dto.DurationMinutes;
        service.Price = dto.Price;

        await dbContext.SaveChangesAsync();
        await adminAuditService.LogAsync(
            businessId,
            action: "update",
            resourceType: "service",
            resourceId: service.Id,
            summary: $"Ažurirana usluga: {service.Name}",
            metadata: new { service.DurationMinutes, service.Price, service.IsActive });
        return Ok(MapToDto(service));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var businessId = currentBusinessService.GetRequiredBusinessId();
        var service = await dbContext.Services.FirstOrDefaultAsync(x => x.Id == id && x.BusinessId == businessId);
        if (service is null)
        {
            return NotFound("Usluga nije pronađena.");
        }

        service.IsActive = false;
        await dbContext.SaveChangesAsync();
        await adminAuditService.LogAsync(
            businessId,
            action: "deactivate",
            resourceType: "service",
            resourceId: service.Id,
            summary: $"Deaktivirana usluga: {service.Name}");
        return NoContent();
    }

    private static ServiceDto MapToDto(Domain.Entities.Service service) => new()
    {
        Id = service.Id,
        BusinessId = service.BusinessId,
        Name = service.Name,
        Description = service.Description,
        ImageUrl = service.ImageUrl,
        DurationMinutes = service.DurationMinutes,
        Price = service.Price,
        IsActive = service.IsActive,
        CreatedAt = service.CreatedAt
    };
}
