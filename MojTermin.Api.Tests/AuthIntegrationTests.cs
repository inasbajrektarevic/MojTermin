using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MojTermin.Api.Application.DTOs;
using MojTermin.Api.Domain.Entities;
using MojTermin.Api.Domain.Enums;
using MojTermin.Api.Infrastructure.Data;
using MojTermin.Api.Infrastructure.Services;
using MojTermin.Api.Tests.Infrastructure;

namespace MojTermin.Api.Tests;

public class AuthIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly CustomWebApplicationFactory _factory;

    public AuthIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Login_Returns_Jwt_And_RefreshToken()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
        {
            UsernameOrEmail = "owner",
            Password = "Owner123!"
        });

        response.EnsureSuccessStatusCode();
        var auth = await response.Content.ReadFromJsonAsync<AuthResponseDto>();

        Assert.NotNull(auth);
        Assert.False(string.IsNullOrWhiteSpace(auth!.Token));
        Assert.False(string.IsNullOrWhiteSpace(auth.RefreshToken));
        Assert.Equal("Owner", auth.Role);
    }

    [Fact]
    public async Task Protected_Endpoint_Without_Token_Returns_Unauthorized()
    {
        var response = await _client.GetAsync("/api/dashboard/summary");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_Tokens_Are_Stored_As_Hashes_Not_Plaintext()
    {
        // SECURITY: A DB dump must not yield working sessions. The token returned to the
        // client is the raw secret; the row persisted in RefreshTokens.Token must be its
        // SHA-256 digest. Verifying via direct DB lookup catches accidental regressions
        // (e.g. someone introducing a code path that bypasses RefreshTokenHasher.Hash).
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
        {
            UsernameOrEmail = "owner",
            Password = "Owner123!"
        });
        loginResponse.EnsureSuccessStatusCode();
        var auth = await loginResponse.Content.ReadFromJsonAsync<AuthResponseDto>();
        Assert.NotNull(auth);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MojTerminDbContext>();
        var expectedHash = RefreshTokenHasher.Hash(auth!.RefreshToken);

        var matchByPlaintext = db.RefreshTokens.Any(x => x.Token == auth.RefreshToken);
        Assert.False(matchByPlaintext, "Refresh token must NOT be stored as plaintext in DB.");

        var matchByHash = db.RefreshTokens.Any(x => x.Token == expectedHash);
        Assert.True(matchByHash, "DB row must store the SHA-256 hash of the raw token.");
    }

    [Fact]
    public async Task Upload_Logo_Rejects_File_With_Mismatched_Magic_Bytes()
    {
        // An attacker renames an HTML payload to logo.jpg. The extension whitelist
        // passes, but the magic-byte sniffer must reject it.
        var maliciousContent = "<html><script>alert(1)</script></html>"u8.ToArray();
        using var multipart = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(maliciousContent);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("image/jpeg");
        multipart.Add(fileContent, "File", "logo.jpg");

        var response = await _client.PostAsync("/api/uploads/logo", multipart);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Upload_Logo_Accepts_Real_Png_File()
    {
        // Minimal valid 1x1 PNG.
        var pngBytes = new byte[]
        {
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
            0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
            0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
            0x89, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x44, 0x41,
            0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
            0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
            0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
            0x42, 0x60, 0x82
        };
        using var multipart = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(pngBytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("image/png");
        multipart.Add(fileContent, "File", "logo.png");

        var response = await _client.PostAsync("/api/uploads/logo", multipart);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Legacy_Auth_Register_Endpoint_Is_Removed()
    {
        // Public POST /api/auth/register used to accept a client-supplied BusinessId
        // and hardcode Role="Owner". It was removed during P0 hardening.
        var response = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            FullName = "Mallory",
            Email = "mallory@example.com",
            Username = "mallory",
            Password = "Mallory123!",
            BusinessId = Guid.NewGuid()
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_Rotates_RefreshToken_And_Returns_New_Jwt()
    {
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
        {
            UsernameOrEmail = "owner",
            Password = "Owner123!"
        });
        loginResponse.EnsureSuccessStatusCode();
        var login = await loginResponse.Content.ReadFromJsonAsync<AuthResponseDto>();
        Assert.NotNull(login);

        var refreshResponse = await _client.PostAsJsonAsync("/api/auth/refresh", new RefreshTokenRequestDto
        {
            RefreshToken = login!.RefreshToken
        });
        refreshResponse.EnsureSuccessStatusCode();

        var refreshed = await refreshResponse.Content.ReadFromJsonAsync<AuthResponseDto>();
        Assert.NotNull(refreshed);
        Assert.NotEqual(login.RefreshToken, refreshed!.RefreshToken);
        Assert.False(string.IsNullOrWhiteSpace(refreshed.Token));
    }

    [Fact]
    public async Task Refresh_Token_Reuse_Attempt_Revokes_Descendant_Token()
    {
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
        {
            UsernameOrEmail = "owner",
            Password = "Owner123!"
        });
        loginResponse.EnsureSuccessStatusCode();
        var login = await loginResponse.Content.ReadFromJsonAsync<AuthResponseDto>();
        Assert.NotNull(login);

        var firstRefresh = await _client.PostAsJsonAsync("/api/auth/refresh", new RefreshTokenRequestDto
        {
            RefreshToken = login!.RefreshToken
        });
        firstRefresh.EnsureSuccessStatusCode();
        var rotated = await firstRefresh.Content.ReadFromJsonAsync<AuthResponseDto>();
        Assert.NotNull(rotated);

        // Reuse of old revoked refresh token should invalidate descendant token chain.
        var reuseAttempt = await _client.PostAsJsonAsync("/api/auth/refresh", new RefreshTokenRequestDto
        {
            RefreshToken = login.RefreshToken
        });
        Assert.Equal(HttpStatusCode.Unauthorized, reuseAttempt.StatusCode);

        var descendantAttempt = await _client.PostAsJsonAsync("/api/auth/refresh", new RefreshTokenRequestDto
        {
            RefreshToken = rotated!.RefreshToken
        });
        Assert.Equal(HttpStatusCode.Unauthorized, descendantAttempt.StatusCode);
    }

    [Fact]
    public async Task Protected_Endpoint_With_Valid_Token_Returns_Ok()
    {
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
        {
            UsernameOrEmail = "owner",
            Password = "Owner123!"
        });
        loginResponse.EnsureSuccessStatusCode();
        var auth = await loginResponse.Content.ReadFromJsonAsync<AuthResponseDto>();
        Assert.NotNull(auth);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Token);

        var response = await _client.GetAsync("/api/dashboard/summary");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Revoke_Should_Not_Revoke_RefreshToken_From_Other_Business()
    {
        var ownerLoginResponse = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
        {
            UsernameOrEmail = "owner",
            Password = "Owner123!"
        });
        ownerLoginResponse.EnsureSuccessStatusCode();
        var ownerAuth = await ownerLoginResponse.Content.ReadFromJsonAsync<AuthResponseDto>();
        Assert.NotNull(ownerAuth);

        string otherBusinessRefreshToken;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MojTerminDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AppUser>>();

            var business = new Business
            {
                Id = Guid.NewGuid(),
                Name = "Other Business",
                Slug = "other-business",
                BusinessType = BusinessType.Other,
                Phone = "555-000",
                Email = "other@local.test",
                Address = "Other",
                Description = "Other",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            var user = new AppUser
            {
                Id = Guid.NewGuid(),
                BusinessId = business.Id,
                FullName = "Other Owner",
                Email = "other-owner@local.test",
                Username = "other-owner",
                Role = "Owner",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            user.PasswordHash = hasher.HashPassword(user, "Owner123!");

            var rawRefreshToken = "cross-business-token";
            var refreshToken = new RefreshToken
            {
                Id = Guid.NewGuid(),
                AppUserId = user.Id,
                Token = RefreshTokenHasher.Hash(rawRefreshToken),
                CreatedAtUtc = DateTime.UtcNow,
                ExpiresAtUtc = DateTime.UtcNow.AddDays(14)
            };

            db.Businesses.Add(business);
            db.AppUsers.Add(user);
            db.RefreshTokens.Add(refreshToken);
            await db.SaveChangesAsync();

            otherBusinessRefreshToken = rawRefreshToken;
        }

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerAuth!.Token);
        var revokeResponse = await _client.PostAsJsonAsync("/api/auth/revoke", new RefreshTokenRequestDto
        {
            RefreshToken = otherBusinessRefreshToken
        });

        Assert.Equal(HttpStatusCode.NotFound, revokeResponse.StatusCode);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<MojTerminDbContext>();
        var otherBusinessTokenHash = RefreshTokenHasher.Hash(otherBusinessRefreshToken);
        var stored = await verifyDb.RefreshTokens.FirstAsync(x => x.Token == otherBusinessTokenHash);
        Assert.Null(stored.RevokedAtUtc);
    }
}
