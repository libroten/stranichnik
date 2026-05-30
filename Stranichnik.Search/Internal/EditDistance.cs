namespace Stranichnik.Search.Internal;

internal static class EditDistance
{
    public static int CalculateBounded(string source, string target, int maximumDistance)
    {
        if (maximumDistance < 0)
        {
            return maximumDistance + 1;
        }

        if (Math.Abs(source.Length - target.Length) > maximumDistance)
        {
            return maximumDistance + 1;
        }

        if (source.Length == 0)
        {
            return target.Length <= maximumDistance ? target.Length : maximumDistance + 1;
        }

        if (target.Length == 0)
        {
            return source.Length <= maximumDistance ? source.Length : maximumDistance + 1;
        }

        int[] previous = new int[target.Length + 1];
        int[] current = new int[target.Length + 1];

        for (int column = 0; column <= target.Length; column++)
        {
            previous[column] = column;
        }

        for (int row = 1; row <= source.Length; row++)
        {
            current[0] = row;
            int rowMinimum = current[0];

            for (int column = 1; column <= target.Length; column++)
            {
                int substitutionCost = source[row - 1] == target[column - 1] ? 0 : 1;
                current[column] = Math.Min(
                    Math.Min(current[column - 1] + 1, previous[column] + 1),
                    previous[column - 1] + substitutionCost);
                rowMinimum = Math.Min(rowMinimum, current[column]);
            }

            if (rowMinimum > maximumDistance)
            {
                return maximumDistance + 1;
            }

            (previous, current) = (current, previous);
        }

        return previous[target.Length];
    }

    public static int GetMaximumDistance(string token)
    {
        return token.Length switch
        {
            <= 2 => 0,
            <= 5 => 1,
            _ => 2
        };
    }
}
