namespace Stranichnik.Search.Internal;

internal static class SearchScorer
{
    public static double ScoreExactMatch(
        IndexedFieldName field,
        int termFrequency,
        int documentCount,
        int documentFrequency,
        int fieldLength,
        double averageFieldLength,
        TermMatchKind matchKind)
    {
        if (termFrequency <= 0 || documentCount <= 0 || documentFrequency <= 0)
        {
            return 0.0;
        }

        double safeAverageFieldLength = averageFieldLength > 0.0 ? averageFieldLength : 1.0;
        double safeFieldLength = fieldLength > 0 ? fieldLength : 1.0;
        double inverseDocumentFrequency = Math.Log(
            1.0 + ((documentCount - documentFrequency + 0.5) / (documentFrequency + 0.5)));
        double numerator = termFrequency * (SearchScoringParameters.Bm25K1 + 1.0);
        double denominator = termFrequency
            + (SearchScoringParameters.Bm25K1
                * (1.0 - SearchScoringParameters.Bm25B
                    + (SearchScoringParameters.Bm25B * (safeFieldLength / safeAverageFieldLength))));

        return inverseDocumentFrequency
            * (numerator / denominator)
            * SearchScoringParameters.GetFieldWeight(field)
            * SearchScoringParameters.GetMatchKindMultiplier(matchKind);
    }
}
