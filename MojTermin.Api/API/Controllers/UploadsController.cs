using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MojTermin.Api.Application.DTOs;

namespace MojTermin.Api.API.Controllers;

[ApiController]
[Route("api/uploads")]
[EnableRateLimiting("auth")]
public class UploadsController(IWebHostEnvironment environment, ILogger<UploadsController> logger) : ControllerBase
{
    private static readonly string[] AllowedExtensions = [".jpg", ".jpeg", ".png", ".webp"];
    private const long MaxFileSizeBytes = 5 * 1024 * 1024;

    // Magic-byte signatures for the formats we accept. Verified against the first bytes of the upload
    // so an attacker cannot rename .html as .jpg and have it stored under wwwroot.
    private static readonly (string Extension, byte[] Signature)[] AllowedSignatures =
    {
        (".jpg",  new byte[] { 0xFF, 0xD8, 0xFF }),
        (".jpeg", new byte[] { 0xFF, 0xD8, 0xFF }),
        (".png",  new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
        (".webp", new byte[] { 0x52, 0x49, 0x46, 0x46 })
    };

    [HttpPost("logo")]
    [AllowAnonymous]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxFileSizeBytes)]
    public Task<ActionResult<UploadFileResponseDto>> UploadLogo([FromForm] UploadImageFormRequest request) =>
        PersistValidatedImageAsync(request.File, "logos");

    /// Owner-only: stores under wwwroot/uploads/services/… and returns an absolute URL for service imageUrl.
    [HttpPost("service-image")]
    [Authorize(Policy = "OwnerOnly")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxFileSizeBytes)]
    public Task<ActionResult<UploadFileResponseDto>> UploadServiceImage([FromForm] UploadImageFormRequest request) =>
        PersistValidatedImageAsync(request.File, "services");

    private async Task<ActionResult<UploadFileResponseDto>> PersistValidatedImageAsync(IFormFile? file, string uploadsFolderName)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest("Datoteka nije odabrana.");
        }

        if (file.Length > MaxFileSizeBytes)
        {
            return BadRequest("Maksimalna veličina slike je 5MB.");
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension))
        {
            return BadRequest("Dozvoljeni formati su .jpg, .jpeg, .png i .webp.");
        }

        if (!await IsExtensionMatchingContentAsync(file, extension))
        {
            logger.LogWarning(
                "Rejected upload with mismatched magic bytes. Reported extension={Extension}, RemoteIp={RemoteIp}",
                extension,
                HttpContext.Connection.RemoteIpAddress);
            return BadRequest("Datoteka nije validna slika.");
        }

        var webRoot = environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot");
        var now = DateTime.UtcNow;
        var monthSegment = $"{now:yyyy}/{now:MM}";
        var uploadsDir = Path.Combine(webRoot, "uploads", uploadsFolderName, $"{now:yyyy}", $"{now:MM}");
        Directory.CreateDirectory(uploadsDir);

        var generatedFileName = $"{Guid.NewGuid():N}{extension}";
        var filePath = Path.Combine(uploadsDir, generatedFileName);

        await using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        var url = $"{Request.Scheme}://{Request.Host}/uploads/{uploadsFolderName}/{monthSegment}/{generatedFileName}";
        return Ok(new UploadFileResponseDto
        {
            Url = url,
            FileName = generatedFileName
        });
    }

    private static async Task<bool> IsExtensionMatchingContentAsync(IFormFile file, string extension)
    {
        var maxSignatureLength = AllowedSignatures.Max(x => x.Signature.Length);
        var buffer = new byte[maxSignatureLength];

        await using var stream = file.OpenReadStream();
        var read = await stream.ReadAsync(buffer.AsMemory(0, maxSignatureLength));
        if (read < 4)
        {
            return false;
        }

        foreach (var (allowedExtension, signature) in AllowedSignatures)
        {
            if (allowedExtension != extension)
            {
                continue;
            }

            if (read < signature.Length)
            {
                return false;
            }

            var matches = true;
            for (var i = 0; i < signature.Length; i++)
            {
                if (buffer[i] != signature[i])
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
            {
                return true;
            }
        }

        return false;
    }
}

public sealed class UploadImageFormRequest
{
    public IFormFile? File { get; init; }
}
