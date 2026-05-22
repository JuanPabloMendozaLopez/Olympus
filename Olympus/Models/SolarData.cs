namespace Olympus.Models
{
    public class HourlyRadiationSlot
    {
        public int Hour { get; set; }
        public string Label { get; set; } = "";
        public double RadiationWm2 { get; set; }
        public double RadiationKwhM2 { get; set; }
    }

    public class SolarData
    {
        public DateTime Date { get; set; }
        public string Location { get; set; } = "Riohacha, La Guajira";
        public double RadiationKwhM2 { get; set; }
        public double TemperatureC { get; set; }
        public double WindSpeedKmh { get; set; }
        public double UvIndex { get; set; }
        public int SolarIndex { get; set; }
        public string SolarIndexLabel { get; set; } = "";
        public string SolarIndexColor { get; set; } = "";
        public string Sunrise { get; set; } = "";
        public string Sunset { get; set; } = "";
        public List<string> OptimalHours { get; set; } = new();
        public List<string> PeakCostHours { get; set; } = new();
        public bool Cached { get; set; }

        // Contexto histórico — vs. promedio 90 días NASA POWER
        public double HistoricalAvgKwhM2 { get; set; }
        public double VsHistoricalPct { get; set; }

        // Radiación horaria real (Open-Meteo hourly)
        public List<HourlyRadiationSlot> HourlyRadiation { get; set; } = new();
    }
}
