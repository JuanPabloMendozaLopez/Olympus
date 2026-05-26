namespace Olympus.Models
{
    // ── Request ─────────────────────────────────────────────────────────────
    public class BlackoutRequest
    {
        public string ProfileType { get; set; } = "hotel";        // hotel | hielera | restaurant | community
        public string Name        { get; set; } = "";
        public int    OutageMinutes { get; set; } = 0;            // cuánto lleva el apagón

        public bool   HasSolarPanels     { get; set; } = false;
        public bool   HasBattery         { get; set; } = false;
        public double BatteryCapacityKwh { get; set; } = 0;

        public List<string> CriticalLoads        { get; set; } = new();
        public double       MonthlyConsumptionKwh { get; set; } = 0;
        public double       TariffCopKwh          { get; set; } = 1050;
    }

    public class BlackoutReportRequest
    {
        public string ProfileType     { get; set; } = "hotel";
        public string Name            { get; set; } = "";
        public int    TotalMinutes    { get; set; } = 0;
        public bool   HasSolarPanels  { get; set; } = false;
        public bool   HasBattery      { get; set; } = false;
        public double BatteryCapacityKwh { get; set; } = 0;
        public double MonthlyConsumptionKwh { get; set; } = 0;
        public double TariffCopKwh    { get; set; } = 1050;
    }

    // ── Responses ────────────────────────────────────────────────────────────
    public class BlackoutPriorityMatrix
    {
        public List<string> Keep       { get; set; } = new();
        public List<string> Reduce     { get; set; } = new();
        public List<string> Disconnect { get; set; } = new();
    }

    public class BlackoutResponse
    {
        public int    OutageMinutes           { get; set; }
        public int    HistoricalAvgMinutes    { get; set; }  // promedio SAIDI/SAIFI La Guajira
        public int    EstimatedAutonomyMinutes { get; set; }
        public string UrgencyLevel            { get; set; } = "low"; // low | moderate | high | critical
        public double CurrentRadiationKwhM2   { get; set; }

        // Impacto económico del apagón
        public double LossPerHourCop  { get; set; }  // COP/hora estimados de pérdida (carga crítica)
        public double ElapsedLossCop  { get; set; }  // COP perdidos hasta el momento
        public string ProfileType     { get; set; } = "";  // hotel | hielera | restaurant | community
        public string ProfileName     { get; set; } = "";  // nombre del negocio

        public BlackoutPriorityMatrix PriorityMatrix { get; set; } = new();

        public string   Instructions    { get; set; } = "";  // párrafo IA contextual
        public string   CriticalAlert   { get; set; } = "";  // acción más urgente
        public List<string> RecoveryActions { get; set; } = new(); // qué hacer cuando vuelva la luz
        public string   StatusMessage   { get; set; } = "";  // frase de 1 línea para UI
        public DateTime GeneratedAt     { get; set; } = DateTime.Now;
    }

    public class BlackoutHistoryResponse
    {
        public string Source              { get; set; } = "SSPD · La Guajira 2022–2023";
        public double SaidiHoursYear      { get; set; }  // horas de interrupción / año
        public double SaifiEventsYear     { get; set; }  // eventos de interrupción / año
        public int    AvgOutageMinutes    { get; set; }  // duración promedio por evento
        public int    MaxOutageMinutes    { get; set; }  // percentil 90
        public List<string> PeakRiskHours { get; set; } = new();
        public string Note                { get; set; } = "";
    }

    public class BlackoutReportResult
    {
        public string Name               { get; set; } = "";
        public int    TotalMinutes       { get; set; }
        public int    HistoricalAvgMinutes { get; set; }
        public string DurationCategory   { get; set; } = ""; // "corto" | "normal" | "prolongado"
        public string Summary            { get; set; } = "";
        public string BatteryRecommendation { get; set; } = "";
        public double EstimatedBatteryKwh   { get; set; }
        public double BatteryInvestmentCop  { get; set; }
        public double BatteryPaybackYears   { get; set; }
        public DateTime GeneratedAt      { get; set; } = DateTime.Now;
    }
}
