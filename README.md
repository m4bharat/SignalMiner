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

- `POST /api/leads/discover`: discover GitHub, X, or LinkedIn URL-only leads immediately. Omit `source` to use GitHub.
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

   GitHub discovery works without a token at GitHub's unauthenticated rate limit. For higher limits, set a local environment variable before starting the API instead of committing a token:

   ```powershell
   $env:GitHub__Token='github_pat_...'
   ```

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

SignalMiner sends separate, manually reviewed emails from lead detail or **Send selected**. Selected sending requires reviewing every recipient's final subject, opening, body, and template version before confirmation. There is no campaign scheduler or unrestricted bulk-send endpoint.

Configure SMTP through `Email:Smtp` settings or environment variables before using **Send email**:

Keep mailbox passwords in environment variables or an ignored `appsettings.Local.json` beside the API project. Local settings override the checked-in JSON; environment variables and command-line settings take precedence. Local configuration is excluded from published output. Never put real credentials in tracked settings files.

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

## Fresh database and workbook import

Configure the same `ConnectionStrings:SignalMiner` for the API and worker (or `ConnectionStrings__SignalMiner`). Stop older API/worker instances before upgrading, back up the database, then start the updated API followed by the worker. Startup now runs EF Core migrations before Hangfire. `20260906211047_OutreachSafety` adopts the current imported-lead schema without recreating existing tables, preserves leads/events, creates suppression/submission tables, and seeds submission history from existing non-test email events. It also supports an empty database. Incompatible older schemas fail rather than being wiped. Rollbacks that would discard suppression/history are deliberately blocked; use a reviewed forward migration.

For a separately managed deployment, set `ConnectionStrings__SignalMiner` securely and run `dotnet ef database update --project src/SignalMiner.Infrastructure --startup-project src/SignalMiner.Infrastructure` before starting services. Do not use `EnsureCreated` on an existing database.

Upload `Zextri_Cleaned_Prioritized_Leads.xlsx` through **Import contacts** and preview before importing. The importer combines Ready 9plus, Verify Before Send, and Hold or Exclude using their `Outreach Fit /10` headers. Summary, Scoring Rules, and the repeated raw Source Data sheet are excluded. CSV/manual entry uses the same column names; historical header aliases and automatic score rescaling have been removed.

Workbook assessment fields have dedicated columns. Company name/domain are stored in Companies, and each lead references its company. Import metadata is not duplicated in notes, company summaries or company keywords. `IsImported` identifies imported contacts. Workbook scores and rationales are kept separately from enrichment results, and recommended actions are stored as text.

Imports create new leads in NeedsManualReview with the supplied outreach status. Duplicate lead IDs, emails or LinkedIn URLs are skipped, including on a repeat import. Imports do not update existing records. Preview and import report duplicate/skipped rows. Blank and literal `NULL` cells are stored as null. Original Fit Score is an integer from 0–100; Outreach Fit /10 is a decimal from 0–10.

The dashboard displays priority, rank and outreach fit, with full assessment details in the lead panel. Ranked records appear in workbook order; unranked records follow by fit score. Companies, SourceProfiles, WebsiteSnapshots and OutreachEvents remain part of the current schema because discovery, enrichment and outreach use them.

Imports validate mailbox syntax, contact statuses and score precision, and report the source worksheet on row errors. Equivalent LinkedIn URLs, including regional hosts, are normalized for duplicate checks. The app's DoNotContact status blocks actual email delivery; workbook recommendations remain informational text.

## Outreach safety

`Email:Outreach:DailySendLimit` defaults to **50** (valid 1–10000), and `Email:Outreach:DelayBetweenMessagesSeconds` defaults to **10** (valid 1–3600). Environment equivalents are `Email__Outreach__DailySendLimit` and `Email__Outreach__DelayBetweenMessagesSeconds`. Invalid values fail startup. The UI obtains these values and remaining UTC-day capacity from `GET /api/outreach/policy` and fails closed if unavailable.

The backend serializes send admission with a PostgreSQL advisory lock, persists a reservation before SMTP, checks all To/CC/BCC addresses against suppression and DoNotContact, enforces pacing, and returns HTTP 429 with `remainingCapacity: 0` when exhausted. Tests go only to the configured test inbox with CC/BCC removed and do not consume the limit. Pending/uncertain submissions conservatively consume capacity because SMTP acceptance cannot always be determined after a disconnect; do not automatically retry them. Submitted records and outreach history survive browser cancellation. SMTP submission is not proof of delivery.

Under **Delivery safety**, record a hard bounce, complaint, unsubscribe, or manual suppression after confirmation. This permanently blocks the normalized address, marks matching leads DoNotContact, and records history. The original reason/date are retained on repeated submissions. Imports skip suppressed addresses with an explicit issue. Use the searchable **Suppression list** to view records; there is no UI deletion. APIs: `POST /api/outreach/leads/{id}/suppression` with `reason` and optional `details`, and `GET /api/outreach/suppressions?query=...&page=1&pageSize=25`.

Selected sending uses frozen, individualized reviewed requests, without CC/BCC. **Stop sending** interrupts the delay and prevents the next request; an in-flight SMTP message may still complete. The final summary separates submitted, failed/uncertain, suppressed/skipped, and stopped. Network/provider/limit failures stop the remaining run, and no address is automatically retried. Refreshing or closing the page does not resume a run. Existing templates, discovery, imports, and individual sending remain manual workflows.

Titan bounce/complaint/unsubscribe detection is **manual**. This change adds no IMAP polling, mailbox access, provider webhooks, automatic parsing, or campaigns. The app remains intended for a trusted local environment; its existing frontend sign-in is not server authentication.

Run `dotnet test SignalMiner.sln`. For the PostgreSQL safety integration test, set `SIGNALMINER_TEST_DB` to a test connection with permission to create/drop schemas; it uses only a random `safety_test_*` schema and a fake mail sender. Without this variable, that integration test is explicitly skipped. From `ui/signalminer-dashboard`, run `npm run check`, `npm test` (Node 24), and `npm run build`; finish with `git diff --check`.

## Validation before pushing

Run `dotnet test SignalMiner.sln`, then run `npm run check`, `npm test`, `npm run build` and `npm audit` from `ui/signalminer-dashboard`. Check .NET dependencies with `dotnet list SignalMiner.sln package --vulnerable --include-transitive`. The GitHub validation workflow runs builds, tests and dependency audits on pushes and pull requests. The dashboard uses Angular's application builder; the unused webpack build chain has been removed.

## Scoring signals

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
