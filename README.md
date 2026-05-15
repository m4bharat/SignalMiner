# SignalMiner

SignalMiner is a compliant lead discovery and enrichment platform for finding potential Zextri users from public, reviewable signals.

## Guardrails

- No LinkedIn scraping. SignalMiner can store a public LinkedIn URL that a human enters or that appears on an allowed public website, but it does not crawl LinkedIn.
- No automated messaging. The CRM stores notes, statuses, and reusable templates for manual outreach only.
- No credential or browser-session handling.
- No CAPTCHA bypass or evasion.
- Manual-review-first workflow. Newly discovered leads are marked `NeedsManualReview`.

## Projects

- `src/SignalMiner.Api`: ASP.NET Core Web API with lead discovery, enrichment, search, outreach status, and Hangfire dashboard endpoints.
- `src/SignalMiner.Worker`: Hangfire worker host.
- `src/SignalMiner.Domain`: Lead, Company, OutreachEvent, SourceProfile, and WebsiteSnapshot entities.
- `src/SignalMiner.Application`: workflows, scoring, and ports.
- `src/SignalMiner.Infrastructure`: PostgreSQL EF Core persistence, GitHub discovery, Playwright + AngleSharp website extraction, Hangfire jobs.
- `ui/signalminer-dashboard`: Angular dashboard for manual lead review and outreach tracking.

## API Endpoints

- `POST /api/leads/discover`: discover GitHub leads immediately.
- `POST /api/leads/discover/jobs`: enqueue discovery through Hangfire.
- `POST /api/leads/{id}/enrich`: enrich one lead from public website data.
- `PATCH /api/leads/{id}/outreach-status`: update manual outreach status.
- `POST /api/leads/{id}/outreach-events`: add a manual note or outreach event.
- `GET /api/leads/search`: search and filter leads.
- `GET /api/compliance`: returns active compliance guardrails.
- `GET /jobs`: Hangfire dashboard.

## Local Run

1. Start a local PostgreSQL server without Docker and create a database named `signalminer`.

   ```powershell
   createdb -U postgres signalminer
   ```

   The default connection string is:

   ```text
   Host=localhost;Port=5432;Database=signalminer;Username=postgres;Password=postgres
   ```

   Update `src/SignalMiner.Api/appsettings.json` and `src/SignalMiner.Worker/appsettings.json` if your local PostgreSQL credentials differ.

2. Restore and build:

   ```powershell
   $env:DOTNET_CLI_HOME='C:\JaiMataDi\SIL\SignalMiner\.dotnet'
   dotnet restore
   dotnet build
   ```

3. Run the API:

   ```powershell
   dotnet run --project src\SignalMiner.Api --urls http://localhost:5000
   ```

4. Run the worker in a second terminal:

   ```powershell
   dotnet run --project src\SignalMiner.Worker
   ```

5. Run the Angular dashboard:

   ```powershell
   cd ui\signalminer-dashboard
   npm install
   npm start
   ```

The dashboard expects the API at `http://localhost:5000`. Docker is not required for SignalMiner.

## Scoring Signals

SignalMiner scores leads from public data using founder/operator keywords, SaaS keywords, AI keywords, public LinkedIn/X URL presence, GitHub posting/repository activity, and public website quality.
