using System.Security.Claims;
using MojTermin.Api.Application.Interfaces;

namespace MojTermin.Api.Infrastructure.Services;

public class CurrentBusinessService(IHttpContextAccessor httpContextAccessor) : ICurrentBusinessService
{
    public Guid GetRequiredBusinessId()
    {
        var context = httpContextAccessor.HttpContext
            ?? throw new BadHttpRequestException("HttpContext nije dostupan.");

        var claimValue = context.User.FindFirstValue("businessId");
        if (Guid.TryParse(claimValue, out var claimBusinessId))
        {
            return claimBusinessId;
        }
        throw new UnauthorizedAccessException("BusinessId claim nije pronađen u tokenu.");
    }
}
