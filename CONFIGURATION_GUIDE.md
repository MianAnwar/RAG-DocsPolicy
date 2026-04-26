# PolicyRAG — Configuration Guide

This guide walks through every configuration step required to run PolicyRAG on a new system — from prerequisites and infrastructure setup through backend and frontend configuration, verification, and production considerations.

---

## Table of Contents

1. [Prerequisites](#1-prerequisites)
2. [Infrastructure — Qdrant](#2-infrastructure--qdrant)
3. [Backend Configuration](#3-backend-configuration)
   - 3.1 [OpenAI Settings](#31-openai-settings)
   - 3.2 [Qdrant Settings](#32-qdrant-settings)
   - 3.3 [Chunking Settings](#33-chunking-settings)
   - 3.4 [Retrieval Settings](#34-retrieval-settings)
   - 3.5 [Resilience Settings](#35-resilience-settings)
   - 3.6 [Securing the API Key with User Secrets](#36-securing-the-api-key-with-user-secrets)
   - 3.7 [Running the API](#37-running-the-api)
4. [Frontend Configuration](#4-frontend-configuration)
   - 4.1 [Environment Files](#41-environment-files)
   - 4.2 [Dev Proxy Configuration](#42-dev-proxy-configuration)
   - 4.3 [Running the Frontend](#43-running-the-frontend)
5. [Verifying the Setup](#5-verifying-the-setup)
6. [Advanced Configuration](#6-advanced-configuration)
7. [Production Considerations](#7-production-considerations)

---

## 1. Prerequisites

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

## 2. Infrastructure — Qdrant

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

## 3. Backend Configuration

All backend settings live in `src/PolicyRAG.Api/appsettings.json` (production defaults) and `src/PolicyRAG.Api/appsettings.Development.json` (development overrides). The ASP.NET Core configuration system merges these files, with the environment-specific file taking precedence.

The sections below document every configurable property.

---

### 3.1 OpenAI Settings

**Configuration section:** `OpenAI`

| Property | Type | Default | Required | Description |
|----------|------|---------|----------|-------------|
| `ApiKey` | string | _(none)_ | **Yes** | OpenAI secret key — see [Section 3.6](#36-securing-the-api-key-with-user-secrets) |
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

### 3.2 Qdrant Settings

**Configuration section:** `Qdrant`

| Property | Type | Default (prod) | Default (dev) | Description |
|----------|------|----------------|---------------|-------------|
| `Host` | string | `localhost` | `localhost` | Hostname of the Qdrant server |
| `Port` | int | `6334` | `6334` | gRPC port |
| `CollectionName` | string | `policy_documents` | `policy_documents_dev` | Name of the Qdrant collection that stores vectors |
| `VectorSize` | ulong | `1536` | `1536` | Embedding vector dimension — must match the chosen `OpenAI:EmbeddingModel` |
| `ApiKey` | string | _(none)_ | _(none)_ | Optional — required only if Qdrant is configured with authentication |

Example:

```json
"Qdrant": {
  "Host": "localhost",
  "Port": 6334,
  "CollectionName": "policy_documents_dev",
  "VectorSize": 1536
}
```

> The Qdrant collection is created automatically on first run if it does not exist. Separate collection names for `Development` and `Production` (the default) prevent dev ingestion from polluting the production index.

---

### 3.3 Chunking Settings

**Configuration section:** `Chunking`

Controls how uploaded documents are split into chunks before embedding.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `MaxChunkSize` | int | `512` | Maximum number of tokens per chunk |
| `ChunkOverlap` | int | `50` | Number of tokens shared between adjacent chunks — preserves context across boundaries |
| `MinChunkSize` | int | `100` | Chunks smaller than this are discarded |
| `Separators` | string[] | `["\n\n", "\n", ". ", " "]` | Ordered list of text separators tried when splitting — earlier separators are preferred |

Example:

```json
"Chunking": {
  "MaxChunkSize": 512,
  "ChunkOverlap": 50,
  "MinChunkSize": 100
}
```

**Tuning guidance:**

- Increase `MaxChunkSize` (e.g. `1024`) for documents with long paragraphs where more context per chunk improves answer quality — at the cost of more tokens per embedding call.
- Increase `ChunkOverlap` (e.g. `100`) if answers are truncated or miss context that spans a chunk boundary.
- Decrease `MinChunkSize` if short sections (e.g. headers, lists) are being dropped.

---

### 3.4 Retrieval Settings

**Configuration section:** `Retrieval`

Controls how the vector search selects context chunks for the LLM.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `DefaultTopK` | int | `5` | Number of chunks retrieved per query |
| `ScoreThreshold` | float | `0.7` | Minimum cosine similarity score — chunks scoring below this are excluded |
| `MmrDiversity` | float | `0.3` | Maximum Marginal Relevance diversity factor (0 = most similar, 1 = most diverse) |

Example:

```json
"Retrieval": {
  "DefaultTopK": 5,
  "ScoreThreshold": 0.7,
  "MmrDiversity": 0.3
}
```

**Tuning guidance:**

- Increase `DefaultTopK` (e.g. `10`) if answers are missing relevant information from the document corpus.
- Lower `ScoreThreshold` (e.g. `0.5`) if retrieval returns too few results; raise it (e.g. `0.85`) to reduce noise.
- Raise `MmrDiversity` (e.g. `0.5`) if retrieved chunks are repetitive.

---

### 3.5 Resilience Settings

**Configuration section:** `Resilience`

The backend uses [Polly](https://github.com/App-vNext/Polly) resilience pipelines for calls to both OpenAI and Qdrant. There is a base set of defaults and two service-specific overrides.

#### Base settings (`Resilience`)

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `MaxRetryAttempts` | int | `3` | Number of retry attempts before failing |
| `BaseDelayMs` | int | `1000` | Initial exponential backoff delay (ms) |
| `MaxDelayMs` | int | `30000` | Maximum backoff delay cap (ms) |
| `TimeoutSeconds` | int | `30` | Per-operation timeout |
| `CircuitBreakerFailureThreshold` | int | `5` | Failures before the circuit breaker opens |
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

### 3.6 Securing the API Key with User Secrets

> **Warning:** The file `src/PolicyRAG.Api/appsettings.Development.json` in this repository contains a hardcoded `OpenAI:ApiKey`. **Do not commit a real API key to source control.** The recommended approach for local development is [.NET User Secrets](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets).

#### Set up User Secrets

```bash
# From the repo root
cd src/PolicyRAG.Api

# Initialize user secrets for this project (only needed once)
dotnet user-secrets init

# Store your OpenAI API key
dotnet user-secrets set "OpenAI:ApiKey" "sk-your-key-here"
```

User Secrets are stored outside the repository at `~/.microsoft/usersecrets/<project-id>/secrets.json` and are never committed to git.

#### Remove the hardcoded key

After setting the User Secret, clear the `ApiKey` value from `appsettings.Development.json`:

```json
"OpenAI": {
  "ApiKey": "",
  "EmbeddingModel": "text-embedding-3-small",
  "ChatModel": "gpt-4o"
}
```

User Secrets override `appsettings.Development.json` automatically in the `Development` environment.

#### Verify

```bash
dotnet user-secrets list
# OpenAI:ApiKey = sk-...
```

---

### 3.7 Running the API

```bash
cd src/PolicyRAG.Api
dotnet run
```

By default this uses the `http` launch profile, which sets `ASPNETCORE_ENVIRONMENT=Development` and starts the server at:

| URL | Profile |
|-----|---------|
| `http://localhost:5058` | `http` (default) |
| `https://localhost:7040` + `http://localhost:5058` | `https` |

To use the HTTPS profile:

```bash
dotnet run --launch-profile https
```

> If this is your first time using HTTPS locally, you may need to trust the development certificate:
> ```bash
> dotnet dev-certs https --trust
> ```

The API exposes a Swagger UI at `http://localhost:5058/swagger` in the Development environment.

---

## 4. Frontend Configuration

### 4.1 Environment Files

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

In production, `apiUrl: '/api'` means all API calls are made relative to the host serving the frontend (the backend must serve or proxy the `/api` path). In development, calls go directly to the .NET API at `http://localhost:5058`.

If you change the backend port in `launchSettings.json`, update `apiUrl` in `environment.ts` to match.

---

### 4.2 Dev Proxy Configuration

`policy-rag-ui/proxy.conf.json` defines a dev-server proxy used by `ng serve`:

```json
{
  "/api": {
    "target": "https://localhost:5001",
    "secure": false,
    "changeOrigin": true,
    "logLevel": "debug"
  }
}
```

> **Known discrepancy:** The proxy target (`https://localhost:5001`) does not match the actual backend ports defined in `launchSettings.json` (`http://localhost:5058` / `https://localhost:7040`). Because `environment.ts` already points directly to `http://localhost:5058`, the Angular app currently bypasses this proxy in development and the proxy is not actively used. If you intend to use the proxy (e.g. to test production-style `/api` routing), update the target to match your actual backend URL:

```json
{
  "/api": {
    "target": "http://localhost:5058",
    "secure": false,
    "changeOrigin": true
  }
}
```

---

### 4.3 Running the Frontend

```bash
cd policy-rag-ui

# Install dependencies (first time or after pulling changes)
npm install

# Start the development server
npm start
```

The app will be available at `http://localhost:4200`.

> The backend CORS policy allows requests from `http://localhost:4200` by default. If you change the Angular dev server port, update the CORS origin in `src/PolicyRAG.Api/Program.cs` accordingly.

---

## 5. Verifying the Setup

With Qdrant running, the API running, and the frontend running, confirm everything is wired up correctly.

### Backend health endpoints

| Endpoint | What it checks | Expected status |
|----------|---------------|-----------------|
| `GET /health/live` | API process is alive | `Healthy` |
| `GET /health/startup` | Startup initialization completed | `Healthy` |
| `GET /health/ready` | Qdrant and OpenAI reachable | `Healthy` |
| `GET /health/detail` | All checks with full detail | JSON report |

```bash
# Quick liveness check
curl http://localhost:5058/health/live

# Full detail (pretty-print with jq)
curl http://localhost:5058/health/detail | jq
```

Example `health/detail` output when all services are healthy:

```json
{
  "status": "Healthy",
  "results": {
    "self":                { "status": "Healthy" },
    "startup":             { "status": "Healthy" },
    "qdrant":              { "status": "Healthy", "description": "Collection 'policy_documents_dev' has 0 points" },
    "openai":              { "status": "Healthy", "description": "OpenAI API is accessible" },
    "document-repository": { "status": "Healthy", "description": "0 documents in repository" }
  }
}
```

> The `openai` check is reported as `Degraded` (not `Unhealthy`) if the OpenAI API is unreachable — the app will still start, but chat and embedding calls will fail at runtime.

### End-to-end smoke test

1. Open `http://localhost:4200` in a browser.
2. Navigate to **Documents → Upload**.
3. Upload a PDF or DOCX file. The document should appear in the document list.
4. Navigate to **Chat**.
5. Ask a question about the uploaded document. A response with source citations should appear.

---

## 6. Advanced Configuration

### Changing AI models

To use a cheaper or more capable model, update `appsettings.json` (or your User Secrets):

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

### Adjusting file upload size

The maximum upload size is configured in `Program.cs`:

```csharp
options.MultipartBodyLengthLimit = 52428800; // 50 MB
```

This is not currently exposed as a settings key. To change it, edit `Program.cs` directly.

### Logging verbosity

Increase logging detail for troubleshooting by editing the `Logging` section:

```json
"Logging": {
  "LogLevel": {
    "Default": "Debug",
    "Microsoft.AspNetCore": "Information",
    "Qdrant.Client": "Debug"
  }
}
```

### Graceful shutdown timeout

`HostOptions:ShutdownTimeout` (default `00:00:30`) controls how long the application waits for in-flight requests to complete before forcefully stopping. Increase this in environments with long-running chat streams:

```json
"HostOptions": {
  "ShutdownTimeout": "00:01:00"
}
```

---

## 7. Production Considerations

### Override configuration via environment variables

ASP.NET Core supports overriding any configuration key via environment variables using `__` (double underscore) as a section separator:

```bash
# Override OpenAI API key without touching appsettings.json
export OpenAI__ApiKey="sk-prod-..."

# Override Qdrant host
export Qdrant__Host="qdrant.internal"
export Qdrant__Port="6334"

# Override chat model
export OpenAI__ChatModel="gpt-4o-mini"
```

This is the recommended approach for container and CI/CD deployments — no secrets ever touch the filesystem.

### Securing Qdrant with an API key

If your Qdrant instance is not running on localhost (e.g. a hosted or cloud-deployed instance), enable API key authentication:

**In Qdrant's configuration:**

```yaml
service:
  api_key: your-qdrant-api-key
```

**In `appsettings.json` (or as an environment variable):**

```json
"Qdrant": {
  "ApiKey": "your-qdrant-api-key"
}
```

### CORS for production deployments

The backend currently allows CORS only from `http://localhost:4200`. For production, update the allowed origin in `src/PolicyRAG.Api/Program.cs`:

```csharp
policy.WithOrigins("https://your-production-domain.com")
```

Consider also setting `AllowedHosts` in `appsettings.json` instead of the current wildcard `"*"`:

```json
"AllowedHosts": "your-production-domain.com"
```

### Secrets management in production

| Approach | When to use |
|----------|-------------|
| Environment variables | Containers, VMs, CI/CD pipelines |
| [Azure Key Vault](https://learn.microsoft.com/en-us/aspnet/core/security/key-vault-configuration) | Azure-hosted deployments |
| [AWS Secrets Manager](https://aws.amazon.com/secrets-manager/) | AWS-hosted deployments |
| .NET User Secrets | Local development only — never production |

Never store secrets in `appsettings.json` or `appsettings.Development.json` committed to source control.

### Deploying the frontend for production

```bash
cd policy-rag-ui
npm run build
```

Output is written to `policy-rag-ui/dist/`. Serve the `dist/` directory from any static file host or CDN. In production, the Angular app expects the .NET API to be accessible at `/api` relative to the same host (configured in `environment.prod.ts`).
