using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Text.Json;

namespace PolicyRAG.Api.HealthChecks;

/// <summary>
/// Extension methods for configuring health checks
/// </summary>
public static class HealthCheckExtensions
{
    /// <summary>
    /// Adds health checks for all PolicyRAG dependencies
    /// </summary>
    public static IServiceCollection AddPolicyRagHealthChecks(this IServiceCollection services)
    {
        services.AddSingleton<StartupHealthCheck>();
        services.AddHttpClient("OpenAIHealth");
        
        services.AddHealthChecks()
            // Liveness probe - basic check that app is running
            .AddCheck("self", () => HealthCheckResult.Healthy(), tags: new[] { "live" })
            
            // Startup probe - check if app has finished initialization
            .AddCheck<StartupHealthCheck>(
                "startup",
                failureStatus: HealthStatus.Unhealthy,
                tags: new[] { "startup" })
            
            // Readiness probes - check external dependencies
            .AddCheck<QdrantHealthCheck>(
                "qdrant",
                failureStatus: HealthStatus.Unhealthy,
                tags: new[] { "ready", "db", "vector" })
            
            .AddCheck<OpenAIHealthCheck>(
                "openai",
                failureStatus: HealthStatus.Degraded,
                tags: new[] { "ready", "external", "ai" })
            
            .AddCheck<DocumentRepositoryHealthCheck>(
                "document-repository",
                failureStatus: HealthStatus.Unhealthy,
                tags: new[] { "ready", "db" });

        return services;
    }

    /// <summary>
    /// Maps health check endpoints for liveness, readiness, and startup probes
    /// </summary>
    public static IEndpointRouteBuilder MapPolicyRagHealthChecks(this IEndpointRouteBuilder endpoints)
    {
        // Liveness probe - is the application running?
        // Used by Kubernetes to know when to restart a container
        endpoints.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("live"),
            ResponseWriter = WriteHealthCheckResponse
        }).WithTags("Health");

        // Readiness probe - is the application ready to accept traffic?
        // Used by Kubernetes to know when to route traffic to the pod
        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready"),
            ResponseWriter = WriteHealthCheckResponse
        }).WithTags("Health");

        // Startup probe - has the application finished starting?
        // Used by Kubernetes to know when the app has finished initialization
        endpoints.MapHealthChecks("/health/startup", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("startup"),
            ResponseWriter = WriteHealthCheckResponse
        }).WithTags("Health");

        // Full health check - all checks
        endpoints.MapHealthChecks("/health", new HealthCheckOptions
        {
            ResponseWriter = WriteHealthCheckResponse
        }).WithTags("Health");

        // Detailed health check with full information
        endpoints.MapHealthChecks("/health/detail", new HealthCheckOptions
        {
            ResponseWriter = WriteDetailedHealthCheckResponse
        }).WithTags("Health");

        return endpoints;
    }

    private static Task WriteHealthCheckResponse(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";

        var response = new
        {
            status = report.Status.ToString(),
            timestamp = DateTime.UtcNow,
            duration = report.TotalDuration.TotalMilliseconds
        };

        return context.Response.WriteAsJsonAsync(response);
    }

    private static Task WriteDetailedHealthCheckResponse(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";

        var response = new
        {
            status = report.Status.ToString(),
            timestamp = DateTime.UtcNow,
            duration = report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                duration = e.Value.Duration.TotalMilliseconds,
                description = e.Value.Description,
                exception = e.Value.Exception?.Message,
                data = e.Value.Data.Count > 0 ? e.Value.Data : null,
                tags = e.Value.Tags
            })
        };

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        return context.Response.WriteAsJsonAsync(response, options);
    }
}
