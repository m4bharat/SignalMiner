using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using SignalMiner.Application;
using SignalMiner.Domain;

namespace SignalMiner.Infrastructure;

public sealed class LeadImportService(ILeadRepository repository) : ILeadImportService
{
    private const int PreviewLimit = 25;

    private static readonly string[][] RequiredHeaderGroups =
    [
        ["First Name"],
        ["Last Name"]
    ];

    public async Task<LeadImportResult> ImportAsync(Stream file, string fileName, bool commit, CancellationToken cancellationToken)
    {
        var rows = await ParseRowsAsync(file, fileName, cancellationToken);
        var issues = new List<LeadImportIssue>();
        var candidates = new List<ImportCandidate>();
        var headers = rows.Headers;

        foreach (var requiredHeaderGroup in RequiredHeaderGroups)
        {
            if (!requiredHeaderGroup.Any(header => headers.Contains(header, StringComparer.OrdinalIgnoreCase)))
            {
                issues.Add(new LeadImportIssue(1, string.Join(" or ", requiredHeaderGroup), "Required column is missing."));
            }
        }

        if (issues.Count > 0)
        {
            return new LeadImportResult(rows.Items.Count, 0, 0, rows.Items.Count, issues, []);
        }

        foreach (var row in rows.Items)
        {
            var candidate = MapCandidate(row, issues);
            if (candidate is not null)
            {
                candidates.Add(candidate);
            }
        }

        var existingKeys = await repository.FindExistingImportKeysAsync(
            candidates.Select(x => x.NormalizedEmail).OfType<string>(),
            candidates.Select(x => x.NormalizedLinkedInUrl).OfType<string>(),
            cancellationToken);
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var leadsToImport = new List<Lead>();
        var previewRows = new List<LeadImportPreviewRow>();

        foreach (var candidate in candidates)
        {
            var keys = candidate.ImportKeys;
            var duplicateReason = keys.FirstOrDefault(existingKeys.Contains) is { } existingKey
                ? BuildDuplicateReason(existingKey)
                : keys.FirstOrDefault(key => !seenKeys.Add(key)) is { } batchKey
                    ? $"Duplicate in this file by {BuildDuplicateReason(batchKey).ToLowerInvariant()}"
                    : null;
            var isDuplicate = duplicateReason is not null;

            if (!isDuplicate)
            {
                leadsToImport.Add(candidate.ToLead());
            }

            if (previewRows.Count < PreviewLimit)
            {
                previewRows.Add(candidate.ToPreviewRow(isDuplicate, duplicateReason));
            }
        }

        if (commit && leadsToImport.Count > 0)
        {
            await repository.AddRangeAsync(leadsToImport, cancellationToken);
            await repository.SaveChangesAsync(cancellationToken);
        }

        return new LeadImportResult(
            rows.Items.Count,
            candidates.Count,
            commit ? leadsToImport.Count : 0,
            rows.Items.Count - candidates.Count + candidates.Count - leadsToImport.Count,
            issues,
            previewRows);
    }

    private static async Task<ParsedRows> ParseRowsAsync(Stream file, string fileName, CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".csv" => await ParseCsvAsync(file, cancellationToken),
            ".xlsx" => ParseXlsx(file),
            _ => throw new InvalidOperationException("Only CSV and XLSX files are supported.")
        };
    }

    private static async Task<ParsedRows> ParseCsvAsync(Stream file, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(file, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var content = await reader.ReadToEndAsync(cancellationToken);
        var delimiter = DetectDelimiter(content);
        var records = ParseDelimitedRecords(content, delimiter);
        return RecordsToRows(records);
    }

    private static ParsedRows ParseXlsx(Stream file)
    {
        using var archive = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: true);
        var sharedStrings = ReadSharedStrings(archive);
        var sheetEntry = archive.GetEntry("xl/worksheets/sheet1.xml")
            ?? throw new InvalidOperationException("The workbook must contain a first worksheet.");

        using var sheetStream = sheetEntry.Open();
        var document = XDocument.Load(sheetStream);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var records = document.Descendants(ns + "row")
            .Select(row => ReadXlsxRow(row, sharedStrings, ns))
            .Where(values => values.Any(value => !string.IsNullOrWhiteSpace(value)))
            .ToList();

        return RecordsToRows(records);
    }

    private static string[] ReadXlsxRow(XElement row, string[] sharedStrings, XNamespace ns)
    {
        var values = new List<string>();
        var nextColumn = 1;
        foreach (var cell in row.Elements(ns + "c"))
        {
            var reference = cell.Attribute("r")?.Value;
            var column = GetColumnIndex(reference) ?? nextColumn;
            while (nextColumn < column)
            {
                values.Add(string.Empty);
                nextColumn++;
            }

            values.Add(ReadCellValue(cell, sharedStrings, ns));
            nextColumn = column + 1;
        }

        return values.ToArray();
    }

    private static string[] ReadSharedStrings(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null)
        {
            return [];
        }

        using var stream = entry.Open();
        var document = XDocument.Load(stream);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        return document.Descendants(ns + "si")
            .Select(item => string.Concat(item.Descendants(ns + "t").Select(text => text.Value)))
            .ToArray();
    }

    private static string ReadCellValue(XElement cell, string[] sharedStrings, XNamespace ns)
    {
        var rawValue = cell.Element(ns + "v")?.Value ?? string.Empty;
        var type = cell.Attribute("t")?.Value;
        if (type == "s" && int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
        {
            return index >= 0 && index < sharedStrings.Length ? sharedStrings[index] : string.Empty;
        }

        if (type == "inlineStr")
        {
            return string.Concat(cell.Descendants(ns + "t").Select(text => text.Value));
        }

        return rawValue;
    }

    private static int? GetColumnIndex(string? cellReference)
    {
        if (string.IsNullOrWhiteSpace(cellReference))
        {
            return null;
        }

        var column = 0;
        foreach (var current in cellReference)
        {
            if (!char.IsLetter(current))
            {
                break;
            }

            column = column * 26 + char.ToUpperInvariant(current) - 'A' + 1;
        }

        return column == 0 ? null : column;
    }

    private static ParsedRows RecordsToRows(IReadOnlyList<string[]> records)
    {
        if (records.Count == 0)
        {
            return new ParsedRows([], []);
        }

        var headers = records[0].Select(header => header.Trim()).ToArray();
        var rows = records.Skip(1)
            .Select((values, index) => new ImportRow(index + 2, headers, values))
            .Where(row => row.HasValues)
            .ToList();
        return new ParsedRows(headers, rows);
    }

    private static char DetectDelimiter(string content)
    {
        var firstLine = content.Split(["\r\n", "\n", "\r"], StringSplitOptions.None).FirstOrDefault() ?? string.Empty;
        return firstLine.Count(character => character == '\t') > firstLine.Count(character => character == ',')
            ? '\t'
            : ',';
    }

    private static List<string[]> ParseDelimitedRecords(string content, char delimiter)
    {
        var records = new List<string[]>();
        var fields = new List<string>();
        var value = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < content.Length; i++)
        {
            var current = content[i];
            if (current == '"')
            {
                if (inQuotes && i + 1 < content.Length && content[i + 1] == '"')
                {
                    value.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (current == delimiter && !inQuotes)
            {
                fields.Add(value.ToString());
                value.Clear();
            }
            else if ((current == '\r' || current == '\n') && !inQuotes)
            {
                if (current == '\r' && i + 1 < content.Length && content[i + 1] == '\n')
                {
                    i++;
                }

                fields.Add(value.ToString());
                value.Clear();
                records.Add(fields.ToArray());
                fields.Clear();
            }
            else
            {
                value.Append(current);
            }
        }

        if (value.Length > 0 || fields.Count > 0)
        {
            fields.Add(value.ToString());
            records.Add(fields.ToArray());
        }

        return records;
    }

    private static ImportCandidate? MapCandidate(ImportRow row, List<LeadImportIssue> issues)
    {
        var firstName = row.Get("First Name");
        var lastName = row.Get("Last Name");
        var displayName = string.Join(' ', new[] { firstName, lastName }.Where(value => !string.IsNullOrWhiteSpace(value))).Trim();
        var email = row.GetAny("Professional Email", "Email");
        var linkedInValue = row.GetAny("LinkedIn Profile", "LinkedIn");
        var linkedInUrl = NormalizeLinkedInUrl(linkedInValue);
        var score = ParseScore(row.Get("Zextri Fit Score"));
        var companyDomain = NormalizeDomain(row.Get("Company Domain"));

        if (string.IsNullOrWhiteSpace(displayName))
        {
            issues.Add(new LeadImportIssue(row.RowNumber, "First Name / Last Name", "A contact name is required."));
        }

        if (!string.IsNullOrWhiteSpace(email) && !email.Contains('@', StringComparison.Ordinal))
        {
            issues.Add(new LeadImportIssue(row.RowNumber, "Professional Email / Email", "A valid professional email is required."));
        }

        if (!string.IsNullOrWhiteSpace(linkedInValue) && linkedInUrl is null)
        {
            issues.Add(new LeadImportIssue(row.RowNumber, "LinkedIn Profile / LinkedIn", "LinkedIn URL must start with linkedin.com or a LinkedIn URL."));
        }

        if (string.IsNullOrWhiteSpace(email) && linkedInUrl is null)
        {
            issues.Add(new LeadImportIssue(row.RowNumber, "Professional Email / Email or LinkedIn", "An email or LinkedIn URL is required."));
        }

        if (string.IsNullOrWhiteSpace(displayName) ||
            (!string.IsNullOrWhiteSpace(email) && !email.Contains('@', StringComparison.Ordinal)) ||
            (string.IsNullOrWhiteSpace(email) && linkedInUrl is null))
        {
            return null;
        }

        return new ImportCandidate(
            row.RowNumber,
            displayName,
            row.GetAny("Title", "Job Title"),
            row.GetAny("Company", "Company Name"),
            companyDomain,
            row.Get("Country"),
            string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant(),
            linkedInUrl,
            row.Get("Segment"),
            Math.Clamp(score, 0, 100),
            row.GetAny("Verification Note", "Fit Reason"),
            ParseContactStatus(row.Get("Outreach Status")),
            row.Get("Priority"));
    }

    private static int ParseScore(string? value)
    {
        if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var score))
        {
            return 0;
        }

        if (score is > 0 and <= 10)
        {
            score *= 10;
        }

        return (int)Math.Round(score, MidpointRounding.AwayFromZero);
    }

    private static ContactStatus ParseContactStatus(string? value)
    {
        var normalized = (value ?? string.Empty).Replace(" ", string.Empty, StringComparison.OrdinalIgnoreCase);
        return Enum.TryParse<ContactStatus>(normalized, ignoreCase: true, out var status)
            ? status
            : ContactStatus.NotContacted;
    }

    private static string? NormalizeLinkedInUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (!trimmed.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = $"https://{trimmed}";
        }

        return Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) &&
            uri.Host.Contains("linkedin.com", StringComparison.OrdinalIgnoreCase)
            ? uri.ToString()
            : null;
    }

    private static string? NormalizeDomain(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        trimmed = trimmed.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? trimmed : $"https://{trimmed}";
        return Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ? uri.Host : null;
    }

    private static string BuildDuplicateReason(string key) =>
        key.StartsWith("email:", StringComparison.OrdinalIgnoreCase)
            ? "Existing lead with same email"
            : "Existing lead with same LinkedIn URL";

    private sealed record ParsedRows(string[] Headers, IReadOnlyList<ImportRow> Items);

    private sealed class ImportRow(int rowNumber, string[] headers, string[] values)
    {
        public int RowNumber { get; } = rowNumber;

        public bool HasValues => values.Any(value => !string.IsNullOrWhiteSpace(value));

        public string? Get(string header)
        {
            var index = Array.FindIndex(headers, item => item.Equals(header, StringComparison.OrdinalIgnoreCase));
            return index >= 0 && index < values.Length ? values[index].Trim() : null;
        }

        public string? GetAny(params string[] candidates) =>
            candidates.Select(Get).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    private sealed record ImportCandidate(
        int RowNumber,
        string DisplayName,
        string? RoleTitle,
        string? CompanyName,
        string? CompanyDomain,
        string? Country,
        string? NormalizedEmail,
        string? LinkedInUrl,
        string? Segment,
        int FitScore,
        string? VerificationNote,
        ContactStatus ContactStatus,
        string? Priority)
    {
        public string? NormalizedLinkedInUrl => LinkedInUrl?.Trim().ToLowerInvariant();

        public string[] ImportKeys =>
            new[] { NormalizedEmail is null ? null : $"email:{NormalizedEmail}", NormalizedLinkedInUrl is null ? null : $"linkedin:{NormalizedLinkedInUrl}" }
                .OfType<string>()
                .ToArray();

        public Lead ToLead()
        {
            var notes = new[]
            {
                string.IsNullOrWhiteSpace(Priority) ? null : $"Import priority: {Priority}",
                string.IsNullOrWhiteSpace(CompanyDomain) ? null : $"Company domain: {CompanyDomain}",
                string.IsNullOrWhiteSpace(Country) ? null : $"Country: {Country}",
                string.IsNullOrWhiteSpace(Segment) ? null : $"Segment: {Segment}",
                VerificationNote,
                "Imported from curated launch contacts file. LinkedIn URL is stored only; SignalMiner does not scrape LinkedIn."
            }.Where(value => !string.IsNullOrWhiteSpace(value));

            var sourceProfiles = new List<SourceProfile>();
            if (!string.IsNullOrWhiteSpace(LinkedInUrl))
            {
                sourceProfiles.Add(new SourceProfile
                {
                    Kind = SourceKind.LinkedInProfileUrlOnly,
                    Url = LinkedInUrl,
                    PublicHandle = DisplayName,
                    Bio = "Imported public LinkedIn URL; not scraped."
                });
            }

            return new Lead
            {
                DisplayName = DisplayName,
                RoleTitle = RoleTitle,
                PublicEmail = NormalizedEmail,
                WebsiteUrl = string.IsNullOrWhiteSpace(CompanyDomain) ? null : $"https://{CompanyDomain}",
                LinkedInUrl = LinkedInUrl,
                FitScore = FitScore,
                ScoreRationale = string.IsNullOrWhiteSpace(VerificationNote)
                    ? "Imported Zextri fit score from curated launch contacts file."
                    : VerificationNote,
                Status = LeadStatus.NeedsManualReview,
                ContactStatus = ContactStatus,
                Notes = string.Join(Environment.NewLine, notes),
                Company = string.IsNullOrWhiteSpace(CompanyName)
                    ? null
                    : new Company
                    {
                        Name = CompanyName,
                        Domain = CompanyDomain,
                        Summary = Segment,
                        Keywords = SplitSegment(Segment)
                    },
                SourceProfiles = sourceProfiles
            };
        }

        public LeadImportPreviewRow ToPreviewRow(bool isDuplicate, string? duplicateReason) =>
            new(RowNumber, DisplayName, RoleTitle, CompanyName, NormalizedEmail, LinkedInUrl, Segment, FitScore, ContactStatus, isDuplicate, duplicateReason);

        private static string[] SplitSegment(string? segment) =>
            string.IsNullOrWhiteSpace(segment)
                ? []
                : segment.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
