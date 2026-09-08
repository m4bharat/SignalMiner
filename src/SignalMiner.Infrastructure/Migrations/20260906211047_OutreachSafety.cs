using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable
namespace SignalMiner.Infrastructure.Migrations;
public partial class OutreachSafety : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Adopt the existing EnsureCreated schema without recreating its tables.
        // Missing baseline columns fail the transaction rather than guessing a destructive upgrade.
        migrationBuilder.Sql("""
DO $baseline$
BEGIN
IF to_regclass('"Leads"') IS NULL THEN
CREATE TABLE "Companies" (
    "Id" uuid NOT NULL,
    "Name" text NOT NULL,
    "Domain" text,
    "Summary" text,
    "Keywords" text[] NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_Companies" PRIMARY KEY ("Id")
);
CREATE TABLE "Leads" (
    "Id" uuid NOT NULL,
    "DisplayName" text NOT NULL,
    "FirstName" text,
    "LastName" text,
    "Rank" integer,
    "PriorityGroup" text,
    "OutreachFitScore" numeric(4,2),
    "ZextriSegment" text,
    "IsImported" boolean NOT NULL,
    "CountryUnverified" text,
    "PersonalizationAngle" text,
    "OutreachScoreRationale" text,
    "DataQualityFlags" text,
    "RecommendedAction" text,
    "OriginalFitScore" integer,
    "SourceRow" integer,
    "SourceSheet" text,
    "RoleTitle" text,
    "PublicEmail" text,
    "WebsiteUrl" text,
    "GitHubUrl" text,
    "XUrl" text,
    "LinkedInUrl" text,
    "FitScore" integer NOT NULL,
    "ScoreRationale" character varying(1000) NOT NULL,
    "Status" integer NOT NULL,
    "ContactStatus" integer NOT NULL,
    "Notes" text,
    "CompanyId" uuid,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_Leads" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_Leads_Companies_CompanyId" FOREIGN KEY ("CompanyId") REFERENCES "Companies" ("Id")
);
CREATE TABLE "OutreachEvents" (
    "Id" uuid NOT NULL,
    "LeadId" uuid NOT NULL,
    "Type" integer NOT NULL,
    "Body" text NOT NULL,
    "NewContactStatus" integer,
    "OccurredAt" timestamp with time zone NOT NULL,
    "CreatedBy" text NOT NULL,
    CONSTRAINT "PK_OutreachEvents" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_OutreachEvents_Leads_LeadId" FOREIGN KEY ("LeadId") REFERENCES "Leads" ("Id") ON DELETE CASCADE
);
CREATE TABLE "SourceProfiles" (
    "Id" uuid NOT NULL,
    "LeadId" uuid NOT NULL,
    "Kind" integer NOT NULL,
    "Url" text NOT NULL,
    "PublicHandle" text NOT NULL,
    "Bio" text,
    "PublicActivityCount" integer,
    "CapturedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_SourceProfiles" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_SourceProfiles_Leads_LeadId" FOREIGN KEY ("LeadId") REFERENCES "Leads" ("Id") ON DELETE CASCADE
);
CREATE TABLE "WebsiteSnapshots" (
    "Id" uuid NOT NULL,
    "LeadId" uuid NOT NULL,
    "Url" text NOT NULL,
    "Title" text NOT NULL,
    "Description" text,
    "PublicEmails" text[] NOT NULL,
    "Links" text[] NOT NULL,
    "QualityScore" integer NOT NULL,
    "CapturedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_WebsiteSnapshots" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_WebsiteSnapshots_Leads_LeadId" FOREIGN KEY ("LeadId") REFERENCES "Leads" ("Id") ON DELETE CASCADE
);
CREATE INDEX "IX_Leads_CompanyId" ON "Leads" ("CompanyId");
CREATE INDEX "IX_Leads_ContactStatus" ON "Leads" ("ContactStatus");
CREATE INDEX "IX_Leads_FitScore" ON "Leads" ("FitScore");
CREATE INDEX "IX_OutreachEvents_LeadId" ON "OutreachEvents" ("LeadId");
CREATE INDEX "IX_SourceProfiles_LeadId" ON "SourceProfiles" ("LeadId");
CREATE INDEX "IX_WebsiteSnapshots_LeadId" ON "WebsiteSnapshots" ("LeadId");
ELSE
PERFORM "Id", "Name", "Domain", "Summary", "Keywords", "CreatedAt" FROM "Companies" LIMIT 0;
PERFORM "Id", "DisplayName", "FirstName", "LastName", "Rank", "PriorityGroup", "OutreachFitScore", "ZextriSegment", "IsImported", "CountryUnverified", "PersonalizationAngle", "OutreachScoreRationale", "DataQualityFlags", "RecommendedAction", "OriginalFitScore", "SourceRow", "SourceSheet", "RoleTitle", "PublicEmail", "WebsiteUrl", "GitHubUrl", "XUrl", "LinkedInUrl", "FitScore", "ScoreRationale", "Status", "ContactStatus", "Notes", "CompanyId", "CreatedAt", "UpdatedAt" FROM "Leads" LIMIT 0;
PERFORM "Id", "LeadId", "Type", "Body", "NewContactStatus", "OccurredAt", "CreatedBy" FROM "OutreachEvents" LIMIT 0;
PERFORM "Id", "LeadId", "Kind", "Url", "PublicHandle", "Bio", "PublicActivityCount", "CapturedAt" FROM "SourceProfiles" LIMIT 0;
PERFORM "Id", "LeadId", "Url", "Title", "Description", "PublicEmails", "Links", "QualityScore", "CapturedAt" FROM "WebsiteSnapshots" LIMIT 0;
END IF;
END $baseline$;
CREATE TABLE "EmailSubmissions" (
    "Id" uuid NOT NULL,
    "LeadId" uuid NOT NULL,
    "Recipients" text NOT NULL,
    "StartedAt" timestamp with time zone NOT NULL,
    "SubmittedAt" timestamp with time zone,
    "Status" integer NOT NULL,
    "Details" text,
    CONSTRAINT "PK_EmailSubmissions" PRIMARY KEY ("Id")
);
CREATE TABLE "EmailSuppressions" (
    "NormalizedEmail" character varying(320) NOT NULL,
    "Reason" integer NOT NULL,
    "Details" character varying(2000),
    "CreatedAt" timestamp with time zone NOT NULL,
    "RelatedLeadId" uuid,
    CONSTRAINT "PK_EmailSuppressions" PRIMARY KEY ("NormalizedEmail")
);
CREATE INDEX "IX_EmailSubmissions_StartedAt" ON "EmailSubmissions" ("StartedAt");
ALTER TABLE "EmailSuppressions" ADD CONSTRAINT "CK_EmailSuppression_Normalized"
CHECK ("NormalizedEmail" = lower(btrim("NormalizedEmail")) AND "NormalizedEmail" <> '');
INSERT INTO "EmailSubmissions" ("Id", "LeadId", "Recipients", "StartedAt", "SubmittedAt", "Status", "Details")
SELECT "Id", "LeadId", '', "OccurredAt", "OccurredAt", 1, 'Existing manual email history'
FROM "OutreachEvents" WHERE "Type" = 2;

""");
    }
    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new System.NotSupportedException("Suppression and submission history must be retained. Use a reviewed forward migration.");
}
