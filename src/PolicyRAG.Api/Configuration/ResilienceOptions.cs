namespace PolicyRAG.Api.Configuration;

/// <summary>
/// Configuration options for resilience policies (retry, circuit breaker, timeout)
/// </summary>
public class ResilienceOptions
{
    public const string SectionName = "Resilience";

    /// <summary>
    /// Maximum number of retry attempts for transient failures
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// Base delay in milliseconds for exponential backoff
    /// </summary>
    public int BaseDelayMs { get; set; } = 1000;

    /// <summary>
    /// Maximum delay in milliseconds between retries
    /// </summary>
    public int MaxDelayMs { get; set; } = 30000;

    /// <summary>
    /// Timeout in seconds for individual operations
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Minimum number of actions in the sampling window before the failure ratio is evaluated.
    /// Prevents the circuit breaker from opening on too few samples.
    /// </summary>
    public int CircuitBreakerMinimumThroughput { get; set; } = 10;

    /// <summary>
    /// Duration in seconds the circuit breaker stays open before allowing test requests
    /// </summary>
    public int CircuitBreakerDurationSeconds { get; set; } = 30;

    /// <summary>
    /// Sampling duration in seconds for measuring failure rate
    /// </summary>
    public int CircuitBreakerSamplingDurationSeconds { get; set; } = 60;
}

/// <summary>
/// OpenAI-specific resilience configuration
/// </summary>
public class OpenAIResilienceOptions : ResilienceOptions
{
    public new const string SectionName = "Resilience:OpenAI";

    /// <summary>
    /// Rate limit delay in milliseconds when 429 is received
    /// </summary>
    public int RateLimitDelayMs { get; set; } = 5000;

    /// <summary>
    /// Maximum tokens per minute budget (for rate limiting)
    /// </summary>
    public int TokensPerMinuteLimit { get; set; } = 90000;
}

/// <summary>
/// Qdrant-specific resilience configuration
/// </summary>
public class QdrantResilienceOptions : ResilienceOptions
{
    public new const string SectionName = "Resilience:Qdrant";

    /// <summary>
    /// Connection timeout in seconds
    /// </summary>
    public int ConnectionTimeoutSeconds { get; set; } = 10;
}
