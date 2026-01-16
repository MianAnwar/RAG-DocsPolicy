using PolicyRAG.Api.Configuration;
using PolicyRAG.Api.Interfaces;
using PolicyRAG.Api.Services;
using PolicyRAG.Api.Services.Parsers;
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
    builder.Services.AddSingleton<IVectorStoreService, QdrantVectorStoreService>();

    var app = builder.Build();

    // Configure the HTTP request pipeline.
    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
    }

    app.UseHttpsRedirection();
    app.UseCors("AllowAngularApp");
    app.UseSerilogRequestLogging();
    
    app.MapControllers();
    
    // Health check endpoint
    app.MapGet("/health", () => Results.Ok(new { Status = "Healthy", Timestamp = DateTime.UtcNow }))
        .WithName("HealthCheck")
        .WithTags("Health");

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
