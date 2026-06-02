namespace GeoSilence.Utilities
{
    public static class AvatarHelper
    {
        private static readonly string[] Palette =
        {
            "#2563EB",
            "#0F766E",
            "#C2410C",
            "#7C3AED",
            "#B91C1C",
            "#0369A1"
        };

        public static string GetInitials(
            string? firstName,
            string? lastName,
            string? displayName,
            string? email)
        {
            if (!string.IsNullOrWhiteSpace(firstName) || !string.IsNullOrWhiteSpace(lastName))
            {
                var first = FirstLetter(firstName);
                var last = FirstLetter(lastName);
                var combined = string.Concat(first, last);
                if (!string.IsNullOrWhiteSpace(combined))
                    return combined.ToUpperInvariant();
            }

            if (!string.IsNullOrWhiteSpace(displayName))
            {
                var tokens = displayName
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                if (tokens.Length >= 2)
                    return $"{FirstLetter(tokens[0])}{FirstLetter(tokens[1])}".ToUpperInvariant();

                if (tokens.Length == 1)
                    return FirstLetter(tokens[0]).ToUpperInvariant();
            }

            if (!string.IsNullOrWhiteSpace(email))
                return FirstLetter(email).ToUpperInvariant();

            return "U";
        }

        public static string GetAvatarColor(string seed)
        {
            if (string.IsNullOrWhiteSpace(seed))
                return Palette[0];

            var hash = 0;
            foreach (var ch in seed)
                hash = ((hash * 31) + ch) & 0x7FFFFFFF;

            return Palette[hash % Palette.Length];
        }

        public static (string FirstName, string LastName) SplitDisplayName(string? displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName))
                return (string.Empty, string.Empty);

            var tokens = displayName
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (tokens.Length == 0)
                return (string.Empty, string.Empty);

            if (tokens.Length == 1)
                return (tokens[0], string.Empty);

            return (tokens[0], string.Join(' ', tokens.Skip(1)));
        }

        public static string BuildDisplayName(string firstName, string lastName)
        {
            return string.Join(
                ' ',
                new[] { firstName?.Trim(), lastName?.Trim() }
                    .Where(value => !string.IsNullOrWhiteSpace(value)));
        }

        private static string FirstLetter(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            return value.Trim()[0].ToString();
        }
    }
}
