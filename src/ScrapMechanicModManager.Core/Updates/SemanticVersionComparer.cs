using System.Globalization;
using System.Numerics;

namespace ScrapMechanicModManager.Core.Updates;

public static class SemanticVersionComparer
{
    public static int Compare(string left, string right)
    {
        ParsedSemanticVersion leftVersion = Parse(left, nameof(left));
        ParsedSemanticVersion rightVersion = Parse(right, nameof(right));

        int coreComparison = leftVersion.Major.CompareTo(rightVersion.Major);
        if (coreComparison != 0) return coreComparison;
        coreComparison = leftVersion.Minor.CompareTo(rightVersion.Minor);
        if (coreComparison != 0) return coreComparison;
        coreComparison = leftVersion.Patch.CompareTo(rightVersion.Patch);
        if (coreComparison != 0) return coreComparison;

        string[] leftPrerelease = leftVersion.Prerelease;
        string[] rightPrerelease = rightVersion.Prerelease;
        if (leftPrerelease.Length == 0 && rightPrerelease.Length == 0) return 0;
        if (leftPrerelease.Length == 0) return 1;
        if (rightPrerelease.Length == 0) return -1;

        int sharedLength = Math.Min(leftPrerelease.Length, rightPrerelease.Length);
        for (int index = 0; index < sharedLength; index++)
        {
            string leftIdentifier = leftPrerelease[index];
            string rightIdentifier = rightPrerelease[index];
            bool leftNumeric = BigInteger.TryParse(
                leftIdentifier,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out BigInteger leftNumber);
            bool rightNumeric = BigInteger.TryParse(
                rightIdentifier,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out BigInteger rightNumber);
            if (leftNumeric && rightNumeric)
            {
                int numericComparison = leftNumber.CompareTo(rightNumber);
                if (numericComparison != 0) return numericComparison;
                continue;
            }
            if (leftNumeric != rightNumeric) return leftNumeric ? -1 : 1;

            int textComparison = string.CompareOrdinal(leftIdentifier, rightIdentifier);
            if (textComparison != 0) return textComparison;
        }

        return leftPrerelease.Length.CompareTo(rightPrerelease.Length);
    }

    private static ParsedSemanticVersion Parse(string value, string parameterName)
    {
        if (!ModManifest.IsSemanticVersion(value))
        {
            throw new ArgumentException(
                $"Invalid semantic version: {value}.",
                parameterName);
        }

        string withoutMetadata = value.Split('+', 2)[0];
        string[] versionAndPrerelease = withoutMetadata.Split('-', 2);
        string[] core = versionAndPrerelease[0].Split('.');
        string[] prerelease = versionAndPrerelease.Length == 2
            ? versionAndPrerelease[1].Split('.')
            : [];
        return new ParsedSemanticVersion(
            BigInteger.Parse(core[0], CultureInfo.InvariantCulture),
            BigInteger.Parse(core[1], CultureInfo.InvariantCulture),
            BigInteger.Parse(core[2], CultureInfo.InvariantCulture),
            prerelease);
    }

    private sealed record ParsedSemanticVersion(
        BigInteger Major,
        BigInteger Minor,
        BigInteger Patch,
        string[] Prerelease);
}
