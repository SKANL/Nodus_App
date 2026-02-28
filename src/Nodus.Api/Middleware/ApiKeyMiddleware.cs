using System.Security.Cryptography;
using System.Text;

namespace Nodus.Api.Middleware;

/// <summary>
/// Middleware de autenticación por API Key.
/// Valida el header <c>X-Api-Key</c> en cada request.
///
/// Configuración en appsettings.json:
/// <code>
/// "Auth": {
///   "ApiKey": "your-secret-key"
/// }
/// </code>
///
/// Si <c>Auth:ApiKey</c> está vacío, la autenticación se desactiva (útil en desarrollo).
/// En producción, configurar vía variable de entorno <c>Auth__ApiKey</c>.
/// </summary>
public class ApiKeyMiddleware
{
    private const string ApiKeyHeaderName = "X-Api-Key";
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiKeyMiddleware> _logger;
    private readonly string? _configuredKey;

    public ApiKeyMiddleware(RequestDelegate next, IConfiguration configuration, ILogger<ApiKeyMiddleware> logger)
    {
        _next = next;
        _logger = logger;
        _configuredKey = configuration["Auth:ApiKey"];
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Si no hay clave configurada, la autenticación está desactivada (modo dev)
        if (string.IsNullOrWhiteSpace(_configuredKey))
        {
            await _next(context);
            return;
        }

        // Preflight CORS pasa sin autenticación
        if (context.Request.Method == HttpMethods.Options)
        {
            await _next(context);
            return;
        }

        // GET públicos (lectura de rubrica de evento para Blazor WASM sin auth)
        // Ajustar según requisitos: aquí sólo lectura sin API key para facilitar el portal de estudiantes
        // Si se desea proteger TODO, eliminar este bloque.
        if (context.Request.Method == HttpMethods.Get &&
            (context.Request.Path.StartsWithSegments("/api/events") ||
             context.Request.Path.StartsWithSegments("/api/projects")))
        {
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(ApiKeyHeaderName, out var providedKey))
        {
            _logger.LogWarning("Request to {Path} rejected: missing {Header}", context.Request.Path, ApiKeyHeaderName);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers.WWWAuthenticate = "ApiKey";
            await context.Response.WriteAsJsonAsync(new
            {
                error = "API key requerida.",
                hint = $"Incluye el header '{ApiKeyHeaderName}' con la clave del evento."
            });
            return;
        }

        if (!CryptographicTimingEquals(providedKey!, _configuredKey))
        {
            _logger.LogWarning("Request to {Path} rejected: invalid API key (last4: {Last4})",
                context.Request.Path,
                providedKey.ToString().Length >= 4
                    ? $"...{providedKey.ToString()[^4..]}"
                    : "****");
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { error = "API key inválida." });
            return;
        }

        await _next(context);
    }

    /// <summary>Comparación en tiempo constante para prevenir timing attacks.</summary>
    private static bool CryptographicTimingEquals(string a, string b)
    {
        var aBytes = Encoding.UTF8.GetBytes(a);
        var bBytes = Encoding.UTF8.GetBytes(b);
        return CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
    }
}
