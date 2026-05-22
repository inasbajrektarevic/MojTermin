using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MojTermin.Api.Application.DTOs;
using MojTermin.Api.Infrastructure.Services;

namespace MojTermin.Api.API.Controllers;

[ApiController]
[Route("api/public")]
public sealed class PublicConfigController(IOptions<AuthOptions> authOptions) : ControllerBase
{
    /// <summary>
    /// Lets the marketing shell show either self-serve registration or a sales CTA,
    /// matching the same flag that gates <c>POST /api/businesses/register</c>.
    /// </summary>
    [HttpGet("site-config")]
    [AllowAnonymous]
    public ActionResult<PublicSiteConfigDto> GetSiteConfig()
    {
        return Ok(new PublicSiteConfigDto
        {
            AllowPublicRegistration = authOptions.Value.AllowPublicRegistration
        });
    }
}
