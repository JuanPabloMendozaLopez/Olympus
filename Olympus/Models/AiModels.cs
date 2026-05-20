namespace Olympus.Models
{
    // ---------- Requests ----------
    public class RecommendationRequest
    {
        public string CompanyType { get; set; } = "hotel";
        public string CompanyName { get; set; } = "";
        public double MonthlyConsumptionKwh { get; set; }   // consumo declarado
        public int? CompanySize { get; set; }                // habitaciones / m² / empleados
        public string? PeakUsageHours { get; set; }          // ej "18:00-22:00"
    }

    public class ChatRequest
    {
        public string Message { get; set; } = "";
        public string CompanyType { get; set; } = "hotel";
        public double MonthlyConsumptionKwh { get; set; }    // mismo perfil
        public int? CompanySize { get; set; }
    }

    // ---------- Responses ----------
    public class RecommendationResult
    {
        public string CompanyName { get; set; } = "";
        public string CompanyType { get; set; } = "";
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