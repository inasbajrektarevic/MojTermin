using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MojTermin.Api.Application;
using MojTermin.Api.Application.DTOs;
using MojTermin.Api.Application.Interfaces;
using MojTermin.Api.Application.Validation;
using MojTermin.Api.Infrastructure.Data;
using System.Globalization;
using System.Text;

namespace MojTermin.Api.API.Controllers;

[ApiController]
[Route("api/clients")]
[Authorize(Policy = "OwnerOnly")]
public class ClientsController(
    MojTerminDbContext dbContext,
    ICurrentBusinessService currentBusinessService,
    IAdminAuditService adminAuditService) : ControllerBase
{
    [HttpGet("export.csv")]
    public async Task<IActionResult> ExportCsv()
    {
        var businessId = currentBusinessService.GetRequiredBusinessId();
        var rows = await dbContext.Clients
            .Where(x => x.BusinessId == businessId)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new ClientDto
            {
                Id = x.Id,
                BusinessId = x.BusinessId,
                FullName = x.FullName,
                Phone = x.Phone,
                Email = x.Email,
                Note = x.Note,
                CreatedAt = x.CreatedAt
            })
            .ToListAsync();

        var csv = BuildClientsCsv(rows);
        var bytes = Encoding.UTF8.GetBytes(csv);
        var fileName = $"clients-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv";
        await adminAuditService.LogAsync(
            businessId,
            action: "export",
            resourceType: "client",
            summary: $"Izvezen CSV klijenata ({rows.Count} zapisa)");
        return File(bytes, "text/csv; charset=utf-8", fileName);
    }

    [HttpGet]
    public async Task<ActionResult<List<ClientDto>>> GetAll(
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null)
    {
        var businessId = currentBusinessService.GetRequiredBusinessId();
        var clients = await dbContext.Clients
            .Where(x => x.BusinessId == businessId)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new ClientDto
            {
                Id = x.Id,
                BusinessId = x.BusinessId,
                FullName = x.FullName,
                Phone = x.Phone,
                Email = x.Email,
                Note = x.Note,
                CreatedAt = x.CreatedAt
            })
            .ToPagedListAsync(page, pageSize);

        return Ok(clients);
    }

    [HttpPost]
    public async Task<ActionResult<ClientDto>> Create([FromBody] CreateClientDto dto)
    {
        if (!OptionalEmail.IsValid(dto.Email))
        {
            return BadRequest("Email nije ispravan.");
        }

        if (!RegionalPhone.IsValid(dto.Phone))
        {
            return BadRequest("Telefon mora biti u međunarodnom formatu sa pozivnim brojem (npr. +387...).");
        }

        var businessId = currentBusinessService.GetRequiredBusinessId();
        var client = new Domain.Entities.Client
        {
            Id = Guid.NewGuid(),
            BusinessId = businessId,
            FullName = dto.FullName.Trim(),
            Phone = RegionalPhone.Normalize(dto.Phone),
            Email = OptionalEmail.Normalize(dto.Email),
            Note = dto.Note?.Trim(),
            CreatedAt = DateTime.UtcNow
        };

        dbContext.Clients.Add(client);
        await dbContext.SaveChangesAsync();
        await adminAuditService.LogAsync(
            businessId,
            action: "create",
            resourceType: "client",
            resourceId: client.Id,
            summary: $"Kreiran klijent: {client.FullName}",
            metadata: new { client.Phone, client.Email });

        return CreatedAtAction(nameof(GetAll), new { id = client.Id }, MapToDto(client));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ClientDto>> Update(Guid id, [FromBody] UpdateClientDto dto)
    {
        var businessId = currentBusinessService.GetRequiredBusinessId();
        var client = await dbContext.Clients
            .FirstOrDefaultAsync(x => x.Id == id && x.BusinessId == businessId);

        if (client is null)
        {
            return NotFound("Klijent nije pronađen.");
        }

        if (!OptionalEmail.IsValid(dto.Email))
        {
            return BadRequest("Email nije ispravan.");
        }

        if (!RegionalPhone.IsValid(dto.Phone))
        {
            return BadRequest("Telefon mora biti u međunarodnom formatu sa pozivnim brojem (npr. +387...).");
        }

        client.FullName = dto.FullName.Trim();
        client.Phone = RegionalPhone.Normalize(dto.Phone);
        client.Email = OptionalEmail.Normalize(dto.Email);
        client.Note = dto.Note?.Trim();

        await dbContext.SaveChangesAsync();
        await adminAuditService.LogAsync(
            businessId,
            action: "update",
            resourceType: "client",
            resourceId: client.Id,
            summary: $"Ažuriran klijent: {client.FullName}",
            metadata: new { client.Phone, client.Email });
        return Ok(MapToDto(client));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var businessId = currentBusinessService.GetRequiredBusinessId();
        var client = await dbContext.Clients
            .FirstOrDefaultAsync(x => x.Id == id && x.BusinessId == businessId);

        if (client is null)
        {
            return NotFound("Klijent nije pronađen.");
        }

        var hasAppointments = await dbContext.Appointments
            .AnyAsync(x => x.ClientId == client.Id && x.BusinessId == businessId);
        if (hasAppointments)
        {
            return BadRequest("Klijent ima povezane termine i ne može biti obrisan.");
        }

        dbContext.Clients.Remove(client);
        await dbContext.SaveChangesAsync();
        await adminAuditService.LogAsync(
            businessId,
            action: "delete",
            resourceType: "client",
            resourceId: client.Id,
            summary: $"Obrisan klijent: {client.FullName}",
            metadata: new { client.Phone, client.Email });
        return NoContent();
    }

    private static ClientDto MapToDto(Domain.Entities.Client client) => new()
    {
        Id = client.Id,
        BusinessId = client.BusinessId,
        FullName = client.FullName,
        Phone = client.Phone,
        Email = client.Email,
        Note = client.Note,
        CreatedAt = client.CreatedAt
    };

    private static string BuildClientsCsv(IEnumerable<ClientDto> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Ime i prezime,Telefon,Email,Napomena,Kreiran UTC");

        foreach (var row in rows)
        {
            sb.Append(EscapeCsv(row.FullName));
            sb.Append(',');
            sb.Append(EscapeCsv(row.Phone));
            sb.Append(',');
            sb.Append(EscapeCsv(row.Email));
            sb.Append(',');
            sb.Append(EscapeCsv(row.Note));
            sb.Append(',');
            sb.Append(EscapeCsv(row.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)));
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "\"\"";
        }

        var escaped = value.Replace("\"", "\"\"");
        return $"\"{escaped}\"";
    }
}
