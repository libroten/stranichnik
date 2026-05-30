namespace Stranichnik.Search.Internal;

internal static class SearchScoringParameters
{
    public const double Bm25K1 = 1.2;
    public const double Bm25B = 0.75;
    public const int MinimumPrefixLength = 3;
    public const int MaximumPrefixExpansionsPerQueryToken = 128;
    public const int MinimumFuzzyLength = 3;
    public const int MaximumFuzzyCandidateTermsPerQueryToken = 256;
    public const double FullCoverageMultiplier = 1.20;
    public const double PartialCoverageMultiplier = 1.05;
    public const double TitlePhraseBonus = 2.0;
    public const double UrlPhraseBonus = 1.2;
    public const double OrderedTokenBonus = 1.0;
    public const double AdjacentTokenBonus = 1.5;
    public const double ExactHostQueryBonus = 1.5;
    public const double HostStartsWithQueryBonus = 0.8;

    public static double GetMatchKindMultiplier(TermMatchKind kind)
    {
        return kind switch
        {
            TermMatchKind.Exact => 1.0,
            TermMatchKind.Prefix => 0.72,
            TermMatchKind.FuzzyDistanceOne => 0.55,
            TermMatchKind.FuzzyDistanceTwo => 0.35,
            _ => 1.0
        };
    }

    public static double GetFieldWeight(IndexedFieldName field)
    {
        return field switch
        {
            IndexedFieldName.Title => 3.0,
            IndexedFieldName.UrlHost => 3.0,
            IndexedFieldName.UrlDomainParts => 2.6,
            IndexedFieldName.UrlPathParts => 1.8,
            IndexedFieldName.Tags => 2.2,
            IndexedFieldName.Notes => 1.1,
            IndexedFieldName.UrlText => 0.7,
            _ => 1.0
        };
    }
}
