# PolicyRAG - Policy Document Q&A System

A production-grade Retrieval-Augmented Generation (RAG) application for answering questions about company policy documents.

## Tech Stack

- **Backend**: .NET Core 9.0 Web API
- **Frontend**: Angular 21 with Angular Material
- **Vector Database**: Qdrant
- **LLM**: OpenAI GPT-4o / Azure OpenAI
- **Embeddings**: text-embedding-3-small (1536 dimensions)

## Project Structure

```
ai-rag-app/
├── src/
│   └── PolicyRAG.Api/          # .NET Core Web API
│       ├── Configuration/       # Options classes
│       ├── Controllers/         # API endpoints
│       ├── Services/            # Business logic
│       └── Models/              # Domain models
├── policy-rag-ui/              # Angular frontend
│   └── src/app/
│       ├── core/               # Services, models
│       ├── features/           # Chat, Documents modules
│       └── shared/             # Shared components
├── docker-compose.yml          # Qdrant container
└── PolicyRAG.sln               # Solution file
```

## Table of Contents

1. [Prerequisites](#prerequisites)
2. [Infrastructure — Qdrant](#infrastructure--qdrant)
3. [Backend Configuration](#backend-configuration)
   - [OpenAI Settings](#openai-settings)
   - [Qdrant Settings](#qdrant-settings)
   - [Chunking Settings](#chunking-settings)
   - [Retrieval Settings](#retrieval-settings)
   - [Resilience Settings](#resilience-settings)
   - [Securing the API Key with User Secrets](#securing-the-api-key-with-user-secrets)
   - [Running the API](#running-the-api)
4. [Frontend Configuration](#frontend-configuration)
   - [Environment Files](#environment-files)
   - [Dev Proxy Configuration](#dev-proxy-configuration)
   - [Running the Frontend](#running-the-frontend)
5. [Verifying the Setup](#verifying-the-setup)
6. [API Endpoints](#api-endpoints)
7. [Advanced Configuration](#advanced-configuration)
8. [Production Considerations](#production-considerations)
9. [Development Phases](#development-phases)

---

## Prerequisites

Install all of the following before continuing.

| Tool | Minimum Version | Install Link |
|------|----------------|-------------|
| .NET SDK | 9.0 | https://dotnet.microsoft.com/download |
| Node.js | 20.x LTS | https://nodejs.org/ |
| Angular CLI | latest | `npm install -g @angular/cli` |
| Docker Desktop | latest | https://www.docker.com/products/docker-desktop/ |

You also need an **OpenAI API key** (`sk-...`). Obtain one from https://platform.openai.com/api-keys.

Verify your installations:

```bash
dotnet --version     # should print 9.x.x
node --version       # should print v20.x.x or higher
ng version           # should print Angular CLI version
docker --version     # should print Docker version
```

---

## Infrastructure — Qdrant

PolicyRAG uses [Qdrant](https://qdrant.tech/) as its vector database. A `docker-compose.yml` at the repo root spins it up as a local container.

### Start Qdrant

```bash
# From the repo root
docker-compose up -d
```

This starts a container named `policy-rag-qdrant` with:

| Resource | Detail |
|----------|--------|
| REST API | `http://localhost:6333` |
| gRPC (used by the .NET client) | `localhost:6334` |
| Persistent storage | Docker volume `qdrant_storage` |

### Verify Qdrant is running

```bash
curl http://localhost:6333/healthz
# Expected: {"title":"qdrant - healthy"}
```

Or open `http://localhost:6333/dashboard` in a browser to view the Qdrant web UI.

### Stop / remove

```bash
docker-compose down          # stop container, keep data volume
docker-compose down -v       # stop container AND delete all stored vectors
```

> **Note:** The `qdrant_storage` Docker volume persists all ingested document vectors between container restarts. Use `docker-compose down -v` only when you want a clean slate.

---

## Backend Configuration

All backend settings live in `src/PolicyRAG.Api/appsettings.json` (production defaults) and `src/PolicyRAG.Api/appsettings.Development.json` (development overrides). The ASP.NET Core configuration system merges these files, with the environment-specific file taking precedence.

### OpenAI Settings

**Configuration section:** `OpenAI`

| Property | Type | Default | Required | Description |
|----------|------|---------|----------|-------------|
| `ApiKey` | string | _(none)_ | **Yes** | OpenAI secret key — see [Securing the API Key](#securing-the-api-key-with-user-secrets) |
| `EmbeddingModel` | string | `text-embedding-3-small` | No | Model used to generate document embeddings. Dimension must match `Qdrant:VectorSize`. |
| `ChatModel` | string | `gpt-4o` | No | Model used for chat completions. |

Example (`appsettings.Development.json`):

```json
"OpenAI": {
  "ApiKey": "sk-...",
  "EmbeddingModel": "text-embedding-3-small",
  "ChatModel": "gpt-4o"
}
```

> **Supported embedding models and their vector sizes:**
>
> | Model | Vector Size |
> |-------|-------------|
> | `text-embedding-3-small` | 1536 |
> | `text-embedding-3-large` | 3072 |
> | `text-embedding-ada-002` | 1536 |
>
> If you change `EmbeddingModel` to one with a different dimension, you **must** also update `Qdrant:VectorSize` and re-create the Qdrant collection (all previously ingested documents will need to be re-uploaded).

---

### Qdrant Settings

**Configuration section:** `Qdrant`

| Property | Type | Default (prod) | Default (dev) | Description |
|----------|------|----------------|---------------|-------------|
| `Host` | string | `localhost` | `localhost` | Hostname of the Qdrant server |
| `Port` | int | `6334` | `6334` | gRPC port |
| `CollectionName` | string | `policy_documents` | `policy_documents_dev` | Name of the Qdrant collection that stores vectors |
| `VectorSize` | ulong | `1536` | `1536` | Embedding vector dimension — must match the chosen `OpenAI:EmbeddingModel` |
| `ApiKey` | string | _(none)_ | _(none)_ | Optional — required only if Qdrant is configured with authentication |

> The Qdrant collection is created automatically on first run if it does not exist. Separate collection names for `Development` and `Production` (the default) prevent dev ingestion from polluting the production index.

---

### Chunking Settings

**Configuration section:** `Chunking`

Controls how uploaded documents are split into chunks before embedding.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `MaxChunkSize` | int | `512` | Maximum number of tokens per chunk |
| `ChunkOverlap` | int | `50` | Number of tokens shared between adjacent chunks — preserves context across boundaries |
| `MinChunkSize` | int | `100` | Chunks smaller than this are discarded |
| `Separators` | string[] | `["\n\n", "\n", ". ", " "]` | Ordered list of text separators tried when splitting — earlier separators are preferred |

**Tuning guidance:**

- Increase `MaxChunkSize` (e.g. `1024`) for documents with long paragraphs where more context per chunk improves answer quality — at the cost of more tokens per embedding call.
- Increase `ChunkOverlap` (e.g. `100`) if answers are truncated or miss context that spans a chunk boundary.
- Decrease `MinChunkSize` if short sections (e.g. headers, lists) are being dropped.

---

### Retrieval Settings

**Configuration section:** `Retrieval`

Controls how the vector search selects context chunks for the LLM.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `DefaultTopK` | int | `5` | Number of chunks retrieved per query |
| `ScoreThreshold` | float | `0.7` | Minimum cosine similarity score — chunks scoring below this are excluded |
| `MmrDiversity` | float | `0.3` | Maximum Marginal Relevance diversity factor (0 = most similar, 1 = most diverse) |

**Tuning guidance:**

- Increase `DefaultTopK` (e.g. `10`) if answers are missing relevant information from the document corpus.
- Lower `ScoreThreshold` (e.g. `0.5`) if retrieval returns too few results; raise it (e.g. `0.85`) to reduce noise.
- Raise `MmrDiversity` (e.g. `0.5`) if retrieved chunks are repetitive.

---

### Resilience Settings

**Configuration section:** `Resilience`

The backend uses [Polly](https://github.com/App-vNext/Polly) resilience pipelines for calls to both OpenAI and Qdrant. There is a base set of defaults and two service-specific overrides.

#### Base settings (`Resilience`)

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `MaxRetryAttempts` | int | `3` | Number of retry attempts before failing |
| `BaseDelayMs` | int | `1000` | Initial exponential backoff delay (ms) |
| `MaxDelayMs` | int | `30000` | Maximum backoff delay cap (ms) |
| `TimeoutSeconds` | int | `30` | Per-operation timeout |
| `CircuitBreakerMinimumThroughput` | int | `10` | Minimum actions in the sampling window before the failure ratio is evaluated |
| `CircuitBreakerDurationSeconds` | int | `30` | How long the circuit stays open (seconds) |
| `CircuitBreakerSamplingDurationSeconds` | int | `60` | Sampling window for measuring failures (seconds) |

#### OpenAI-specific overrides (`Resilience:OpenAI`)

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `TimeoutSeconds` | int | `60` | Longer timeout for LLM completions |
| `RateLimitDelayMs` | int | `5000` | Extra delay injected on HTTP 429 (rate-limit) responses |
| `TokensPerMinuteLimit` | int | `90000` | TPM rate tracking ceiling (adjust to match your OpenAI tier) |

#### Qdrant-specific overrides (`Resilience:Qdrant`)

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `BaseDelayMs` | int | `500` | Shorter initial backoff for local gRPC calls |
| `MaxDelayMs` | int | `10000` | Lower cap — Qdrant errors typically resolve quickly |
| `ConnectionTimeoutSeconds` | int | `10` | gRPC connection timeout |

All other properties not listed under `Resilience:OpenAI` or `Resilience:Qdrant` inherit from the base `Resilience` section.

---

### Securing the API Key with User Secrets

> **Warning:** `src/PolicyRAG.Api/appsettings.Development.json` may contain a hardcoded `OpenAI:ApiKey`. **Do not commit a real API key to source control.** The recommended approach for local development is [.NET User Secrets](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets).

```bash
cd src/PolicyRAG.Api

# Initialize user secrets for this project (only needed once)
dotnet user-secrets init

# Store your OpenAI API key
dotnet user-secrets set "OpenAI:ApiKey" "sk-your-key-here"
```

User Secrets are stored outside the repository at `~/.microsoft/usersecrets/<project-id>/secrets.json` and are never committed to git. After setting the secret, clear the `ApiKey` value in `appsettings.Development.json`:

```json
"OpenAI": {
  "ApiKey": "",
  "EmbeddingModel": "text-embedding-3-small",
  "ChatModel": "gpt-4o"
}
```

Verify:

```bash
dotnet user-secrets list
# OpenAI:ApiKey = sk-...
```

---

### Running the API

```bash
cd src/PolicyRAG.Api
dotnet run
```

| URL | Profile |
|-----|---------|
| `http://localhost:5058` | `http` (default) |
| `https://localhost:7040` + `http://localhost:5058` | `https` |

To use the HTTPS profile:

```bash
dotnet run --launch-profile https
```

> If this is your first time using HTTPS locally: `dotnet dev-certs https --trust`

The Swagger UI is available at `http://localhost:5058/swagger` in the Development environment.

---

## Frontend Configuration

### Environment Files

The Angular app has two environment files in `policy-rag-ui/src/environments/`:

**`environment.ts`** — used during `ng serve` (development):

```typescript
export const environment = {
  production: false,
  apiUrl: 'http://localhost:5058'
};
```

**`environment.prod.ts`** — used during `ng build` (production):

```typescript
export const environment = {
  production: true,
  apiUrl: '/api'
};
```

If you change the backend port in `launchSettings.json`, update `apiUrl` in `environment.ts` to match.

---

### Dev Proxy Configuration

`policy-rag-ui/proxy.conf.json` defines a dev-server proxy used by `ng serve`:

```json
{
  "/api": {
    "target": "https://localhost:5001",
    "secure": false,
    "changeOrigin": true
  }
}
```

> **Known discrepancy:** The proxy target (`https://localhost:5001`) does not match the actual backend ports defined in `launchSettings.json` (`http://localhost:5058` / `https://localhost:7040`). Because `environment.ts` already points directly to `http://localhost:5058`, the Angular app bypasses this proxy in development. If you intend to use the proxy, update the target:
>
> ```json
> { "/api": { "target": "http://localhost:5058", "secure": false, "changeOrigin": true } }
> ```

---

### Running the Frontend

```bash
cd policy-rag-ui
npm install      # first time or after pulling changes
npm start        # serves at http://localhost:4200
```

> The backend CORS policy allows requests from `http://localhost:4200` by default. If you change the Angular dev server port, update the CORS origin in `src/PolicyRAG.Api/Program.cs`.

---

## Verifying the Setup

### Backend health endpoints

| Endpoint | What it checks | Expected status |
|----------|---------------|-----------------|
| `GET /health/live` | API process is alive | `Healthy` |
| `GET /health/startup` | Startup initialization completed | `Healthy` |
| `GET /health/ready` | Qdrant and OpenAI reachable | `Healthy` |
| `GET /health/detail` | All checks with full detail | JSON report |

```bash
curl http://localhost:5058/health/live
curl http://localhost:5058/health/detail | jq
```

> The `openai` check reports as `Degraded` (not `Unhealthy`) if the OpenAI API is unreachable — the app will still start, but chat and embedding calls will fail at runtime.

### End-to-end smoke test

1. Open `http://localhost:4200` in a browser.
2. Navigate to **Documents → Upload** and upload a PDF or DOCX file.
3. Navigate to **Chat** and ask a question about the uploaded document.
4. A response with source citations should appear.

---

## API Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/documents/upload` | POST | Upload policy documents |
| `/api/documents` | GET | List all documents |
| `/api/documents/{id}` | GET | Get a single document |
| `/api/documents/{id}` | DELETE | Delete a document |
| `/api/documents/{id}/reprocess` | POST | Regenerate embeddings |
| `/api/chat` | POST | Ask a question |
| `/api/chat/stream` | GET | Stream response (SSE) |
| `/api/search` | POST | Search documents |
| `/health/live` | GET | Liveness probe |
| `/health/ready` | GET | Readiness probe |
| `/health/detail` | GET | Full health report |

---

## Advanced Configuration

### Changing AI models

```json
"OpenAI": {
  "ChatModel": "gpt-4o-mini",
  "EmbeddingModel": "text-embedding-3-small"
}
```

| Chat model | Notes |
|------------|-------|
| `gpt-4o` | Default — best quality |
| `gpt-4o-mini` | Lower cost, slightly reduced quality |

> Changing `EmbeddingModel` requires re-ingesting all documents if the new model has a different vector dimension.

### Logging verbosity

```json
"Logging": {
  "LogLevel": {
    "Default": "Debug",
    "Microsoft.AspNetCore": "Information"
  }
}
```

### Graceful shutdown timeout

`HostOptions:ShutdownTimeout` (default `00:00:30`) controls how long the app waits for in-flight requests before stopping. Increase for environments with long-running chat streams:

```json
"HostOptions": {
  "ShutdownTimeout": "00:01:00"
}
```

---

## Production Considerations

### Override configuration via environment variables

ASP.NET Core supports overriding any config key via environment variables using `__` (double underscore) as a section separator:

```bash
export OpenAI__ApiKey="sk-prod-..."
export Qdrant__Host="qdrant.internal"
export OpenAI__ChatModel="gpt-4o-mini"
```

This is the recommended approach for container and CI/CD deployments — no secrets touch the filesystem.

### Securing Qdrant with an API key

Add to `appsettings.json` (or as an environment variable) when using a hosted Qdrant instance:

```json
"Qdrant": {
  "ApiKey": "your-qdrant-api-key"
}
```

### CORS for production deployments

Update the allowed origin in `src/PolicyRAG.Api/Program.cs`:

```csharp
policy.WithOrigins("https://your-production-domain.com")
```

Also tighten `AllowedHosts` in `appsettings.json`:

```json
"AllowedHosts": "your-production-domain.com"
```

### Secrets management

| Approach | When to use |
|----------|-------------|
| Environment variables | Containers, VMs, CI/CD pipelines |
| [Azure Key Vault](https://learn.microsoft.com/en-us/aspnet/core/security/key-vault-configuration) | Azure-hosted deployments |
| [AWS Secrets Manager](https://aws.amazon.com/secrets-manager/) | AWS-hosted deployments |
| .NET User Secrets | Local development only — never production |

### Building the frontend for production

```bash
cd policy-rag-ui
npm run build
```

Output is written to `policy-rag-ui/dist/`. Serve the `dist/` directory from any static file host or CDN. The app expects the .NET API to be accessible at `/api` relative to the same host (configured in `environment.prod.ts`).

---

## License

MIT
