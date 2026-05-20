namespace Olympus.Models
{
    // ---------- Requests ----------
    public class RecommendationRequest
    {
        // Discriminador: "company" o "community"
        public string TargetType { get; set; } = "company";

        // Común a ambos modos
        public string Name { get; set; } = "";

        // --- Solo modo "company" ---
        public string? CompanyType { get; set; }            // hotel, hielera, retail, restaurante
        public double? MonthlyConsumptionKwh { get; set; }
        public int? CompanySize { get; set; }
        public string? PeakUsageHours { get; set; }
        public List<string>? MainLoads { get; set; }

        // --- Solo modo "community" ---
        public int? PopulationEstimate { get; set; }
        public List<string>? MainProblems { get; set; }
    }

    public class ChatRequest
    {
        public string Message { get; set; } = "";
        public string TargetType { get; set; } = "company";
        public string Name { get; set; } = "";

        // Opcionales de company
        public string? CompanyType { get; set; }
        public double? MonthlyConsumptionKwh { get; set; }
        public int? CompanySize { get; set; }
        public List<string>? MainLoads { get; set; }

        // Opcionales de community
        public int? PopulationEstimate { get; set; }
    }

    // ---------- Responses ----------
    public class RecommendationResult
    {
        public string TargetType { get; set; } = "";
        public string Name { get; set; } = "";
        public DateTime Date { get; set; }
        public double RadiationToday { get; set; }
        public int SolarIndex { get; set; }
        public string Reasoning { get; set; } = "";
        public List<Recommendation> Recommendations { get; set; } = new();
        public int TotalSavingsCopDay { get; set; }
        public int TotalSavingsCopMonth { get; set; }
        public string? Alert { get; set; }
    }

    public class Recommendation
    {
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string Priority { get; set; } = "media";
        public string TimeWindow { get; set; } = "";
        public int SavingsCopDay { get; set; }
    }

    public class ChatResult
    {
        public string Reply { get; set; } = "";
        public DateTime Timestamp { get; set; }
    }
}