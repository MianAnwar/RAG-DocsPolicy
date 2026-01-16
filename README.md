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
│       ├── Controllers/         # API endpoints (coming in Phase 8)
│       ├── Services/            # Business logic (coming in Phases 2-7)
│       └── Models/              # Domain models (coming in Phase 3)
├── policy-rag-ui/              # Angular frontend
│   └── src/app/
│       ├── core/               # Services, models
│       ├── features/           # Chat, Documents modules
│       └── shared/             # Shared components
├── docker-compose.yml          # Qdrant container
└── PolicyRAG.sln               # Solution file
```

## Prerequisites

- [.NET 9.0 SDK](https://dotnet.microsoft.com/download)
- [Node.js 20+](https://nodejs.org/)
- [Angular CLI](https://angular.io/cli): `npm install -g @angular/cli`
- [Docker](https://www.docker.com/) (for Qdrant)
- OpenAI API Key or Azure OpenAI credentials

## Quick Start

### 1. Start Qdrant

```bash
docker-compose up -d
```

### 2. Configure API Keys

Edit `src/PolicyRAG.Api/appsettings.Development.json`:

```json
{
  "OpenAI": {
    "ApiKey": "your-openai-api-key"
  }
}
```

Or for Azure OpenAI:

```json
{
  "AzureOpenAI": {
    "Endpoint": "https://your-resource.openai.azure.com",
    "ApiKey": "your-azure-api-key"
  }
}
```

### 3. Run the Backend

```bash
cd src/PolicyRAG.Api
dotnet run
```

API will be available at: `https://localhost:5001`

### 4. Run the Frontend

```bash
cd policy-rag-ui
npm install
ng serve
```

Frontend will be available at: `http://localhost:4200`

## API Endpoints (Coming Soon)

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/documents/upload` | POST | Upload policy documents |
| `/api/documents` | GET | List all documents |
| `/api/documents/{id}` | DELETE | Delete a document |
| `/api/chat` | POST | Ask a question |
| `/api/chat/stream` | GET | Stream response (SSE) |
| `/api/search` | POST | Search documents |

## Configuration

### Qdrant Settings

| Setting | Default | Description |
|---------|---------|-------------|
| `Host` | localhost | Qdrant host |
| `Port` | 6334 | gRPC port |
| `CollectionName` | policy_documents | Vector collection name |
| `VectorSize` | 1536 | Embedding dimensions |

### Chunking Settings

| Setting | Default | Description |
|---------|---------|-------------|
| `MaxChunkSize` | 512 | Max tokens per chunk |
| `ChunkOverlap` | 50 | Overlap between chunks |
| `MinChunkSize` | 100 | Min tokens per chunk |

### Retrieval Settings

| Setting | Default | Description |
|---------|---------|-------------|
| `DefaultTopK` | 5 | Results to return |
| `ScoreThreshold` | 0.7 | Minimum similarity score |
| `MmrDiversity` | 0.3 | Diversity weight for MMR |

## Development Phases

- [x] **Phase 1**: Project scaffolding & infrastructure
- [ ] **Phase 2**: Document ingestion & preprocessing
- [ ] **Phase 3**: Chunking strategy & metadata design
- [ ] **Phase 4**: Embedding generation
- [ ] **Phase 5**: Qdrant vector storage
- [ ] **Phase 6**: Query-time retrieval
- [ ] **Phase 7**: Prompt construction & LLM integration
- [ ] **Phase 8**: Backend API design
- [ ] **Phase 9**: Angular frontend
- [ ] **Phase 10**: Production hardening

## License

MIT
