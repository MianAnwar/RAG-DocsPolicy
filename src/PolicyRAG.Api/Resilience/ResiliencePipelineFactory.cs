using Microsoft.Extensions.Options;
using PolicyRAG.Api.Configuration;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;

namespace PolicyRAG.Api.Resilience;

/// <summary>
/// Factory for creating resilience pipelines with retry, circuit breaker, and timeout policies
/// </summary>
public class ResiliencePipelineFactory
{
    private readonly ILogger<ResiliencePipelineFactory> _logger;

    public ResiliencePipelineFactory(ILogger<ResiliencePipelineFactory> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Creates a resilience pipeline for OpenAI API calls
    /// </summary>
    public ResiliencePipeline CreateOpenAIPipeline(OpenAIResilienceOptions options)
    {
        return new ResiliencePipelineBuilder()
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = 0.5,
                MinimumThroughput = options.CircuitBreakerMinimumThroughput,
                SamplingDuration = TimeSpan.FromSeconds(options.CircuitBreakerSamplingDurationSeconds),
                BreakDuration = TimeSpan.FromSeconds(options.CircuitBreakerDurationSeconds),
                ShouldHandle = new PredicateBuilder()
                    .Handle<HttpRequestException>()
                    .Handle<TaskCanceledException>(),
                OnOpened = args =>
                {
                    _logger.LogError(
                        "OpenAI circuit breaker OPENED for {Duration}s due to: {Reason}",
                        options.CircuitBreakerDurationSeconds,
                        args.Outcome.Exception?.Message ?? "Multiple failures");
                    return default;
                },
                OnClosed = args =>
                {
                    _logger.LogInformation("OpenAI circuit breaker CLOSED - service recovered");
                    return default;
                },
                OnHalfOpened = args =>
                {
                    _logger.LogInformation("OpenAI circuit breaker HALF-OPEN - testing service");
                    return default;
                }
            })
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = options.MaxRetryAttempts,
                BackoffType = DelayBackoffType.Exponential,
                Delay = TimeSpan.FromMilliseconds(options.BaseDelayMs),
                MaxDelay = TimeSpan.FromMilliseconds(options.MaxDelayMs),
                ShouldHandle = new PredicateBuilder()
                    .Handle<HttpRequestException>()
                    .Handle<TaskCanceledException>()
                    .Handle<TimeoutRejectedException>(),
                OnRetry = args =>
                {
                    var delay = args.RetryDelay;
                    _logger.LogWarning(
                        "OpenAI retry attempt {Attempt}/{MaxAttempts} after {Delay}ms. Reason: {Reason}",
                        args.AttemptNumber,
                        options.MaxRetryAttempts,
                        delay.TotalMilliseconds,
                        args.Outcome.Exception?.Message ?? "Rate limited");
                    return default;
                }
            })
            .AddTimeout(new TimeoutStrategyOptions
            {
                Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds),
                OnTimeout = args =>
                {
                    _logger.LogWarning("OpenAI request timed out after {Timeout}s", 
                        options.TimeoutSeconds);
                    return default;
                }
            })
            .Build();
    }

    /// <summary>
    /// Creates a resilience pipeline for Qdrant vector database calls
    /// </summary>
    public ResiliencePipeline CreateQdrantPipeline(QdrantResilienceOptions options)
    {
        return new ResiliencePipelineBuilder()
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = 0.5,
                MinimumThroughput = options.CircuitBreakerMinimumThroughput,
                SamplingDuration = TimeSpan.FromSeconds(options.CircuitBreakerSamplingDurationSeconds),
                BreakDuration = TimeSpan.FromSeconds(options.CircuitBreakerDurationSeconds),
                ShouldHandle = new PredicateBuilder()
                    .Handle<Grpc.Core.RpcException>()
                    .Handle<HttpRequestException>(),
                OnOpened = args =>
                {
                    _logger.LogError(
                        "Qdrant circuit breaker OPENED for {Duration}s due to: {Reason}",
                        options.CircuitBreakerDurationSeconds,
                        args.Outcome.Exception?.Message ?? "Multiple failures");
                    return default;
                },
                OnClosed = args =>
                {
                    _logger.LogInformation("Qdrant circuit breaker CLOSED - service recovered");
                    return default;
                },
                OnHalfOpened = args =>
                {
                    _logger.LogInformation("Qdrant circuit breaker HALF-OPEN - testing service");
                    return default;
                }
            })
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = options.MaxRetryAttempts,
                BackoffType = DelayBackoffType.Exponential,
                Delay = TimeSpan.FromMilliseconds(options.BaseDelayMs),
                MaxDelay = TimeSpan.FromMilliseconds(options.MaxDelayMs),
                ShouldHandle = new PredicateBuilder()
                    .Handle<Grpc.Core.RpcException>()
                    .Handle<HttpRequestException>()
                    .Handle<TaskCanceledException>()
                    .Handle<TimeoutRejectedException>(),
                OnRetry = args =>
                {
                    _logger.LogWarning(
                        "Qdrant retry attempt {Attempt}/{MaxAttempts} after {Delay}ms. Reason: {Reason}",
                        args.AttemptNumber,
                        options.MaxRetryAttempts,
                        args.RetryDelay.TotalMilliseconds,
                        args.Outcome.Exception?.Message ?? "Unknown");
                    return default;
                }
            })
            .AddTimeout(new TimeoutStrategyOptions
            {
                Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds),
                OnTimeout = args =>
                {
                    _logger.LogWarning("Qdrant request timed out after {Timeout}s", 
                        options.TimeoutSeconds);
                    return default;
                }
            })
            .Build();
    }

    /// <summary>
    /// Creates a generic resilience pipeline with default settings
    /// </summary>
    public ResiliencePipeline CreateDefaultPipeline(ResilienceOptions options)
    {
        return new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = options.MaxRetryAttempts,
                BackoffType = DelayBackoffType.Exponential,
                Delay = TimeSpan.FromMilliseconds(options.BaseDelayMs),
                MaxDelay = TimeSpan.FromMilliseconds(options.MaxDelayMs)
            })
            .AddTimeout(TimeSpan.FromSeconds(options.TimeoutSeconds))
            .Build();
    }

    private static bool IsRateLimitError(object? result)
    {
        // Check if the result indicates a rate limit error
        if (result is Exception ex)
        {
            return ex.Message.Contains("429", StringComparison.OrdinalIgnoreCase) ||
                   ex.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase) ||
                   ex.Message.Contains("too many requests", StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }
}

/// <summary>
/// Extension methods for resilience pipeline registration
/// </summary>
public static class ResilienceServiceExtensions
{
    public static IServiceCollection AddResiliencePolicies(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Register configuration
        services.Configure<ResilienceOptions>(
            configuration.GetSection(ResilienceOptions.SectionName));
        services.Configure<OpenAIResilienceOptions>(
            configuration.GetSection(OpenAIResilienceOptions.SectionName));
        services.Configure<QdrantResilienceOptions>(
            configuration.GetSection(QdrantResilienceOptions.SectionName));

        // Register the pipeline factory
        services.AddSingleton<ResiliencePipelineFactory>();

        // Register named resilience pipelines
        services.AddSingleton(sp =>
        {
            var factory = sp.GetRequiredService<ResiliencePipelineFactory>();
            var openAiOptions = sp.GetRequiredService<IOptions<OpenAIResilienceOptions>>().Value;
            return new OpenAIResiliencePipeline(factory.CreateOpenAIPipeline(openAiOptions));
        });

        services.AddSingleton(sp =>
        {
            var factory = sp.GetRequiredService<ResiliencePipelineFactory>();
            var qdrantOptions = sp.GetRequiredService<IOptions<QdrantResilienceOptions>>().Value;
            return new QdrantResiliencePipeline(factory.CreateQdrantPipeline(qdrantOptions));
        });

        return services;
    }
}

/// <summary>
/// Wrapper for OpenAI-specific resilience pipeline
/// </summary>
public class OpenAIResiliencePipeline
{
    public ResiliencePipeline Pipeline { get; }

    public OpenAIResiliencePipeline(ResiliencePipeline pipeline)
    {
        Pipeline = pipeline;
    }
}

/// <summary>
/// Wrapper for Qdrant-specific resilience pipeline
/// </summary>
public class QdrantResiliencePipeline
{
    public ResiliencePipeline Pipeline { get; }

    public QdrantResiliencePipeline(ResiliencePipeline pipeline)
    {
        Pipeline = pipeline;
    }
}
