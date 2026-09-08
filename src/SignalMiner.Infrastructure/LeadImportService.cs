using System.Globalization;
using System.IO.Compression;
using System.Net.Mail;
using System.Text;
using System.Xml.Linq;
using SignalMiner.Application;
using SignalMiner.Domain;

namespace SignalMiner.Infrastructure;

public sealed class LeadImportService(ILeadRepository repository, ISuppressionService suppression) : ILeadImportService
{
    private const int PreviewLimit = 25;

    private static readonly string[][] RequiredHeaderGroups =
    [
        ["First Name", "Display Name"],
        ["Last Name", "Display Name"]
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
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = MapCandidate(row, issues);
            if (candidate is not null)
            {
                candidates.Add(candidate);
            }
        }

        var existingKeys = await repository.FindExistingImportKeysAsync(
            candidates.Select(x => x.Lead.Id),
            candidates.Select(x => x.Lead.PublicEmail).OfType<string>(),
            candidates.Select(x => x.Lead.LinkedInUrl).OfType<string>(),
            cancellationToken);
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var suppressed = await suppression.FindAsync(candidates.Select(x => x.Lead.PublicEmail).OfType<string>(), cancellationToken);
        var leadsToImport = new List<Lead>();
        var previewRows = new List<LeadImportPreviewRow>();

        foreach (var candidate in candidates)
        {
            var keys = candidate.ImportKeys;
            var suppressedAddress = candidate.Lead.PublicEmail is { } email && suppressed.ContainsKey(email.Trim().ToLowerInvariant());
            var duplicateReason = suppressedAddress ? "Suppressed email: import skipped." : keys.FirstOrDefault(existingKeys.Contains) is { } existingKey
                ? BuildDuplicateReason(existingKey)
                : keys.FirstOrDefault(seenKeys.Contains) is { } batchKey
                    ? BuildDuplicateReason(batchKey, existing: false)
                    : null;
            var isDuplicate = duplicateReason is not null;
            if (suppressedAddress)
                issues.Add(new LeadImportIssue(candidate.RowNumber, "Professional Email", "Suppressed email: import skipped."));

            if (!isDuplicate)
            {
                leadsToImport.Add(candidate.Lead);
                foreach (var key in keys) seenKeys.Add(key);
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
            rows.Items.Count - leadsToImport.Count,
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
        // ASP.NET's bounded upload stream cannot seek backwards past the start.
        if (file.CanSeek && file.Length - file.Position < 22)
            throw new InvalidDataException("The XLSX archive is truncated.");
        using var archive = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: true);
        var sharedStrings = ReadSharedStrings(archive);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relationshipNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        using var workbookStream = (archive.GetEntry("xl/workbook.xml")
            ?? throw new InvalidOperationException("Workbook metadata is missing.")).Open();
        using var relationshipsStream = (archive.GetEntry("xl/_rels/workbook.xml.rels")
            ?? throw new InvalidOperationException("Workbook relationships are missing.")).Open();
        var workbook = XDocument.Load(workbookStream);
        var relationships = XDocument.Load(relationshipsStream).Root!.Elements()
            .Where(element => element.Attribute("TargetMode")?.Value != "External")
            .ToDictionary(element => element.Attribute("Id")!.Value, element => element.Attribute("Target")!.Value);
        var sheets = new List<ParsedRows>();
        foreach (var sheet in workbook.Descendants(ns + "sheet"))
        {
            if (!relationships.TryGetValue(sheet.Attribute(relationshipNs + "id")!.Value, out var target)) continue;
            var path = new Uri(new Uri("https://workbook.local/xl/"), target).AbsolutePath.TrimStart('/');
            using var sheetStream = (archive.GetEntry(path)
                ?? throw new InvalidOperationException($"Worksheet {sheet.Attribute("name")?.Value} is missing.")).Open();
            var document = XDocument.Load(sheetStream);
            var xmlRows = document.Descendants(ns + "row").ToList();
            var records = xmlRows.Select(row => ReadXlsxRow(row, sharedStrings, ns)).ToList();
            var rowNumbers = xmlRows.Select((row, index) => (int?)row.Attribute("r") ?? index + 1).ToArray();
            var parsed = RecordsToRows(records, sheet.Attribute("name")?.Value, rowNumbers);
            if (RequiredHeaderGroups.All(group => group.Any(header => parsed.Headers.Contains(header, StringComparer.OrdinalIgnoreCase))))
                sheets.Add(parsed);
        }

        // Prioritized sheets are the cleaned data; Source Data repeats the raw leads.
        var selected = sheets.Where(sheet => sheet.Headers.Contains("Outreach Fit /10", StringComparer.OrdinalIgnoreCase)).ToList();
        if (selected.Count == 0)
            throw new InvalidOperationException("No prioritized lead worksheet was found. Expected name columns and Outreach Fit /10.");
        return new ParsedRows(selected.SelectMany(sheet => sheet.Headers).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            selected.SelectMany(sheet => sheet.Items).ToList());
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

    private static ParsedRows RecordsToRows(IReadOnlyList<string[]> records, string? sheetName = null, IReadOnlyList<int>? rowNumbers = null)
    {
        if (records.Count == 0)
        {
            return new ParsedRows([], []);
        }

        var headers = records[0].Select(header => header.Trim()).ToArray();
        var duplicateHeader = headers.Where(header => header.Length > 0)
            .GroupBy(header => header, StringComparer.OrdinalIgnoreCase).FirstOrDefault(group => group.Count() > 1)?.Key;
        if (duplicateHeader is not null)
            throw new InvalidOperationException($"Duplicate column '{duplicateHeader}' in {sheetName ?? "CSV file"}.");
        var rows = records.Skip(1)
            .Select((values, index) => new ImportRow(rowNumbers?[index + 1] ?? index + 2, headers, values, sheetName))
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

        if (inQuotes)
            throw new InvalidOperationException("CSV contains an unclosed quoted field.");

        if (value.Length > 0 || fields.Count > 0)
        {
            fields.Add(value.ToString());
            records.Add(fields.ToArray());
        }

        return records;
    }

    private static ImportCandidate? MapCandidate(ImportRow row, List<LeadImportIssue> issues)
    {
        var issueCount = issues.Count;
        var leadIdText = row.Get("Lead Id");
        var leadId = Guid.TryParse(leadIdText, out var id) ? id : Guid.NewGuid();
        if (leadIdText is not null && (!Guid.TryParse(leadIdText, out _) || leadId == Guid.Empty))
            issues.Add(new LeadImportIssue(row.RowNumber, "Lead Id", "Lead ID must be a non-empty UUID."));

        var displayName = row.Get("Display Name") ?? string.Join(' ', new[] { row.Get("First Name"), row.Get("Last Name") }.OfType<string>());
        if (string.IsNullOrWhiteSpace(displayName))
            issues.Add(new LeadImportIssue(row.RowNumber, "Display Name", "A contact name is required."));

        var email = row.Get("Professional Email")?.ToLowerInvariant();
        var linkedInValue = row.Get("LinkedIn");
        var linkedInUrl = PublicProfileUrlDiscoveryService.NormalizeLinkedInUrl(linkedInValue);
        if (email is not null && (!MailAddress.TryCreate(email, out var address) || address.Address != email || email.Contains('\r') || email.Contains('\n')))
            issues.Add(new LeadImportIssue(row.RowNumber, "Professional Email", "A valid professional email is required."));
        if (linkedInValue is not null && linkedInUrl is null)
            issues.Add(new LeadImportIssue(row.RowNumber, "LinkedIn", "A valid LinkedIn URL is required."));
        if (email is null && linkedInUrl is null && leadIdText is null)
            issues.Add(new LeadImportIssue(row.RowNumber, "Professional Email / LinkedIn / Lead Id", "An email, LinkedIn URL or lead ID is required."));

        var rank = ReadInteger("Rank");
        var sourceRow = ReadInteger("Source Row");
        var originalScore = ReadInteger("Original Fit Score", 100);
        var contactStatus = ContactStatus.NotContacted;
        if (row.Get("Outreach Status") is { } statusValue &&
            (!Enum.TryParse(statusValue.Replace(" ", string.Empty), ignoreCase: true, out contactStatus) || !Enum.IsDefined(contactStatus)))
            issues.Add(new LeadImportIssue(row.RowNumber, "Outreach Status", "Expected a valid contact status."));
        decimal? outreachScore = null;
        if (row.Get("Outreach Fit /10") is { } value)
        {
            if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var score) && score >= 0 && score <= 10 && decimal.Round(score, 2) == score)
                outreachScore = score;
            else
                issues.Add(new LeadImportIssue(row.RowNumber, "Outreach Fit /10", "Expected a score from 0 to 10 with at most two decimal places."));
        }
        if (issues.Count > issueCount)
        {
            for (var index = issueCount; index < issues.Count; index++)
                issues[index] = issues[index] with { SourceSheet = row.SheetName };
            return null;
        }

        var companyName = row.Get("Company");
        var companyDomain = NormalizeDomain(row.Get("Company Domain"));
        var lead = new Lead
        {
            Id = leadId,
            DisplayName = displayName,
            FirstName = row.Get("First Name"),
            LastName = row.Get("Last Name"),
            RoleTitle = row.Get("Role / Title"),
            PublicEmail = email,
            LinkedInUrl = linkedInUrl,
            WebsiteUrl = row.Get("Website") ?? (companyDomain is null ? null : $"https://{companyDomain}"),
            XUrl = row.Get("X URL"),
            GitHubUrl = row.Get("GitHub URL"),
            Rank = rank,
            PriorityGroup = row.Get("Priority Group"),
            OutreachFitScore = outreachScore,
            OriginalFitScore = originalScore,
            FitScore = originalScore ?? 0,
            ZextriSegment = row.Get("Zextri Segment"),
            CountryUnverified = row.Get("Country (Unverified)"),
            PersonalizationAngle = row.Get("Personalization Angle"),
            OutreachScoreRationale = row.Get("Score Rationale"),
            DataQualityFlags = row.Get("Data Quality Flags"),
            RecommendedAction = row.Get("Recommended Action"),
            Notes = row.Get("Notes"),
            SourceRow = sourceRow,
            SourceSheet = row.SheetName,
            IsImported = true,
            Status = LeadStatus.NeedsManualReview,
            ContactStatus = contactStatus,
            Company = companyName is null && companyDomain is null ? null : new Company
            {
                Name = companyName ?? string.Empty,
                Domain = companyDomain
            }
        };
        if (linkedInUrl is not null)
            lead.SourceProfiles.Add(new SourceProfile
            {
                Kind = SourceKind.LinkedInProfileUrlOnly,
                Url = linkedInUrl,
                PublicHandle = displayName
            });
        return new ImportCandidate(row.RowNumber, lead);

        int? ReadInteger(string field, int maximum = int.MaxValue)
        {
            if (row.Get(field) is not { } text) return null;
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) && number >= 0 && number <= maximum)
                return number;
            issues.Add(new LeadImportIssue(row.RowNumber, field, $"Expected an integer from 0 to {maximum}."));
            return null;
        }
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

    private static string BuildDuplicateReason(string key, bool existing = true)
    {
        var field = key.StartsWith("id:", StringComparison.OrdinalIgnoreCase) ? "ID"
            : key.StartsWith("email:", StringComparison.OrdinalIgnoreCase) ? "email" : "LinkedIn URL";
        return existing ? $"Existing lead with same {field}" : $"Duplicate in this file with same {field}";
    }

    private sealed record ParsedRows(string[] Headers, IReadOnlyList<ImportRow> Items);

    private sealed class ImportRow(int rowNumber, string[] headers, string[] values, string? sheetName = null)
    {
        public int RowNumber { get; } = rowNumber;
        public string? SheetName { get; } = sheetName;

        public bool HasValues => values.Any(value => !string.IsNullOrWhiteSpace(value));

        public string? Get(string header)
        {
            var index = Array.FindIndex(headers, item => item.Equals(header, StringComparison.OrdinalIgnoreCase));
            var value = index >= 0 && index < values.Length ? values[index].Trim() : null;
            return string.IsNullOrWhiteSpace(value) || value.Equals("NULL", StringComparison.OrdinalIgnoreCase) ? null : value;
        }

    }

    private sealed record ImportCandidate(int RowNumber, Lead Lead)
    {
        public string[] ImportKeys => new[]
        {
            $"id:{Lead.Id}",
            Lead.PublicEmail is null ? null : $"email:{Lead.PublicEmail}",
            Lead.LinkedInUrl is null ? null : $"linkedin:{Lead.LinkedInUrl}"
        }.OfType<string>().ToArray();

        public LeadImportPreviewRow ToPreviewRow(bool isDuplicate, string? duplicateReason) =>
            new(RowNumber, Lead.DisplayName, Lead.RoleTitle, Lead.Company?.Name, Lead.PublicEmail,
                Lead.LinkedInUrl, Lead.ZextriSegment, Lead.FitScore, Lead.ContactStatus, isDuplicate, duplicateReason)
            {
                Rank = Lead.Rank,
                PriorityGroup = Lead.PriorityGroup,
                OutreachFitScore = Lead.OutreachFitScore,
                DataQualityFlags = Lead.DataQualityFlags,
                RecommendedAction = Lead.RecommendedAction,
                SourceSheet = Lead.SourceSheet
            };
    }
}
