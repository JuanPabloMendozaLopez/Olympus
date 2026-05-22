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
        public double? TariffCopKwh { get; set; }           // tarifa real del perfil (comercial, industrial, residencial)
        public double? OperatingHoursPerDay { get; set; }   // horas de operación al día

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
        public double? TariffCopKwh { get; set; }
        public double? OperatingHoursPerDay { get; set; }

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
        public long TotalSavingsCopDay { get; set; }
        public long TotalSavingsCopMonth { get; set; }
        public string? Alert { get; set; }
    }

    public class Recommendation
    {
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string Priority { get; set; } = "media";
        public string TimeWindow { get; set; } = "";
        public long SavingsCopDay { get; set; }
    }

    public class ChatResult
    {
        public string Reply { get; set; } = "";
        public DateTime Timestamp { get; set; }
    }

    // ── Simulation ─────────────────────────────────────────────────────────
    public class SimulateRequest
    {
        public string ProfileName { get; set; } = "";
        public string TargetType { get; set; } = "company";
        public double KwpInstalled { get; set; } = 5.0;
        public double TariffCopKwh { get; set; } = 1050;
        public double MonthlyConsumptionKwh { get; set; } = 5400;
        public string? CompanyType { get; set; }
    }

    public class SimulateResult
    {
        public double KwpInstalled { get; set; }
        public double AnnualGenerationKwh { get; set; }
        public double AnnualSavingsCop { get; set; }
        public double MonthlySavingsCop { get; set; }
        public double DailySavingsCop { get; set; }
        public double InvestmentCop { get; set; }
        public double PaybackYears { get; set; }
        public double Co2ReductionKgYear { get; set; }
        public double CoveragePercent { get; set; }
        public double Roi25Years { get; set; }
        public string ProfileName { get; set; } = "";
        public string Summary { get; set; } = "";
    }

    // ── Insights ────────────────────────────────────────────────────────────
    public class InsightsRequest
    {
        public string TargetType { get; set; } = "company";
        public string Name { get; set; } = "";
        public string? CompanyType { get; set; }
        public double? MonthlyConsumptionKwh { get; set; }
        public double? TariffCopKwh { get; set; }
        public int? PopulationEstimate { get; set; }
    }

    public class InsightItem
    {
        public string Icon { get; set; } = "💡";
        public string Title { get; set; } = "";
        public string Body { get; set; } = "";
        public string Impact { get; set; } = "";  // "alto" | "medio" | "bajo"
    }

    public class InsightsResult
    {
        public string Headline { get; set; } = "";
        public string ExecutiveSummary { get; set; } = "";
        public List<InsightItem> Insights { get; set; } = new();
        public string SolarContext { get; set; } = "";
        public DateTime GeneratedAt { get; set; }
        public double RadiationToday { get; set; }
        public int SolarIndex { get; set; }
    }

    // ── Alerts ──────────────────────────────────────────────────────────────
    public class AlertsRequest
    {
        public string TargetType { get; set; } = "company";
        public string Name { get; set; } = "";
        public string? CompanyType { get; set; }
        public double? MonthlyConsumptionKwh { get; set; }
        public double? TariffCopKwh { get; set; }
    }

    public class EnergyAlert
    {
        public string Type { get; set; } = "info";   // "info" | "warning" | "critical"
        public string Title { get; set; } = "";
        public string Message { get; set; } = "";
        public string Action { get; set; } = "";
        public string TimeWindow { get; set; } = "";
        public string Icon { get; set; } = "⚡";
    }

    public class AlertsResult
    {
        public List<EnergyAlert> Alerts { get; set; } = new();
        public DateTime GeneratedAt { get; set; }
        public string SolarSummary { get; set; } = "";
        public double RadiationToday { get; set; }
        public int SolarIndex { get; set; }
    }
}