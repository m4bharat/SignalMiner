using SignalMiner.Domain;

namespace SignalMiner.Application;

public sealed class LeadScoringService : ILeadScoringService
{
    private static readonly string[] FounderKeywords = ["founder", "co-founder", "ceo", "cto", "builder", "indie hacker"];
    private static readonly string[] SaaSKeywords = ["saas", "b2b", "subscription", "crm", "workflow", "automation"];
    private static readonly string[] AiKeywords = ["ai", "llm", "agent", "copilot", "machine learning", "automation"];

    public LeadScore Score(Lead lead)
    {
        var text = string.Join(' ', new[]
        {
            lead.DisplayName,
            lead.RoleTitle,
            lead.Notes,
            lead.Company?.Summary,
            string.Join(' ', lead.Company?.Keywords ?? [])
        }.Where(x => !string.IsNullOrWhiteSpace(x))).ToLowerInvariant();

        var score = 0;
        var reasons = new List<string>();
        AddKeywordScore(text, FounderKeywords, 25, "founder/operator language", ref score, reasons);
        AddKeywordScore(text, SaaSKeywords, 20, "SaaS/business software language", ref score, reasons);
        AddKeywordScore(text, AiKeywords, 20, "AI/productivity language", ref score, reasons);

        if (!string.IsNullOrWhiteSpace(lead.LinkedInUrl))
        {
            score += 10;
            reasons.Add("public LinkedIn URL present");
        }

        if (!string.IsNullOrWhiteSpace(lead.XUrl))
        {
            score += 10;
            reasons.Add("public X URL present");
        }

        var activity = lead.SourceProfiles.Sum(p => p.PublicActivityCount ?? 0);
        if (activity > 25)
        {
            score += 10;
            reasons.Add("visible public posting/activity");
        }

        var websiteQuality = lead.WebsiteSnapshots.OrderByDescending(s => s.CapturedAt).FirstOrDefault()?.QualityScore ?? 0;
        if (websiteQuality >= 70)
        {
            score += 15;
            reasons.Add("high quality website");
        }

        score = Math.Clamp(score, 0, 100);
        return new LeadScore(score, reasons.Count == 0 ? "insufficient public fit signals" : string.Join("; ", reasons));
    }

    private static void AddKeywordScore(string text, string[] keywords, int points, string reason, ref int score, List<string> reasons)
    {
        if (keywords.Any(text.Contains))
        {
            score += points;
            reasons.Add(reason);
        }
    }
}
