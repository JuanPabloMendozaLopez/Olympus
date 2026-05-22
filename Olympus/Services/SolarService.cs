using Olympus.Models;
using System.Globalization;
using System.Text.Json;

namespace Olympus.Services
{
    public class SolarService
    {
        private readonly HttpClient _httpClient;
        private readonly SolarIndexService _solarIndex;

        private const double LAT = 11.5444;
        private const double LON = -72.9072;

        public SolarService(HttpClient httpClient, SolarIndexService solarIndex)
        {
            _httpClient = httpClient;
            _solarIndex = solarIndex;
        }

        // ---------- HOY (Open-Meteo — daily + hourly en una sola llamada) ----------
        public async Task<SolarData?> GetTodayAsync()
        {
            var url = $"https://api.open-meteo.com/v1/forecast" +
                $"?latitude={Inv(LAT)}&longitude={Inv(LON)}" +
                $"&daily=shortwave_radiation_sum,temperature_2m_max,temperature_2m_min,windspeed_10m_max,uv_index_max,sunrise,sunset" +
                $"&hourly=shortwave_radiation" +
                $"&timezone=America/Bogota&forecast_days=1";

            var data = await GetOpenMeteo(url);
            if (data?.Daily == null || !data.Daily.Time.Any()) return null;

            var radiation = Math.Round(data.Daily.ShortwaveRadiationSum[0] / 3.6, 2);
            var tempMax = data.Daily.Temperature2mMax[0];
            var windKmh = data.Daily.Windspeed10mMax[0];
            var uv = data.Daily.UvIndexMax[0];
            var score = _solarIndex.Calculate(radiation, windKmh, tempMax);

            // ── Radiación horaria real ──
            var hourlySlots = new List<HourlyRadiationSlot>();
            if (data.Hourly?.ShortwaveRadiation?.Any() == true)
            {
                for (int h = 0; h < Math.Min(24, data.Hourly.ShortwaveRadiation.Count); h++)
                {
                    var wm2 = data.Hourly.ShortwaveRadiation[h];
                    hourlySlots.Add(new HourlyRadiationSlot
                    {
                        Hour = h,
                        Label = $"{h:00}:00",
                        RadiationWm2 = Math.Round(wm2, 1),
                        RadiationKwhM2 = Math.Round(wm2 / 1000.0, 3)
                    });
                }
            }

            // ── Horas óptimas dinámicas: ventana de 4h con mayor radiación entre 6-17h ──
            var optimalLabel = "10:00-14:00";
            if (hourlySlots.Any())
            {
                int peakStart = 10;
                double maxSum = 0;
                for (int h = 6; h <= 14; h++)
                {
                    double windowSum = hourlySlots
                        .Where(x => x.Hour >= h && x.Hour < h + 4)
                        .Sum(x => x.RadiationWm2);
                    if (windowSum > maxSum) { maxSum = windowSum; peakStart = h; }
                }
                optimalLabel = $"{peakStart:00}:00-{peakStart + 4:00}:00";
            }

            return new SolarData
            {
                Date = ParseDate(data.Daily.Time[0]),
                RadiationKwhM2 = radiation,
                TemperatureC = Math.Round(tempMax, 1),
                WindSpeedKmh = Math.Round(windKmh, 1),
                UvIndex = Math.Round(uv, 1),
                SolarIndex = score,
                SolarIndexLabel = _solarIndex.GetLabel(score),
                SolarIndexColor = _solarIndex.GetColor(score),
                Sunrise = ExtractTime(data.Daily.Sunrise[0]),
                Sunset = ExtractTime(data.Daily.Sunset[0]),
                OptimalHours = new List<string> { optimalLabel },
                PeakCostHours = new List<string> { "18:00-21:00" },
                HourlyRadiation = hourlySlots,
                Cached = false
            };
        }

        // ---------- PRONÓSTICO N días (Open-Meteo) ----------
        public async Task<List<SolarDayData>> GetForecastAsync(int days)
        {
            var url = $"https://api.open-meteo.com/v1/forecast" +
                $"?latitude={Inv(LAT)}&longitude={Inv(LON)}" +
                $"&daily=shortwave_radiation_sum,temperature_2m_max,windspeed_10m_max,uv_index_max" +
                $"&timezone=America/Bogota&forecast_days={days}";

            var data = await GetOpenMeteo(url);
            var list = new List<SolarDayData>();
            if (data?.Daily == null) return list;

            for (int i = 0; i < data.Daily.Time.Count; i++)
            {
                var radiation = Math.Round(data.Daily.ShortwaveRadiationSum[i] / 3.6, 2);
                var temp = data.Daily.Temperature2mMax[i];
                var wind = data.Daily.Windspeed10mMax[i];
                var score = _solarIndex.Calculate(radiation, wind, temp);

                list.Add(new SolarDayData
                {
                    Date = ParseDate(data.Daily.Time[i]),
                    RadiationKwhM2 = radiation,
                    TemperatureC = Math.Round(temp, 1),
                    WindSpeedKmh = Math.Round(wind, 1),
                    SolarIndex = score,
                    SolarIndexLabel = _solarIndex.GetLabel(score)
                });
            }
            return list;
        }

        // ---------- HISTÓRICO (NASA POWER) ----------
        public async Task<List<SolarDayData>> GetHistoryAsync(DateTime start, DateTime end)
        {
            var url = $"https://power.larc.nasa.gov/api/temporal/daily/point" +
                $"?parameters=ALLSKY_SFC_SW_DWN,T2M,WS10M&community=RE" +
                $"&longitude={Inv(LON)}&latitude={Inv(LAT)}" +
                $"&start={start:yyyyMMdd}&end={end:yyyyMMdd}&format=JSON";

            var response = await _httpClient.GetStringAsync(url);
            var data = JsonSerializer.Deserialize<NasaResponse>(response, JsonOpts());

            var list = new List<SolarDayData>();
            if (data?.Properties?.Parameter == null) return list;

            var diasValidos = data.Properties.Parameter.ALLSKY_SFC_SW_DWN
                .Where(kv => kv.Value > 0)
                .OrderBy(kv => kv.Key)
                .ToList();

            foreach (var kv in diasValidos)
            {
                var fecha = kv.Key;
                var radiation = kv.Value;
                var temp = data.Properties.Parameter.T2M.GetValueOrDefault(fecha, 30);
                var wind = data.Properties.Parameter.WS10M.GetValueOrDefault(fecha, 5);
                var windKmh = wind > 0 ? wind * 3.6 : 18;
                if (temp <= -100) temp = 30;

                var score = _solarIndex.Calculate(radiation, windKmh, temp);

                list.Add(new SolarDayData
                {
                    Date = DateTime.ParseExact(fecha, "yyyyMMdd", CultureInfo.InvariantCulture),
                    RadiationKwhM2 = Math.Round(radiation, 2),
                    TemperatureC = Math.Round(temp, 1),
                    WindSpeedKmh = Math.Round(windKmh, 1),
                    SolarIndex = score,
                    SolarIndexLabel = _solarIndex.GetLabel(score)
                });
            }
            return list;
        }

        // ---------- PROMEDIO HISTÓRICO (para el agente IA) ----------
        public async Task<double> GetHistoricalAverageAsync(int days = 90)
        {
            var end = DateTime.Today.AddDays(-7);
            var start = end.AddDays(-days);
            var list = await GetHistoryAsync(start, end);
            return list.Any() ? Math.Round(list.Average(d => d.RadiationKwhM2), 2) : 0;
        }

        // ---------- Helpers ----------
        private async Task<OpenMeteoResponse?> GetOpenMeteo(string url)
        {
            var response = await _httpClient.GetStringAsync(url);
            return JsonSerializer.Deserialize<OpenMeteoResponse>(response, JsonOpts());
        }

        private static JsonSerializerOptions JsonOpts() => new() { PropertyNameCaseInsensitive = true };
        private static string Inv(double v) => v.ToString(CultureInfo.InvariantCulture);
        private static DateTime ParseDate(string s) => DateTime.ParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        private static string ExtractTime(string iso)
            => DateTime.TryParse(iso, out var dt) ? dt.ToString("HH:mm") : iso;
    }
}