using System.ComponentModel.DataAnnotations;

namespace MojTermin.Api.Application.DTOs;

public class ServiceDto
{
    public Guid Id { get; set; }
    public Guid BusinessId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }
    public int DurationMinutes { get; set; }
    public decimal Price { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreateServiceDto
{
    [Required, MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    [MaxLength(500)]
    public string? ImageUrl { get; set; }

    [Range(1, 1440)]
    public int DurationMinutes { get; set; }

    [Range(0, 1000000)]
    public decimal Price { get; set; }
}

public class UpdateServiceDto : CreateServiceDto;
