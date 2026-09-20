namespace GeoSilence.Models
{
    public enum PlaceType
    {
        Education,
        Religious,
        Work,
        Hospital,
        Other
    }

    public static class PlaceTypeExtensions
    {
        public static string ToIcon(this PlaceType type)
        {
            return type switch
            {
                PlaceType.Education => "🎓",
                PlaceType.Religious => "🕌",
                PlaceType.Work => "💼",
                PlaceType.Hospital => "🏨",
                _ => "👤"
            };
        }

        public static string ToDisplayName(this PlaceType type)
        {
            return type switch
            {
                PlaceType.Education => "Education",
                PlaceType.Religious => "Religious",
                PlaceType.Work => "Work",
                PlaceType.Hospital => "Hospital",
                _ => "Other"
            };
        }
    }
}
