using System.ComponentModel.DataAnnotations;

namespace MojTermin.Api.Application.DTOs;

public class ClientDto
{
    public Guid Id { get; set; }
    public Guid BusinessId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreateClientDto
{
    [Required, MaxLength(150)]
    public string FullName { get; set; } = string.Empty;

    [Required, MaxLength(30)]
    public string Phone { get; set; } = string.Empty;

    [MaxLength(120)]
    public string? Email { get; set; }

    [MaxLength(1000)]
    public string? Note { get; set; }
}

public class UpdateClientDto : CreateClientDto;
