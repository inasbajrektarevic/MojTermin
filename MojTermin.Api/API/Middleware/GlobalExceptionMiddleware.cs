using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace MojTermin.Api.API.Middleware;

public class GlobalExceptionMiddleware(
    RequestDelegate next,
    ILogger<GlobalExceptionMiddleware> logger,
    IHostEnvironment environment)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            var traceId = context.TraceIdentifier;
            logger.LogError(ex, "Unhandled exception occurred. TraceId: {TraceId}", traceId);
            var statusCode = ex switch
            {
                BadHttpRequestException => HttpStatusCode.BadRequest,
                UnauthorizedAccessException => HttpStatusCode.Unauthorized,
                DbUpdateException => HttpStatusCode.BadRequest,
                _ => HttpStatusCode.InternalServerError
            };

            context.Response.StatusCode = (int)statusCode;
            context.Response.ContentType = "application/json";

            var userMessage = statusCode switch
            {
                HttpStatusCode.BadRequest when ex is DbUpdateException =>
                    "Zahtjev nije mogao biti sačuvan. Provjerite da li je baza podataka ažurirana (restart API-ja).",
                HttpStatusCode.BadRequest => "Neispravan zahtjev.",
                HttpStatusCode.Unauthorized => "Niste autorizovani za ovu akciju.",
                _ => "Došlo je do greške na serveru."
            };

            var payload = new
            {
                code = statusCode switch
                {
                    HttpStatusCode.BadRequest => "bad_request",
                    HttpStatusCode.Unauthorized => "unauthorized",
                    _ => "server_error"
                },
                message = userMessage,
                details = environment.IsDevelopment() ? GetExceptionDetail(ex) : null,
                traceId
            };

            await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
        }
    }

    private static string GetExceptionDetail(Exception ex)
    {
        if (ex is DbUpdateException dbEx && dbEx.InnerException is not null)
        {
            return $"{ex.Message} | {dbEx.InnerException.Message}";
        }

        return ex.Message;
    }
}
