using Microsoft.Extensions.Options;
using PolicyRAG.Api.Configuration;
using PolicyRAG.Api.HealthChecks;
using PolicyRAG.Api.Interfaces;
using PolicyRAG.Api.Resilience;
using PolicyRAG.Api.Services;
using PolicyRAG.Api.Services.Parsers;
using Qdrant.Client;
using Serilog;

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting PolicyRAG API");
    
    var builder = WebApplication.CreateBuilder(args);

    // Configure Serilog from configuration
    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "PolicyRAG.Api")
        .WriteTo.Console());

    // Add configuration options
    builder.Services.Configure<QdrantOptions>(builder.Configuration.GetSection(QdrantOptions.SectionName));
    builder.Services.Configure<OpenAIOptions>(builder.Configuration.GetSection(OpenAIOptions.SectionName));
    builder.Services.Configure<ChunkingOptions>(builder.Configuration.GetSection(ChunkingOptions.SectionName));
    builder.Services.Configure<RetrievalOptions>(builder.Configuration.GetSection(RetrievalOptions.SectionName));

    // Add resilience policies (retry, circuit breaker, timeout)
    builder.Services.AddResiliencePolicies(builder.Configuration);

    // Add health checks
    builder.Services.AddPolicyRagHealthChecks();

    // Add services to the container.
    builder.Services.AddControllers();
    builder.Services.AddOpenApi();
    builder.Services.AddEndpointsApiExplorer();
    
    // Configure CORS for Angular frontend
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("AllowAngularApp", policy =>
        {
            policy.WithOrigins("http://localhost:4200")
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
        });
    });

    // Configure request size limit for file uploads (50MB)
    builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
    {
        options.MultipartBodyLengthLimit = 52428800; // 50MB
    });

    // Register document parsers
    builder.Services.AddSingleton<IDocumentParser, PdfDocumentParser>();
    builder.Services.AddSingleton<IDocumentParser, DocxDocumentParser>();
    builder.Services.AddSingleton<DocumentParserFactory>();

    // Register tokenizer and chunking service
    builder.Services.AddSingleton<ITokenizer, TiktokenTokenizer>();
    builder.Services.AddSingleton<ChunkingService>();

    // Register embedding service
    builder.Services.AddSingleton<IEmbeddingService, OpenAIEmbeddingService>();

    // Register vector store service
    builder.Services.AddSingleton<QdrantClient>(sp =>
    {
        var config = sp.GetRequiredService<IOptions<QdrantOptions>>().Value;
        return new QdrantClient(config.Host, config.Port);
    });
    builder.Services.AddSingleton<IVectorStoreService, QdrantVectorStoreService>();

    // Register retrieval service
    builder.Services.AddSingleton<RetrievalService>();

    // Register prompt builder and chat completion services
    builder.Services.AddSingleton<IPromptBuilder, PromptBuilder>();
    builder.Services.AddSingleton<IChatCompletionService, OpenAIChatCompletionService>();

    // Register document repository and ingestion service
    builder.Services.AddSingleton<IDocumentRepository, InMemoryDocumentRepository>();
    builder.Services.AddSingleton<IDocumentIngestionService, DocumentIngestionService>();

    var app = builder.Build();

    // Configure graceful shutdown
    var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
    lifetime.ApplicationStopping.Register(() =>
    {
        Log.Information("Application is shutting down...");
        
        // Mark as not ready to stop accepting new requests
        var startupCheck = app.Services.GetService<StartupHealthCheck>();
        if (startupCheck != null)
        {
            startupCheck.IsReady = false;
        }
        
        // Give load balancers time to stop routing traffic
        Thread.Sleep(TimeSpan.FromSeconds(5));
        Log.Information("Graceful shutdown period complete");
    });

    lifetime.ApplicationStopped.Register(() =>
    {
        Log.Information("Application has stopped");
    });

    // Configure the HTTP request pipeline.
    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
    }

    app.UseHttpsRedirection();
    app.UseCors("AllowAngularApp");
    app.UseSerilogRequestLogging();
    
    app.MapControllers();
    
    // Map health check endpoints
    app.MapPolicyRagHealthChecks();

    // Mark startup as complete
    var startupHealthCheck = app.Services.GetService<StartupHealthCheck>();

    // Initialize Qdrant collection (creates it if it doesn't exist)
    var vectorStore = app.Services.GetRequiredService<IVectorStoreService>();
    await vectorStore.InitializeCollectionAsync();

    if (startupHealthCheck != null)
    {
        startupHealthCheck.IsReady = true;
        Log.Information("Application startup complete - ready to accept requests");
    }

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
