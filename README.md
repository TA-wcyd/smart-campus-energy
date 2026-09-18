# Smart Campus Energy Optimization (MyApp)

A .NET 10 microservice and web application for smart grid microgrid scheduling, operator directive interpretation with Google Gemini, and battery dispatch optimization.

---

## Architecture & Technology Stack

- **Framework**: .NET 10 (C# 13, ASP.NET Core)
- **UI**: ASP.NET Core Razor Pages
- **Database**: PostgreSQL / Supabase with Entity Framework Core
- **AI / LLM**: Google Gemini API for operational note interpretation
- **Testing**: xUnit with `WebApplicationFactory` integration tests
- **Containerization**: Docker (Multi-stage build on ASP.NET 10 Alpine/Debian slim runtime)
- **CI/CD**: GitHub Actions
- **Hosting**: Render (Web Service via Docker)

### Key Endpoints

- `GET /health` - Lightweight health check (returns `{ "status": "ok" }`)
- `POST /optimize-energy` - Optimize 24-hour battery schedule and persist scenario/plan
- `POST /optimize-energy/raw` - Compute schedule without database persistence
- `GET /swagger` - Interactive Swagger OpenAPI documentation

---

## Local Development

### 1. Configure Environment Variables
Copy `.env.example` to `.env` and set your credentials:
```bash
cp .env.example .env
```
Ensure the following are set in `.env`:
- `GEMINI_API_KEY`: Your Google Gemini API Key
- `SUPABASE_CONNECTION_STRING` or `DATABASE_URL`: PostgreSQL connection string

*(Note: If no connection string is provided, the application will automatically fall back to an In-Memory database for local development and testing.)*

### 2. Build & Test
```bash
dotnet restore MyApp.sln
dotnet build MyApp.sln
dotnet test MyApp.sln
```

### 3. Run Locally
```bash
dotnet run --project MyApp.Api
```
Open your browser at:
[http://localhost:5000](http://localhost:5000)

---

## Production Deployment to Render

### 1. Create Render Web Service
1. In the [Render Dashboard](https://dashboard.render.com/), click **New +** -> **Web Service**.
2. Connect your GitHub repository.
3. Configure the service settings:
   - **Name**: `smart-campus-energy` (or your preferred name)
   - **Runtime**: `Docker`
   - **Branch**: `main`
   - **Dockerfile Path**: `./Dockerfile`
   - **Docker Context**: `.`
   - **Health Check Path**: `/health`
   - **Auto-Deploy**: `No` (Auto-deployment is triggered securely by GitHub Actions after CI passes)
4. Add the following **Environment Variables** in Render:
   - `GEMINI_API_KEY`: `<your-gemini-api-key>`
   - `SUPABASE_CONNECTION_STRING`: `<your-supabase-connection-string>`

### 2. Configure GitHub Actions Deploy Hook
1. In Render, go to your Web Service **Settings** -> **Deploy Hook**.
2. Copy the generated Deploy Hook URL.
3. In your GitHub repository:
   - Go to **Settings** -> **Secrets and variables** -> **Actions**.
   - Click **New repository secret**.
   - **Name**: `RENDER_DEPLOY_HOOK_URL`
   - **Value**: Paste the Render Deploy Hook URL.

---

## CI/CD Workflow

Continuous Integration and Continuous Deployment are managed automatically by `.github/workflows/ci-cd.yml`.

### Workflow Triggers
- **Pull Requests (to `main`)**: Runs restore, build, xUnit test suite, and validates Docker build. **Does NOT deploy.**
- **Pushes (to `main`)**: Runs full CI validation. If and **only if** all tests and builds pass, triggers the Render Deploy Hook.

```
Git push
   │
   ▼
GitHub Actions
   │
   ▼
dotnet restore
   │
   ▼
dotnet build
   │
   ▼
dotnet test
   │
   ▼
docker build (validation)
   │
   │ (Success)
   ▼
Render Deploy Hook
   │
   ▼
Render
   │
   ▼
Docker build on Render
   │
   ▼
Production Container (0.0.0.0:$PORT)
   │
   ▼
Health Check (/health) -> Live!
```