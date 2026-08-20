# SignalMiner

SignalMiner is a compliant lead discovery and enrichment platform for finding potential Zextri users from public, reviewable signals.

## Guardrails

- No LinkedIn scraping. SignalMiner can store a public LinkedIn URL that a human enters or that appears on an allowed public website, but it does not crawl LinkedIn.
- X discovery only uses public profile pages and public search-result pages.
- No automated messaging. The CRM stores notes, statuses, and reusable templates for manual outreach only.
- No credential, cookie, or browser-session handling.
- No CAPTCHA bypass, proxy, anti-detection logic, or rate-limit evasion.
- Manual-review-first workflow. Newly discovered leads are marked `NeedsManualReview`.

## Projects

- `src/SignalMiner.Api`: ASP.NET Core Web API with lead discovery, enrichment, search, outreach status, and Hangfire dashboard endpoints.
- `src/SignalMiner.Worker`: Hangfire worker host.
- `src/SignalMiner.Domain`: Lead, Company, OutreachEvent, SourceProfile, and WebsiteSnapshot entities.
- `src/SignalMiner.Application`: workflows, scoring, and ports.
- `src/SignalMiner.Infrastructure`: PostgreSQL EF Core persistence, GitHub discovery, Playwright + AngleSharp website extraction, Hangfire jobs.
- `ui/signalminer-dashboard`: Angular dashboard for manual lead review and outreach tracking.

## API Endpoints

- `POST /api/leads/discover`: discover GitHub or X leads immediately. Omit `source` to use GitHub.
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

## Manual Email Sending

SignalMiner can send one manually reviewed email at a time from the lead detail panel. It does not send bulk email or automated campaigns.

Configure SMTP through `Email:Smtp` settings or environment variables before using **Send email**:

```powershell
$env:Email__Smtp__Host='smtp.titan.email'
$env:Email__Smtp__Port='465'
$env:Email__Smtp__EnableSsl='true'
$env:Email__Smtp__FromEmail='you@yourdomain.com'
$env:Email__Smtp__FromName='Zextri'
$env:Email__Smtp__Username='you@yourdomain.com'
$env:Email__Smtp__Password='your-app-password-or-mailbox-password'
```

Titan may require third-party email access to be enabled. If two-factor authentication is enabled, use a Titan application password.

## Scoring Signals

SignalMiner scores leads from public data using founder/operator keywords, SaaS keywords, AI keywords, public LinkedIn/X URL presence, GitHub posting/repository activity, and public website quality.

## X Discovery Example

```json
{
  "source": "X",
  "query": "founder AI",
  "limit": 25
}
```

X discovery is limited to public HTML and public search-result pages. If X or the search-result page blocks anonymous HTML access, SignalMiner fails gracefully and does not attempt login, session reuse, CAPTCHA bypass, proxies, or anti-detection behavior.
