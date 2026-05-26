using Olympus.Models;

namespace Olympus.Services
{
    /// <summary>
    /// Singleton con estadísticas históricas de apagones en La Guajira
    /// y lógica auxiliar del Modo Apagón.
    /// Fuente: SSPD — SAIDI/SAIFI reportados para La Guajira 2022-2023.
    /// </summary>
    public class BlackoutHistoryService
    {
        // ── Datos SSPD La Guajira ────────────────────────────────────────────
        // SAIDI 2022: 60.34 h/año · SAIFI 2022: 37.84 eventos/año  → avg 95.7 min
        // SAIDI 2023: 60.56 h/año · SAIFI 2023: 34.05 eventos/año  → avg 106.7 min
        // Promedio consolidado: 101 minutos ≈ 1h 41min por evento

        public const double SaidiHoursYear   = 60.45;   // promedio 2022–2023
        public const double SaifiEventsYear  = 35.95;   // promedio 2022–2023
        public const int    AvgOutageMinutes = 101;      // 1h 41min
        public const int    P90OutageMinutes = 240;      // percentil 90 estimado (4h)

        public static readonly List<string> PeakRiskHours = new()
        {
            "14:00–19:00",  // pico de calor + máxima demanda de A/C en la ciudad
            "20:00–23:00"   // pico tarifario nocturno
        };

        // ── Cargas por defecto según perfil ─────────────────────────────────
        private static readonly Dictionary<string, (List<string> Keep, List<string> Reduce, List<string> Disconnect)>
            DefaultLoads = new(StringComparer.OrdinalIgnoreCase)
            {
                ["hotel"] = (
                    Keep:       new() { "Recepción y cerraduras electrónicas", "Iluminación de emergencia", "Neveras y cuartos fríos de cocina", "Bombas de agua", "Comunicaciones (teléfono, PBX)" },
                    Reduce:     new() { "A/C en pisos con huéspedes (subir a 26°C)", "Iluminación de pasillos al 50%" },
                    Disconnect: new() { "A/C en pisos sin check-in", "Iluminación exterior decorativa", "Jacuzzi y piscina", "Oficinas administrativas", "Lavandería industrial" }
                ),
                ["hielera"] = (
                    Keep:       new() { "Compresores principales de congelación", "Paneles de control y sensores de temperatura", "Iluminación mínima de seguridad" },
                    Reduce:     new() { "Iluminación de áreas de trabajo al 50%", "Ventilación de sala de máquinas (nivel mínimo)" },
                    Disconnect: new() { "Oficinas y administración", "Iluminación exterior", "Sistemas de carga no críticos", "A/C de oficinas" }
                ),
                ["restaurant"] = (
                    Keep:       new() { "Cámaras frigoríficas y congeladores", "POS y sistemas de caja", "Iluminación de cocina esencial", "Comunicaciones" },
                    Reduce:     new() { "A/C del salón (subir a 26°C)", "Iluminación del salón al 60%" },
                    Disconnect: new() { "Letreros luminosos exteriores", "Cocina industrial no urgente", "Música ambiental", "Decoración iluminada" }
                ),
                ["community"] = (
                    Keep:       new() { "Bombas de agua potable", "Comunicaciones de emergencia", "Puntos de recarga comunitarios", "Iluminación de seguridad en zonas críticas" },
                    Reduce:     new() { "Iluminación pública no esencial", "Sistemas de riego (diferir)" },
                    Disconnect: new() { "Alumbrado decorativo", "Sistemas no críticos de edificios públicos" }
                )
            };

        // ── API pública ──────────────────────────────────────────────────────

        public BlackoutHistoryResponse GetHistoryStats() => new()
        {
            Source              = "SSPD · La Guajira 2022–2023",
            SaidiHoursYear      = SaidiHoursYear,
            SaifiEventsYear     = SaifiEventsYear,
            AvgOutageMinutes    = AvgOutageMinutes,
            MaxOutageMinutes    = P90OutageMinutes,
            PeakRiskHours       = PeakRiskHours,
            Note                = "SAIDI/SAIFI son indicadores departamentales. El dato fino por circuito requiere telemetría propia."
        };

        public (List<string> Keep, List<string> Reduce, List<string> Disconnect)
            GetDefaultLoads(string profileType)
        {
            return DefaultLoads.TryGetValue(profileType, out var loads)
                ? loads
                : DefaultLoads["hotel"];
        }

        /// <summary>
        /// Estima la autonomía en minutos dado el contexto energético del negocio.
        /// Es una estimación conservadora para orientar decisiones, no telemetría real.
        /// </summary>
        public int CalculateAutonomyMinutes(
            double radiationKwhM2,
            bool   hasPanels,
            bool   hasBattery,
            double batteryKwh,
            double monthlyKwh)
        {
            if (!hasPanels && !hasBattery) return 0;

            double dailyKwh     = monthlyKwh > 0 ? monthlyKwh / 30.0 : 400.0;
            double criticalKw   = (dailyKwh / 24.0) * 0.25;  // 25% del consumo promedio horario
            if (criticalKw < 1.0) criticalKw = 1.0;

            // Contribución solar: estimamos que paneles cubren ~20% de carga crítica
            // escalado por la radiación actual vs. pico (6.5 kWh/m²)
            double solarFraction  = hasPanels ? Math.Min(1.0, radiationKwhM2 / 6.5) : 0;
            double solarKw        = criticalKw * 0.20 * solarFraction;

            // Horas restantes de sol (cap 6h para no inflar)
            double solarHours = solarKw > 0
                ? Math.Min(6.0, (DateTime.Now.Hour < 18 ? (18 - DateTime.Now.Hour) : 0))
                : 0;

            // Contribución de batería
            double batteryHours = (hasBattery && batteryKwh > 0)
                ? batteryKwh / criticalKw
                : 0;

            return (int)Math.Round((solarHours + batteryHours) * 60);
        }

        /// <summary>Nivel de urgencia en función del tiempo transcurrido y la autonomía.</summary>
        public string GetUrgencyLevel(int outageMinutes, int autonomyMinutes, string profileType)
        {
            // Hielera: umbral de urgencia más bajo por riesgo térmico
            bool isColdChain = string.Equals(profileType, "hielera", StringComparison.OrdinalIgnoreCase);

            if (isColdChain && outageMinutes >= 60)  return "critical";
            if (isColdChain && outageMinutes >= 30)  return "high";

            if (autonomyMinutes > 0)
            {
                double remaining = autonomyMinutes - outageMinutes;
                if (remaining < 0)               return "critical";
                if (remaining < autonomyMinutes * 0.15) return "critical";
                if (remaining < autonomyMinutes * 0.35) return "high";
                if (remaining < autonomyMinutes * 0.60) return "moderate";
                return "low";
            }

            // Sin recursos propios — urgencia por duración
            if (outageMinutes >= AvgOutageMinutes * 1.5) return "critical";
            if (outageMinutes >= AvgOutageMinutes)        return "high";
            if (outageMinutes >= AvgOutageMinutes * 0.5)  return "moderate";
            return "low";
        }

        /// <summary>
        /// Genera el reporte post-apagón con recomendación de batería solar.
        /// Usa la misma fórmula que el simulador ROI del AiController.
        /// </summary>
        public BlackoutReportResult BuildReport(
            string name,
            string profileType,
            int    totalMinutes,
            double monthlyKwh,
            double tariffCopKwh)
        {
            string category = totalMinutes < 60 ? "corto"
                : totalMinutes < AvgOutageMinutes * 1.2 ? "normal"
                : "prolongado";

            // ¿Cuántos kWh necesita para cubrir un apagón promedio de forma autónoma?
            double dailyKwh    = monthlyKwh > 0 ? monthlyKwh / 30.0 : 400.0;
            double criticalKw  = (dailyKwh / 24.0) * 0.25;
            double neededKwh   = criticalKw * (AvgOutageMinutes / 60.0);
            // Redondear al tamaño comercial más cercano
            double batteryKwh  = Math.Ceiling(neededKwh / 5.0) * 5.0;  // múltiplos de 5 kWh
            if (batteryKwh < 5) batteryKwh = 5;

            const double BatteryCostCopPerKwh = 1_800_000; // COP/kWh instalado (2025)
            double investment  = batteryKwh * BatteryCostCopPerKwh;

            // Ahorro: evita pérdidas por apagón (estimado)
            double annualLossCop = (SaidiHoursYear * criticalKw * tariffCopKwh)
                                 + (profileType == "hielera" ? 500_000 * SaifiEventsYear : 0);
            double payback = annualLossCop > 0 ? investment / annualLossCop : 99;

            string summary = category == "corto"
                ? $"El apagón duró {totalMinutes} min — por debajo del promedio histórico de {AvgOutageMinutes} min. Buen resultado de resiliencia."
                : category == "normal"
                ? $"El apagón duró {totalMinutes} min, dentro del rango histórico normal (promedio: {AvgOutageMinutes} min)."
                : $"El apagón duró {totalMinutes} min — por encima del promedio histórico ({AvgOutageMinutes} min). Evento prolongado.";

            string batteryRec = $"Para ser completamente autónomo durante un apagón promedio en La Guajira, " +
                                $"necesitarías una batería de {batteryKwh:F0} kWh. " +
                                $"Inversión estimada: ${investment:N0} COP con payback de {payback:F1} años.";

            return new BlackoutReportResult
            {
                Name                    = name,
                TotalMinutes            = totalMinutes,
                HistoricalAvgMinutes    = AvgOutageMinutes,
                DurationCategory        = category,
                Summary                 = summary,
                BatteryRecommendation   = batteryRec,
                EstimatedBatteryKwh     = batteryKwh,
                BatteryInvestmentCop    = investment,
                BatteryPaybackYears     = Math.Round(payback, 1),
                GeneratedAt             = DateTime.Now
            };
        }
    }
}
