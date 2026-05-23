# Agente Solar — Documentación Técnica de la API

> **Versión:** 1.0 · **Stack:** ASP.NET Core 10 + .NET 10 · **Fecha:** Mayo 2026
> **Base URL local:** `http://localhost:5016`

---

## Tabla de contenidos

1. [Descripción general](#1-descripción-general)
2. [Configuración del proyecto](#2-configuración-del-proyecto)
3. [Modelos y DTOs](#3-modelos-y-dtos)
4. [Endpoints de la API](#4-endpoints-de-la-api)
5. [Sistema de IA](#5-sistema-de-ia)
6. [Integraciones externas](#6-integraciones-externas)
7. [Seguridad](#7-seguridad)
8. [Integración con frontend y mobile](#8-integración-con-frontend-y-mobile)
9. [Arquitectura y escalabilidad](#9-arquitectura-y-escalabilidad)

---

## 1. Descripción general

### Objetivo del proyecto

**Agente Solar** es un copiloto energético inteligente diseñado para PYMES y comunidades de **Riohacha, La Guajira, Colombia**. Analiza condiciones solares en tiempo real y genera recomendaciones concretas con horarios y ahorros en pesos colombianos, ayudando a los negocios a reducir su factura eléctrica aprovechando el mayor potencial solar de Colombia.

### Problema que resuelve

| Problema | Dato |
|---|---|
| Tarifa eléctrica alta | Air-e La Guajira: 780–1.050 COP/kWh (comercial) |
| La energía representa hasta el 33% del OpEx de las PYMES | — |
| Apagones frecuentes | ~60 horas/año promedio |
| Potencial solar desaprovechado | 7.0 kWh/m²/día — mayor de Colombia |
| Falta de herramientas de optimización accesibles | Sin dashboards contextualizados para la región |

### Arquitectura general

```
┌──────────────────────┐  ┌──────────────────────┐
│   FRONTEND WEB       │  │   APP MÓVIL (Android) │
│   Laravel + Blade    │  │   Kotlin + Compose    │
│   Puerto: 8000       │  │   Retrofit 2 + OkHttp │
└──────────┬───────────┘  └──────────┬────────────┘
           │ HTTP/JSON               │ HTTP/JSON
           └─────────────┬───────────┘
                         ▼
           ┌─────────────────────────────┐
           │     BACKEND (ASP.NET Core 10)│
           │  SolarController │ AiController│
           │  Puerto: 5016 (HTTP dev)     │
           └──────┬──────────────┬────────┘
                  │              │
                  ▼              ▼
        ┌──────────────┐  ┌──────────────────┐
        │  Open-Meteo  │  │    Groq API       │
        │  (Forecast)  │  │  LLaMA 3.3 70B   │
        └──────────────┘  │  10 claves rot.  │
               │          └──────────────────┘
               ▼
        ┌──────────────┐
        │  NASA POWER  │
        │  (Historia)  │
        └──────────────┘
```

### Flujo completo de una llamada de IA

```
Frontend                Backend                  APIs Externas
   │                       │                          │
   │  POST /ai/recommendations                        │
   │──────────────────────>│                          │
   │                       │──── GetTodayAsync() ────>│ Open-Meteo
   │                       │──── GetHistoricalAverageAsync(90) ──>│ NASA POWER
   │                       │──── GetForecastAsync(3) ────────────>│ Open-Meteo
   │                       │<─── datos solares ───────────────────│
   │                       │                          │
   │                       │── BuildSystemPrompt()    │
   │                       │── BuildCompanyPrompt()   │
   │                       │──── AskJsonAsync() ─────>│ Groq/LLaMA
   │                       │<─── JSON recomendaciones │
   │                       │── ParseRecommendations() │
   │<── { success, data } ─│                          │
```

### Tecnologías utilizadas

| Capa | Tecnología | Versión |
|---|---|---|
| Backend | ASP.NET Core | 10.0 |
| Lenguaje | C# | 13 |
| Frontend | Laravel | 11 |
| UI | Blade + Tailwind CSS | v4 |
| JS | Vanilla ES2024 | — |
| IA | Groq API — LLaMA 3.3 70B | — |
| Forecast solar | Open-Meteo | REST API |
| Historia solar | NASA POWER | REST API |
| Mapas | Leaflet.js | 1.9.4 |
| Gráficas | Chart.js | 4.4.7 |
| Documentación API | Scalar / OpenAPI | 2.14 |

---

## 2. Configuración del proyecto

### Estructura de carpetas — Backend

```
Olympus/
├── Controllers/
│   ├── SolarController.cs      # Endpoints /api/solar/*
│   └── AiController.cs         # Endpoints /api/ai/*
├── Services/
│   ├── SolarService.cs         # Integración Open-Meteo + NASA POWER
│   ├── AiService.cs            # Cliente HTTP hacia Groq con rotación de claves
│   └── SolarIndexService.cs    # Cálculo del Índice Solar (fórmula propia)
├── Models/
│   ├── SolarData.cs            # Datos del día actual
│   ├── SolarDayData.cs         # Un día de forecast o historia
│   ├── SolarForecast.cs        # Pronóstico N días
│   ├── SolarHistory.cs         # Historia NASA POWER
│   ├── SolarScore.cs           # Score del día
│   ├── AiModels.cs             # Todos los DTOs de IA (requests + responses)
│   ├── OpenMeteoResponse.cs    # Deserialización de Open-Meteo
│   └── NasaResponse.cs         # Deserialización de NASA POWER
├── appsettings.json            # Configuración base (sin secrets)
├── appsettings.Development.json # Configuración local (ignorado en .gitignore)
└── Program.cs                  # Bootstrap: DI, CORS, routing
```

### Estructura de carpetas — Frontend

```
solar-ai/
├── resources/
│   ├── views/dashboard.blade.php   # Única vista (SPA)
│   ├── js/dashboard.js             # Toda la lógica JS (1.800+ líneas)
│   └── css/app.css                 # Tailwind v4 + estilos custom
├── routes/web.php                  # Única ruta: GET / → dashboard
├── public/build/                   # Artefactos compilados (Vite)
│   ├── assets/app-*.js
│   └── assets/app-*.css
└── .env                            # Variables de entorno
```

### `appsettings.json` (plantilla pública)

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "Ai": {
    "KEYS": [ "YOUR_GROQ_API_KEY_HERE" ],
    "MODEL": "llama-3.3-70b-versatile",
    "URL": "https://api.groq.com/openai/v1/chat/completions"
  }
}
```

### `appsettings.Development.json` (local, no commit)

```json
{
  "Ai": {
    "KEYS": [
      "gsk_clave_1",
      "gsk_clave_2",
      "gsk_clave_N"
    ],
    "MODEL": "llama-3.3-70b-versatile",
    "URL": "https://api.groq.com/openai/v1/chat/completions"
  }
}
```

> ⚠ **`appsettings.Development.json` nunca debe subirse a repositorios públicos.** Está en `.gitignore`.
>
> El campo `KEYS` acepta un array de claves. `AiService` las itera automáticamente: si una devuelve HTTP 429 (cuota agotada), pasa a la siguiente. Con 10 claves el límite efectivo se multiplica × 10.

### `.env` — Frontend

```dotenv
APP_NAME=Laravel
APP_ENV=local
APP_KEY=base64:...
APP_DEBUG=true
APP_URL=http://localhost

SOLAR_API_BASE=http://localhost:5016
```

La variable `SOLAR_API_BASE` es la única dependencia del frontend hacia el backend. Si el backend se despliega en otra URL, solo cambia esta variable.

### CORS

El backend configura CORS global abierto para facilitar el desarrollo y el acceso desde dispositivos en la misma red local:

```csharp
policy.AllowAnyOrigin();
policy.AllowAnyMethod();
policy.AllowAnyHeader();
```

> ⚠ Para producción se debe restringir a los orígenes del frontend.

### Arrancar el proyecto localmente

**Backend:**
```bash
cd Olympus
dotnet run --launch-profile http
# Escucha en: http://localhost:5016
# Documentación interactiva: http://localhost:5016/scalar/v1
```

**Frontend:**
```bash
cd solar-ai
npm install
npm run build     # Compilar assets
php artisan serve --port=8000
# Dashboard: http://localhost:8000
```

### Dependencias NuGet

| Paquete | Versión | Uso |
|---|---|---|
| `Microsoft.AspNetCore.OpenApi` | 10.0.8 | Generación del schema OpenAPI |
| `Scalar.AspNetCore` | 2.14.14 | UI interactiva de documentación API |

---

## 3. Modelos y DTOs

### `SolarData` — Datos del día actual

Devuelto por `GET /api/solar/today` y usado internamente por los endpoints de IA.

| Campo | Tipo | Descripción |
|---|---|---|
| `date` | `DateTime` | Fecha del registro |
| `location` | `string` | `"Riohacha, La Guajira"` (fijo) |
| `radiationKwhM2` | `double` | Radiación solar diaria (kWh/m²) |
| `temperatureC` | `double` | Temperatura máxima (°C) |
| `windSpeedKmh` | `double` | Velocidad máxima del viento (km/h) |
| `uvIndex` | `double` | Índice UV máximo (0–11+) |
| `solarIndex` | `int` | Score 0–100 (fórmula propia) |
| `solarIndexLabel` | `string` | `"Día Óptimo"` / `"Día Favorable"` / etc. |
| `solarIndexColor` | `string` | `"green"` / `"lime"` / `"yellow"` / `"orange"` / `"red"` |
| `sunrise` | `string` | Hora de amanecer (`"05:27"`) |
| `sunset` | `string` | Hora de atardecer (`"18:09"`) |
| `optimalHours` | `string[]` | Ventana de 4h de máxima radiación (`["10:00-14:00"]`) |
| `peakCostHours` | `string[]` | Hora pico tarifaria Air-e (`["18:00-21:00"]`) (fijo) |
| `hourlyRadiation` | `HourlyRadiationSlot[]` | Radiación real por hora (0–23h) |
| `cached` | `bool` | Siempre `false` en la implementación actual |
| `historicalAvgKwhM2` | `double` | Promedio NASA POWER 90 días (poblado en `/score`) |
| `vsHistoricalPct` | `double` | Diferencia % vs. promedio histórico |

**Ejemplo JSON:**
```json
{
  "date": "2026-05-22",
  "location": "Riohacha, La Guajira",
  "radiationKwhM2": 6.3,
  "temperatureC": 37.0,
  "windSpeedKmh": 23.1,
  "uvIndex": 8.5,
  "solarIndex": 82,
  "solarIndexLabel": "Día Favorable",
  "solarIndexColor": "lime",
  "sunrise": "05:27",
  "sunset": "18:09",
  "optimalHours": ["10:00-14:00"],
  "peakCostHours": ["18:00-21:00"],
  "hourlyRadiation": [
    { "hour": 10, "label": "10:00", "radiationWm2": 850.5, "radiationKwhM2": 0.851 },
    { "hour": 11, "label": "11:00", "radiationWm2": 920.3, "radiationKwhM2": 0.920 }
  ],
  "cached": false
}
```

---

### `SolarDayData` — Un día de pronóstico o historia

Usado como elemento de arrays en `/forecast` y `/history`.

| Campo | Tipo | Descripción |
|---|---|---|
| `date` | `DateTime` | Fecha |
| `radiationKwhM2` | `double` | Radiación solar |
| `temperatureC` | `double` | Temperatura máxima |
| `windSpeedKmh` | `double` | Viento máximo |
| `solarIndex` | `int` | Score 0–100 |
| `solarIndexLabel` | `string` | Etiqueta del índice |

---

### `SolarForecast` — Pronóstico

| Campo | Tipo | Descripción |
|---|---|---|
| `location` | `string` | Riohacha, La Guajira |
| `source` | `string` | `"Open-Meteo Forecast (ECMWF, NOAA, DWD)"` |
| `days` | `int` | Días solicitados |
| `data` | `SolarDayData[]` | Array de días |
| `averageRadiation` | `double` | Promedio del período |
| `maxRadiation` | `double` | Máximo del período |
| `minRadiation` | `double` | Mínimo del período |

---

### `SolarHistory` — Historia NASA POWER

| Campo | Tipo | Descripción |
|---|---|---|
| `location` | `string` | Riohacha, La Guajira |
| `source` | `string` | `"NASA POWER (satellite observations)"` |
| `from` | `string` | Fecha inicio (`"yyyy-MM-dd"`) |
| `to` | `string` | Fecha fin |
| `totalDays` | `int` | Días con datos válidos |
| `averageRadiation` | `double` | Promedio del período |
| `maxRadiation` | `double` | Máximo |
| `minRadiation` | `double` | Mínimo |
| `daily` | `SolarDayData[]` | Datos diarios |
| `monthly` | `MonthlyAggregate[]` | Agregados mensuales |

---

### `SolarScore` — Score del día

| Campo | Tipo | Descripción |
|---|---|---|
| `score` | `int` | 0–100 |
| `label` | `string` | Etiqueta |
| `color` | `string` | Color semáforo |
| `summary` | `string` | Frase de contexto |
| `radiationKwhM2` | `double` | Radiación de hoy |
| `historicalAvgKwhM2` | `double` | Promedio 90d NASA POWER |
| `vsHistoricalPct` | `double` | % diferencia vs. histórico |

---

### `RecommendationRequest` — Request de recomendaciones IA

| Campo | Tipo | Obligatorio | Descripción |
|---|---|---|---|
| `targetType` | `string` | ✅ | `"company"` o `"community"` |
| `name` | `string` | ✅ | Nombre del negocio / comunidad |
| `companyType` | `string?` | Solo en `company` | hotel, hielera, restaurante, retail |
| `monthlyConsumptionKwh` | `double?` | Recomendado | Consumo mensual del negocio |
| `companySize` | `int?` | No | Número de empleados |
| `peakUsageHours` | `string?` | No | Ej: `"08:00-14:00"` |
| `mainLoads` | `string[]?` | No | Cargas principales declaradas |
| `tariffCopKwh` | `double?` | Recomendado | Tarifa eléctrica real del perfil |
| `operatingHoursPerDay` | `double?` | No | Horas de operación |
| `populationEstimate` | `int?` | Solo en `community` | Población estimada |
| `mainProblems` | `string[]?` | Solo en `community` | Problemas energéticos |

---

### `RecommendationResult` — Respuesta de recomendaciones IA

| Campo | Tipo | Descripción |
|---|---|---|
| `targetType` | `string` | Modo usado |
| `name` | `string` | Nombre del perfil |
| `date` | `DateTime` | Fecha de los datos solares |
| `radiationToday` | `double` | Radiación usada para el análisis |
| `solarIndex` | `int` | Índice Solar del día |
| `reasoning` | `string` | Cálculo paso a paso del modelo IA |
| `recommendations` | `Recommendation[]` | 3 acciones concretas |
| `totalSavingsCopDay` | `long` | Suma de ahorros COP/día |
| `totalSavingsCopMonth` | `long` | Proyección mensual (× 30) |
| `alert` | `string?` | Alerta relevante o `null` |

**`Recommendation`:**

| Campo | Tipo | Descripción |
|---|---|---|
| `title` | `string` | Título corto de la acción |
| `description` | `string` | Qué hacer, cuándo y por qué |
| `priority` | `string` | `"alta"` / `"media"` / `"baja"` |
| `timeWindow` | `string` | Horario sugerido (`"10:00 - 14:00"`) |
| `savingsCopDay` | `long` | Ahorro estimado en COP por día |

---

### `SimulateResult` — Respuesta del simulador ROI

| Campo | Tipo | Descripción |
|---|---|---|
| `kwpInstalled` | `double` | Capacidad del sistema |
| `annualGenerationKwh` | `double` | Generación anual estimada |
| `annualSavingsCop` | `double` | Ahorro anual en COP |
| `monthlySavingsCop` | `double` | Ahorro mensual |
| `dailySavingsCop` | `double` | Ahorro diario |
| `investmentCop` | `double` | Costo de instalación estimado |
| `paybackYears` | `double` | Años de retorno de inversión |
| `co2ReductionKgYear` | `double` | CO₂ evitado (kg/año) |
| `coveragePercent` | `double` | % del consumo cubierto |
| `roi25Years` | `double` | ROI a 25 años (%) |
| `profileName` | `string` | Nombre del perfil activo |
| `summary` | `string` | Resumen en lenguaje natural |

---

### `InsightsResult` — Resumen ejecutivo IA

| Campo | Tipo | Descripción |
|---|---|---|
| `headline` | `string` | Titular ejecutivo con cifra concreta |
| `executiveSummary` | `string` | Párrafo de análisis (2–3 oraciones) |
| `insights` | `InsightItem[]` | 3 insights accionables |
| `solarContext` | `string` | Frase del potencial solar del día |
| `generatedAt` | `DateTime` | Timestamp de generación |
| `radiationToday` | `double` | Radiación usada |
| `solarIndex` | `int` | Índice Solar del día |

**`InsightItem`:**

| Campo | Tipo | Descripción |
|---|---|---|
| `icon` | `string` | Emoji representativo |
| `title` | `string` | Título del insight |
| `body` | `string` | Cuerpo explicativo (1–2 oraciones + cifras) |
| `impact` | `string` | `"alto"` / `"medio"` / `"bajo"` |

---

### `AlertsResult` — Alertas predictivas IA

| Campo | Tipo | Descripción |
|---|---|---|
| `alerts` | `EnergyAlert[]` | 4 alertas predictivas |
| `generatedAt` | `DateTime` | Timestamp |
| `solarSummary` | `string` | Resumen solar del día (1 línea) |
| `radiationToday` | `double` | Radiación del día |
| `solarIndex` | `int` | Índice Solar |

**`EnergyAlert`:**

| Campo | Tipo | Descripción |
|---|---|---|
| `type` | `string` | `"critical"` / `"warning"` / `"info"` |
| `icon` | `string` | Emoji representativo |
| `title` | `string` | Título corto |
| `message` | `string` | Explicación con cifras y horarios |
| `action` | `string` | Acción concreta a tomar |
| `timeWindow` | `string` | Franja horaria relevante |

---

## 4. Endpoints de la API

### Formato de respuesta estándar

Todos los endpoints responden con el mismo envelope:

```json
{ "success": true,  "data": { ... } }
{ "success": false, "error": "Descripción del error", "detail": "..." }
```

---

### Solar Controller — `GET /api/solar/today`

**Descripción:** Datos solares del día actual en Riohacha. Combina datos diarios y por hora en una sola llamada a Open-Meteo.

**Parámetros:** Ninguno.

**Lógica interna:**
1. Llama a Open-Meteo con `daily` + `hourly` en una sola request.
2. Calcula `SolarIndex` con la fórmula del `SolarIndexService`.
3. Detecta la ventana óptima de 4h deslizando una ventana sobre los datos horarios entre 6h–17h.
4. Establece hora pico tarifaria como fija `"18:00-21:00"` (Air-e La Guajira).

**Request:**
```
GET http://localhost:5016/api/solar/today
```

**Response exitoso (200):**
```json
{
  "success": true,
  "data": {
    "date": "2026-05-22",
    "location": "Riohacha, La Guajira",
    "radiationKwhM2": 6.3,
    "temperatureC": 37.0,
    "windSpeedKmh": 23.1,
    "uvIndex": 8.5,
    "solarIndex": 82,
    "solarIndexLabel": "Día Favorable",
    "solarIndexColor": "lime",
    "sunrise": "05:27",
    "sunset": "18:09",
    "optimalHours": ["11:00-15:00"],
    "peakCostHours": ["18:00-21:00"],
    "hourlyRadiation": [
      { "hour": 0,  "label": "00:00", "radiationWm2": 0.0,   "radiationKwhM2": 0.0 },
      { "hour": 10, "label": "10:00", "radiationWm2": 850.5, "radiationKwhM2": 0.851 },
      { "hour": 11, "label": "11:00", "radiationWm2": 920.0, "radiationKwhM2": 0.920 }
    ],
    "cached": false
  }
}
```

**Errores:**

| Código | Causa |
|---|---|
| `503` | Open-Meteo no responde |
| `500` | Error interno al procesar datos |

---

### Solar Controller — `GET /api/solar/forecast?days=N`

**Descripción:** Pronóstico solar para los próximos N días (máx. 16).

**Parámetros de query:**

| Param | Tipo | Default | Descripción |
|---|---|---|---|
| `days` | `int` | 15 | Número de días (1–16) |

**Request:**
```
GET http://localhost:5016/api/solar/forecast?days=10
```

**Response exitoso (200):**
```json
{
  "success": true,
  "data": {
    "location": "Riohacha, La Guajira",
    "source": "Open-Meteo Forecast (ECMWF, NOAA, DWD)",
    "days": 10,
    "averageRadiation": 5.8,
    "maxRadiation": 6.5,
    "minRadiation": 3.2,
    "data": [
      {
        "date": "2026-05-22",
        "radiationKwhM2": 6.3,
        "temperatureC": 37.0,
        "windSpeedKmh": 23.1,
        "solarIndex": 82,
        "solarIndexLabel": "Día Favorable"
      }
    ]
  }
}
```

**Errores:**

| Código | Causa |
|---|---|
| `400` | `days` fuera del rango 1–16 |
| `503` | Open-Meteo no responde |

---

### Solar Controller — `GET /api/solar/history`

**Descripción:** Datos históricos de radiación solar desde NASA POWER. Incluye agregados mensuales.

**Parámetros de query:**

| Param | Tipo | Default | Descripción |
|---|---|---|---|
| `from` | `DateTime?` | hace 1 año | Fecha inicio (`yyyy-MM-dd`) |
| `to` | `DateTime?` | hace 7 días | Fecha fin (máx. 5 días antes de hoy) |

> **Nota:** NASA POWER actualiza con retraso de ~7–8 días. El endpoint fuerza automáticamente `to` a máximo `hoy - 7 días`.

**Request:**
```
GET http://localhost:5016/api/solar/history?from=2025-01-01&to=2025-12-31
```

**Response exitoso (200):**
```json
{
  "success": true,
  "data": {
    "location": "Riohacha, La Guajira",
    "source": "NASA POWER (satellite observations)",
    "from": "2025-01-01",
    "to": "2025-12-24",
    "totalDays": 358,
    "averageRadiation": 5.92,
    "maxRadiation": 7.1,
    "minRadiation": 2.8,
    "daily": [ ... ],
    "monthly": [
      {
        "year": 2025,
        "month": 1,
        "monthName": "enero",
        "averageRadiation": 6.2,
        "maxRadiation": 7.0,
        "minRadiation": 4.5,
        "days": 31
      }
    ]
  }
}
```

**Errores:**

| Código | Causa |
|---|---|
| `400` | `from >= to`, rango > 5 años, o datos vacíos |
| `503` | NASA POWER no responde |

---

### Solar Controller — `GET /api/solar/score`

**Descripción:** Score compuesto del día (0–100) más comparativa vs. promedio histórico 90 días. Combina Open-Meteo (hoy) + NASA POWER (histórico) en paralelo.

**Request:**
```
GET http://localhost:5016/api/solar/score
```

**Response exitoso (200):**
```json
{
  "success": true,
  "data": {
    "score": 82,
    "label": "Día Favorable",
    "color": "lime",
    "summary": "Buen día con 6.3 kWh/m². Mueve cargas al pico solar.",
    "radiationKwhM2": 6.3,
    "historicalAvgKwhM2": 5.86,
    "vsHistoricalPct": 7.5
  }
}
```

---

### AI Controller — `POST /api/ai/recommendations`

**Descripción:** Genera 3 recomendaciones de ahorro energético personalizadas usando LLaMA 3.3 70B. Compatible con modo empresa y modo comunidad. Incluye datos solares reales en el prompt.

**Body (modo empresa):**
```json
{
  "targetType": "company",
  "name": "Hotel Majayura",
  "companyType": "hotel",
  "monthlyConsumptionKwh": 12000,
  "companySize": 18,
  "tariffCopKwh": 1050,
  "operatingHoursPerDay": 24,
  "peakUsageHours": "18:00-22:00",
  "mainLoads": [
    "aire acondicionado (30% consumo)",
    "lavandería industrial (8%)",
    "refrigeración cocina (12%)"
  ]
}
```

**Body (modo comunidad):**
```json
{
  "targetType": "community",
  "name": "Riohacha",
  "populationEstimate": 246000,
  "mainProblems": [
    "apagones frecuentes",
    "altos costos eléctricos",
    "subaprovechamiento del potencial solar"
  ]
}
```

**Response exitoso (200):**
```json
{
  "success": true,
  "data": {
    "targetType": "company",
    "name": "Hotel Majayura",
    "date": "2026-05-22",
    "radiationToday": 6.3,
    "solarIndex": 82,
    "reasoning": "Radiación 6.3 kWh/m² — por encima del promedio 90d de 5.86. Hora 11:45 — dentro del horario solar. A/C = 30% consumo (400 kWh × 1050 = 420.000 COP/día)...",
    "recommendations": [
      {
        "title": "Lanzar lavandería industrial AHORA",
        "description": "Activa todas las lavadoras de toallas y sábanas. Con radiación máxima activa hasta las 15:00, el hotel reduce su compra de red.",
        "priority": "alta",
        "timeWindow": "11:45 - 14:00",
        "savingsCopDay": 33600
      },
      {
        "title": "Reducir A/C en pisos vacíos",
        "description": "Sube el termostato de habitaciones sin check-in a 26°C. Ahorra 18 kWh sin afectar comodidad.",
        "priority": "media",
        "timeWindow": "11:45 - 17:30",
        "savingsCopDay": 18900
      },
      {
        "title": "Pre-enfriar cocina antes del pico",
        "description": "Baja el cuarto frío 2°C antes de las 17:30. El compresor descansará entre 18:00-21:00.",
        "priority": "baja",
        "timeWindow": "16:00 - 17:30",
        "savingsCopDay": 8400
      }
    ],
    "totalSavingsCopDay": 60900,
    "totalSavingsCopMonth": 1827000,
    "alert": null
  }
}
```

**Errores:**

| Código | Causa |
|---|---|
| `400` | `name` vacío o `companyType` ausente en modo company |
| `502` | El modelo IA devolvió JSON inválido |
| `503` | Groq API o Open-Meteo/NASA no disponibles |

---

### AI Controller — `POST /api/ai/chat`

**Descripción:** Chat en lenguaje natural con el agente. Responde preguntas energéticas en máximo 3 oraciones con horarios y cifras en COP.

**Body:**
```json
{
  "message": "¿Cuándo debo encender el aire acondicionado hoy?",
  "targetType": "company",
  "name": "Hotel Majayura",
  "companyType": "hotel",
  "monthlyConsumptionKwh": 12000,
  "tariffCopKwh": 1050,
  "mainLoads": ["aire acondicionado (30% consumo)"]
}
```

**Response exitoso (200):**
```json
{
  "success": true,
  "data": {
    "reply": "Con radiación de 6.3 kWh/m² hoy, pre-enfría las zonas comunes entre 11:00 y 15:00 cuando la energía solar es máxima. Reduce el termostato a 24°C en los pisos con mayor ocupación y sube a 26°C los pisos vacíos. Entre 18:00 y 21:00 la tarifa Air-e es máxima — evita encender unidades adicionales en esa franja.",
    "timestamp": "2026-05-22T14:30:00"
  }
}
```

---

### AI Controller — `POST /api/ai/simulate`

**Descripción:** Calculadora de ROI solar. No usa LLM — cálculo matemático puro con constantes de Riohacha.

**Constantes del motor de cálculo:**

| Constante | Valor | Fuente |
|---|---|---|
| Horas de sol pico (HSP) | 5.0 h/día | Promedio histórico Riohacha |
| Eficiencia del sistema | 80% | Pérdidas inversor + temperatura + cables |
| Costo por kWp instalado | 3.500.000 COP | Promedio Colombia 2025 |
| Factor CO₂ | 0.214 kg/kWh | Factor red eléctrica Colombia (UPME) |
| Vida útil referencia | 25 años | Estándar paneles fotovoltaicos |

**Fórmulas:**
```
generacion_anual = kWp × 5.0 HSP × 0.80 × 365
inversion = kWp × 3.500.000
ahorro_anual = generacion_anual × tarifa_cop_kwh
payback = inversion / ahorro_anual
co2_año = generacion_anual × 0.214
cobertura = min(100%, generacion_anual / (consumo_mensual × 12) × 100)
roi_25 = ((ahorro_anual × 25 - inversion) / inversion) × 100
```

**Body:**
```json
{
  "profileName": "Hotel Majayura",
  "targetType": "company",
  "kwpInstalled": 20.0,
  "tariffCopKwh": 1050,
  "monthlyConsumptionKwh": 12000
}
```

**Response exitoso (200):**
```json
{
  "success": true,
  "data": {
    "kwpInstalled": 20.0,
    "annualGenerationKwh": 29200,
    "annualSavingsCop": 30660000,
    "monthlySavingsCop": 2555000,
    "dailySavingsCop": 84000,
    "investmentCop": 70000000,
    "paybackYears": 2.3,
    "co2ReductionKgYear": 6249,
    "coveragePercent": 20.3,
    "roi25Years": 995.0,
    "profileName": "Hotel Majayura",
    "summary": "20.0 kWp cubriría el 20% del consumo de Hotel Majayura. Payback estimado: 2.3 años con radiación promedio de Riohacha."
  }
}
```

---

### AI Controller — `POST /api/ai/insights`

**Descripción:** Resumen ejecutivo diario generado por LLM. Incluye headline, párrafo ejecutivo y 3 insights accionables con nivel de impacto.

**Body:**
```json
{
  "targetType": "company",
  "name": "Hotel Majayura",
  "companyType": "hotel",
  "monthlyConsumptionKwh": 12000,
  "tariffCopKwh": 1050
}
```

**Response exitoso (200):**
```json
{
  "success": true,
  "data": {
    "headline": "Riohacha al 107% del promedio solar — ahorra $60.900 hoy",
    "executiveSummary": "Con 6.3 kWh/m² de radiación hoy, el Hotel Majayura tiene una ventana de alta eficiencia entre 11:00-15:00. Concentrar lavandería industrial y A/C en ese período puede reducir la factura del día en hasta 14%.",
    "insights": [
      {
        "icon": "☀️",
        "title": "Ventana solar activa ahora",
        "body": "La radiación pico de 920 W/m² entre 11:00-13:00 es ideal para activar las cargas más pesadas del hotel sin comprar energía de la red.",
        "impact": "alto"
      },
      {
        "icon": "🌡️",
        "title": "37°C aumenta consumo del A/C",
        "body": "Temperaturas sobre 34°C incrementan el consumo del aire acondicionado hasta un 25%. Pre-enfriar entre 12:00-16:00 evita ese sobrecosto.",
        "impact": "alto"
      },
      {
        "icon": "💰",
        "title": "Payback solar menor a 3 años",
        "body": "Con tarifa de 1.050 COP/kWh y 6.3 kWh/m² de radiación, un sistema de 20 kWp se pagaría solo en 2.3 años.",
        "impact": "medio"
      }
    ],
    "solarContext": "Riohacha hoy: 6.3 kWh/m², 7.5% sobre el promedio histórico 90d",
    "generatedAt": "2026-05-22T14:30:00",
    "radiationToday": 6.3,
    "solarIndex": 82
  }
}
```

---

### AI Controller — `POST /api/ai/alerts`

**Descripción:** 4 alertas energéticas predictivas generadas por LLM, considerando condiciones del día actual y pronóstico de mañana. Siempre incluye mínimo 1 crítica, 2 advertencias y 1 informativa.

**Body:**
```json
{
  "targetType": "company",
  "name": "Hielera del Caribe",
  "companyType": "hielera",
  "monthlyConsumptionKwh": 28000,
  "tariffCopKwh": 850
}
```

**Response exitoso (200):**
```json
{
  "success": true,
  "data": {
    "alerts": [
      {
        "type": "critical",
        "icon": "⚡",
        "title": "Pico tarifario en 3h 15min",
        "message": "Entre las 18:00-21:00 la tarifa Air-e sube hasta un 35%. Con compresores consumiendo 933 kWh/día, el costo en esas 3 horas puede superar $238.000 COP.",
        "action": "Finalizar ciclos de congelación antes de las 17:30",
        "timeWindow": "18:00 - 21:00"
      },
      {
        "type": "warning",
        "icon": "🌡️",
        "title": "Temperatura crítica para compresores",
        "message": "Con 37°C, los compresores de la hielera trabajan un 15-20% más. Cada grado extra sobre 34°C agrega ~140 kWh al consumo diario.",
        "action": "Verificar ventilación del cuarto de máquinas antes de las 14:00",
        "timeWindow": "12:00 - 18:00"
      },
      {
        "type": "warning",
        "icon": "🔋",
        "title": "Mañana cae la radiación",
        "message": "El pronóstico muestra 4.7 kWh/m² para mañana vs 6.3 hoy. Congelar stock extra hoy aprovechando el pico solar actual.",
        "action": "Maximizar producción de hielo entre 10:00-15:00",
        "timeWindow": "10:00 - 15:00"
      },
      {
        "type": "info",
        "icon": "📊",
        "title": "ROI solar favorable",
        "message": "Con tarifa de 850 COP/kWh y consumo de 28.000 kWh/mes, un sistema de 30 kWp tendría un payback de 2.8 años.",
        "action": "Ver simulación detallada en la sección Simulador",
        "timeWindow": "Planificación"
      }
    ],
    "generatedAt": "2026-05-22T14:30:00",
    "solarSummary": "Riohacha: 6.3 kWh/m² hoy, temperatura 37°C, ventana óptima 11:00-15:00",
    "radiationToday": 6.3,
    "solarIndex": 82
  }
}
```

---

## 5. Sistema de IA

### Proveedor y modelo

| Campo | Valor |
|---|---|
| Proveedor | Groq Cloud |
| Modelo | `llama-3.3-70b-versatile` |
| API compatible | OpenAI Chat Completions |
| URL | `https://api.groq.com/openai/v1/chat/completions` |
| Temperature (JSON) | 0.25 (baja: respuestas deterministas) |
| Temperature (chat) | 0.7 (moderada: respuestas conversacionales) |
| Modo JSON | `response_format: { type: "json_object" }` |
| Claves | Array `Ai:KEYS` — rotación automática en 429 |

### Arquitectura de la integración

```
AiService
├── AskJsonAsync(system, user)  → response_format: json_object, temp: 0.25
│   Usado por: recommendations, insights, alerts
└── AskTextAsync(system, user)  → sin format, temp: 0.7
    Usado por: chat

Rotación de claves:
foreach (key in KEYS)
  → si HTTP 429: continuar con siguiente clave
  → si éxito: retornar respuesta
  → si todas agotadas: lanzar excepción
```

### System Prompt — Agente Solar

El system prompt es compartido por todos los endpoints de IA y establece:

1. **Identidad:** Copiloto energético especializado en Riohacha, La Guajira.
2. **Contexto territorial:**
   - Radiación: hasta 7.0 kWh/m²/día
   - Tarifas Air-e: 780–1.050 COP/kWh según segmento
   - Apagones: ~60 h/año
   - Energía: hasta 33% del OpEx PYME
3. **Modos de operación:** `company` (negocio individual) y `community` (escala poblacional).
4. **Reglas de razonamiento:**

   | Condición | Regla |
   |---|---|
   | Radiación > 6.5 kWh/m² | Recomendar mover cargas pesadas al mediodía |
   | Radiación < 5.0 kWh/m² | Advertir menor generación, priorizar ahorro nocturno |
   | Temperatura > 34°C | A/C consume +25%; gestionar horarios de pre-enfriamiento |
   | Hora actual fuera del horario solar | Orientar acciones hacia planificación del día siguiente |
   | Mañana tiene menos radiación | Recomendar concentrar consumo pesado hoy |

5. **Anclaje de ahorros:** El modelo recibe `costo_diario_base`, rangos mínimo/máximo por recomendación (2%–15%) y tope total (40%). La fórmula obligatoria es: `ahorro_kwh × tarifa_real = savings_cop_day`.
6. **Tres ejemplos few-shot** incluidos en el system prompt:
   - Restaurante (hora 14:30, radiación alta)
   - Hotel (hora 12:15, radiación máxima)
   - Hielera (hora 08:30, menor radiación mañana)

### Construcción del User Prompt

**Para `/recommendations` (empresa):**
```
Genera recomendaciones de ahorro energético para esta EMPRESA:
- Nombre: {name}
- Tipo: {companyType}

PERFIL DE CONSUMO DE LA EMPRESA:
- Consumo mensual declarado: 12.000 kWh
- Consumo diario promedio: 400.0 kWh/día
- Tarifa eléctrica real: 1.050 COP/kWh
- Costo diario base: 420.000 COP/día
- Costo mensual estimado: 12.600.000 COP/mes
- Horas de operación diarias: 24 h/día

ANCLAS DE AHORRO (OBLIGATORIAS):
- Mínimo por recomendación: 8.400 COP/día
- Máximo por recomendación: 63.000 COP/día
- Máximo total 3 recomendaciones: 168.000 COP/día

DATOS SOLARES DE HOY (2026-05-22):
- Hora actual en Riohacha: 11:45 (DENTRO del horario solar)
- Radiación: 6.3 kWh/m²/día
- Índice Solar: 82/100 (Día Favorable)
- Temperatura máxima: 37.0°C
- Horas óptimas: 11:00-15:00
- Horas pico tarifario: 18:00-21:00
```

**Variables dinámicas del prompt:**

| Variable | Origen | Ejemplo |
|---|---|---|
| `radiation` | Open-Meteo en tiempo real | `6.3 kWh/m²` |
| `solarIndex` | `SolarIndexService.Calculate()` | `82/100` |
| `temperature` | Open-Meteo | `37.0°C` |
| `optimalHours` | Ventana dinámica 4h | `11:00-15:00` |
| `horaActual` | `DateTime.Now` | `11:45` |
| `horarioSolar` | `hour >= 9 && hour <= 17` | `DENTRO del horario solar` |
| `historicalAvg` | NASA POWER 90 días | `5.86 kWh/m²` |
| `pronostico` | Open-Meteo D+1 y D+2 | `Mañana: 4.7 kWh/m²` |
| `tariff` | Del request del frontend | `1.050 COP/kWh` |
| `dailyCostCop` | Calculado: `(monthly/30) × tariff` | `420.000 COP/día` |

### Procesamiento de la respuesta

El parser (`ParseRecommendations`, `ParseInsights`, `ParseAlerts`) sigue estos pasos:

1. **Limpieza de código Markdown:** Si el LLM envuelve el JSON en ` ```json ... ``` `, extrae el contenido entre `{` y `}`.
2. **Deserialización con `System.Text.Json`:** Usa `JsonDocument.Parse()` para acceso sin tipado fuerte, permitiendo campos opcionales.
3. **Fallback por campo:** Cada campo usa `TryGetProperty()` con un valor por defecto si falta.
4. **Validación mínima:** Si `recommendations.Any()` es `false`, retorna `null` → HTTP 502.

### Estrategia de fallback

| Endpoint | Fallback si la IA falla |
|---|---|
| `/recommendations` | HTTP 503 — el frontend usa datos estáticos del perfil |
| `/insights` | HTTP 503 — el frontend usa `STATIC_INSIGHTS` (3 insights hardcodeados) |
| `/alerts` | HTTP 503 — el frontend usa `STATIC_ALERTS` (4 alertas hardcodeadas) |
| `/chat` | HTTP 503 — el frontend muestra error al usuario |

### Caché en el frontend

Los resultados de IA se cachean **por perfil** en memoria del navegador:

```javascript
const alertsCache  = {};  // { [profileIndex]: { alerts, context } }
const insightsCache = {}; // { [profileIndex]: data }
const recommendationsCache = {}; // { [profileIndex]: data }
```

- La primera visita a cada sección por cada perfil llama a la API.
- Las visitas siguientes sirven del caché.
- El botón 🔄 Actualizar hace `delete cache[activeProfileIndex]` para forzar una nueva llamada.

---

## 6. Integraciones externas

### Open-Meteo

| Campo | Valor |
|---|---|
| URL base | `https://api.open-meteo.com/v1/forecast` |
| Autenticación | Ninguna (API gratuita) |
| Rate limit | Sin límite documentado para uso razonable |
| Latencia típica | 200–400 ms |

**Coordenadas fijas:**
```csharp
private const double LAT = 11.5444;  // Riohacha, La Guajira
private const double LON = -72.9072;
```

**Parámetros `daily` (hoy + pronóstico):**

| Parámetro | Descripción |
|---|---|
| `shortwave_radiation_sum` | Radiación solar total del día (W·h/m²) → dividir entre 3600 para kWh/m² |
| `temperature_2m_max` | Temperatura máxima a 2m |
| `temperature_2m_min` | Temperatura mínima a 2m |
| `windspeed_10m_max` | Velocidad máxima del viento a 10m |
| `uv_index_max` | Índice UV máximo del día |
| `sunrise` | ISO datetime del amanecer |
| `sunset` | ISO datetime del atardecer |

**Parámetros `hourly` (solo en `/today`):**

| Parámetro | Descripción |
|---|---|
| `shortwave_radiation` | Radiación instantánea (W/m²) por hora |

**Conversión de unidades:**
```csharp
var radiation = Math.Round(data.Daily.ShortwaveRadiationSum[0] / 3.6, 2);
// Open-Meteo entrega W·h/m²; dividir entre 3.6 convierte a kWh/m²
```

**Manejo de errores:** `HttpRequestException` → HTTP 503 con mensaje descriptivo.

---

### NASA POWER

| Campo | Valor |
|---|---|
| URL base | `https://power.larc.nasa.gov/api/temporal/daily/point` |
| Autenticación | Ninguna (API pública) |
| Rate limit | No documentado; recomendado < 30 requests/min |
| Latencia típica | 1–4 segundos |
| Retraso de datos | ~7–8 días (procesamiento satelital) |

**Parámetros de la request:**

| Parámetro | Valor |
|---|---|
| `parameters` | `ALLSKY_SFC_SW_DWN,T2M,WS10M` |
| `community` | `RE` (Renewable Energy) |
| `longitude` | `-72.9072` |
| `latitude` | `11.5444` |
| `start` | `yyyyMMdd` |
| `end` | `yyyyMMdd` |
| `format` | `JSON` |

**Parámetros NASA:**

| Campo NASA | Descripción | Conversión |
|---|---|---|
| `ALLSKY_SFC_SW_DWN` | Radiación solar (kWh/m²/día) | Directa |
| `T2M` | Temperatura media a 2m (°C) | Directa |
| `WS10M` | Velocidad del viento a 10m (m/s) | × 3.6 → km/h |

**Filtrado de datos inválidos:**
```csharp
var diasValidos = data.Properties.Parameter.ALLSKY_SFC_SW_DWN
    .Where(kv => kv.Value > 0)   // NASA envía -999 para días sin datos
    .OrderBy(kv => kv.Key)
    .ToList();
// Temperatura: si temp <= -100, reemplazar por 30°C
```

---

### Groq API

| Campo | Valor |
|---|---|
| URL | `https://api.groq.com/openai/v1/chat/completions` |
| Modelo | `llama-3.3-70b-versatile` |
| Autenticación | `Bearer {GROQ_API_KEY}` en header |
| Rate limit | Tier gratuito: 30 req/min, 6.000 TPM, 14.400 req/día por clave |
| Latencia típica | 500–1500 ms |

**Request body para modo JSON:**
```json
{
  "model": "llama-3.3-70b-versatile",
  "messages": [
    { "role": "system", "content": "..." },
    { "role": "user",   "content": "..." }
  ],
  "temperature": 0.25,
  "response_format": { "type": "json_object" }
}
```

**Extracción de la respuesta:**
```csharp
var text = doc.RootElement
    .GetProperty("choices")[0]
    .GetProperty("message")
    .GetProperty("content")
    .GetString();
```

---

## 7. Seguridad

### Manejo de claves

| Clave | Almacenamiento | Exposición |
|---|---|---|
| `Ai:KEYS` (array) | `appsettings.Development.json` | Solo local. Nunca en `appsettings.json` (repo público). |
| `APP_KEY` (Laravel) | `.env` | Solo en servidor. Nunca en código. |

**Recomendaciones para producción:**
- Usar Azure Key Vault / AWS Secrets Manager / variables de entorno del servidor.
- Rotar las claves Groq periódicamente.
- Añadir `appsettings.Development.json` y `.env` al `.gitignore`.

### CORS

En desarrollo: `AllowAnyOrigin()` para facilitar el acceso desde distintos dispositivos en la red local.

**Para producción — reemplazar con:**
```csharp
policy.WithOrigins("https://tu-dominio.com")
      .AllowAnyMethod()
      .AllowAnyHeader();
```

### HTTPS

En desarrollo, `UseHttpsRedirection` está desactivado intencionalmente para permitir acceso por HTTP en la red local:
```csharp
if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();
```
En producción siempre habilitar HTTPS.

### Validaciones de entrada

| Endpoint | Validaciones activas |
|---|---|
| `/recommendations` | `name` requerido; `companyType` requerido en modo company |
| `/simulate` | `kwpInstalled` entre 0.5–500; `tariffCopKwh` > 0 |
| `/history` | `from < to`; rango máximo 5 años; `to` máximo hoy - 5 días |
| `/forecast` | `days` entre 1–16 |

### Manejo de errores

Todos los errores siguen el formato:
```json
{ "success": false, "error": "Descripción", "detail": "Mensaje técnico" }
```

| Código | Significado |
|---|---|
| `400` | Validación de parámetros fallida |
| `500` | Error interno no esperado |
| `502` | IA devolvió formato inválido |
| `503` | API externa no disponible (Groq, Open-Meteo, NASA) |

---

## 8. Integración con frontend y mobile

### App móvil — AppSolarMovil (Android)

Repositorio: [`angel127633/AppSolarMovil`](https://github.com/angel127633/AppSolarMovil)

La app Android consume los mismos endpoints REST que el frontend web. Está construida en **Kotlin + Jetpack Compose** con arquitectura MVVM.

**Configuración de la URL base** (`RetrofitClient.kt`):
```kotlin
Retrofit.Builder()
    .baseUrl("http://<IP_LOCAL>:5016/api/")  // IP de la máquina que corre el backend
    .addConverterFactory(GsonConverterFactory.create())
    .client(OkHttpClient.Builder()
        .connectTimeout(40, TimeUnit.SECONDS)
        .readTimeout(40, TimeUnit.SECONDS)
        .writeTimeout(40, TimeUnit.SECONDS)
        .build())
    .build()
    .create(ApiService::class.java)
```

> Usar la IP local de la red (ej. `192.168.1.X`), no `localhost`, para que el dispositivo físico o emulador pueda alcanzar el backend. El manifesto tiene `android:usesCleartextTraffic="true"` habilitado para desarrollo HTTP.

**Endpoints consumidos por la app:**

| Método | Endpoint | ViewModel | Pantalla |
|--------|----------|-----------|---------|
| `GET` | `/solar/today` | `SolarViewModel` | Dashboard |
| `GET` | `/solar/score` | `SolarViewModel` | Dashboard |
| `GET` | `/solar/forecast?days=16` | `ForecastViewModel` | Dashboard |
| `POST` | `/ai/recommendations` | `ViewModelRecomendations` | Dashboard |
| `POST` | `/ai/chat` | `ChatIAViewModel` | Chat IA |

**Pantallas:**

| Tab | Pantalla | Descripción |
|-----|----------|-------------|
| 0 | `DashBoardScreen` | Índice solar, radiación, UV, temperatura, pronóstico y recomendaciones IA |
| 1 | `ChaIAScreen` | Chat conversacional con el asistente energético |

**Indicador solar (igual que el backend):**

| Rango | Etiqueta | Color |
|-------|----------|-------|
| 0–30 | Bajo | Rojo |
| 31–60 | Medio | Amarillo |
| 61–80 | Alto | Verde |
| 81–100 | Excelente | Verde brillante |

---

### Mapa de endpoints por widget del Dashboard

| Widget / Pantalla | Endpoint | Frecuencia |
|---|---|---|
| Hero (radiación, índice, temp, viento) | `GET /api/solar/today` | Al cargar + cada 5 min |
| Predicción 10 días | `GET /api/solar/forecast?days=10` | Al cargar |
| Score del día | `GET /api/solar/score` | Al cargar |
| Ventana solar (amanecer, atardecer, óptimo) | `GET /api/solar/today` | Mismo request |
| Radiación por hora (5 franjas) | `GET /api/solar/today` → `hourlyRadiation` | Mismo request |
| Índice UV | `GET /api/solar/today` → `uvIndex` | Mismo request |
| Consumo estimado | Calculado en frontend con datos del perfil | — |
| Gráfica de consumo | Calculado en frontend | — |
| Recomendaciones IA | `POST /api/ai/recommendations` | Al cargar + cambio de perfil |
| Chat IA | `POST /api/ai/chat` | On demand |
| Análisis IA | `POST /api/ai/insights` | Primera visita por perfil |
| Alertas | `POST /api/ai/alerts` | Primera visita por perfil |
| Historial (gráfica) | `GET /api/solar/history` | Primera visita |
| Simulador | `POST /api/ai/simulate` | Calculado en frontend (sin API) |
| Mapa | Leaflet.js + tiles Carto | Al cargar |

### Estructura de una llamada típica en JS

```javascript
// Configuración base
const API_BASE = document.body.dataset.apiBase; // del atributo data del <body>

const fetchJson = async (path, options = {}) => {
    const response = await fetch(`${API_BASE}${path}`, {
        headers: { 'Accept': 'application/json', 'Content-Type': 'application/json' },
        ...options
    });
    const data = await response.json();
    if (!response.ok || data.success === false)
        throw new Error(data.error || `HTTP ${response.status}`);
    return data.data; // extrae siempre el campo data del envelope
};
```

### Llamadas en paralelo al cargar el dashboard

```javascript
const [today, forecast, score] = await Promise.all([
    fetchJson('/api/solar/today'),
    fetchJson('/api/solar/forecast?days=10'),
    fetchJson('/api/solar/score')
]);
```

### Caché de resultados de IA (por perfil)

```javascript
const recommendationsCache = {}; // { [profileIndex]: data }
const insightsCache = {};        // { [profileIndex]: data }
const alertsCache = {};          // { [profileIndex]: { alerts, context } }

// Patrón de acceso
const loadInsights = async () => {
    const idx = activeProfileIndex;
    if (insightsCache[idx]) {
        renderInsights(insightsCache[idx]); // Sirve del caché
        return;
    }
    const data = await fetchJson('/api/ai/insights', { method: 'POST', body: JSON.stringify(profile) });
    insightsCache[idx] = data; // Guarda en caché
    renderInsights(data);
};
```

### Estrategia de actualización

| Dato | Estrategia |
|---|---|
| Datos solares | `setInterval(loadDashboard, 5 * 60 * 1000)` — cada 5 min |
| Recomendaciones IA | Al cambiar de perfil (con caché por perfil) |
| Insights / Alertas | Primera visita por perfil (caché persistente en sesión) |
| Historial | Una vez por sesión (datos del pasado no cambian) |

### Para integración mobile

La API es **REST puro con JSON** — compatible con cualquier cliente HTTP. No requiere SDK ni librerías especiales.

**Headers requeridos:**
```
Content-Type: application/json
Accept: application/json
```

**Base URL:** Configurable. En desarrollo: `http://{IP_LOCAL}:5016`. En producción: la URL del servidor.

**Ejemplo cURL — obtener datos del día:**
```bash
curl http://localhost:5016/api/solar/today \
  -H "Accept: application/json"
```

**Ejemplo cURL — recomendaciones IA:**
```bash
curl -X POST http://localhost:5016/api/ai/recommendations \
  -H "Content-Type: application/json" \
  -d '{
    "targetType": "company",
    "name": "Hotel Majayura",
    "companyType": "hotel",
    "monthlyConsumptionKwh": 12000,
    "tariffCopKwh": 1050
  }'
```

---

## 9. Arquitectura y escalabilidad

### Organización por capas

```
Presentation Layer
└── Controllers (SolarController, AiController)
    ├── Validación de input
    ├── Orquestación de llamadas al servicio
    └── Formateo de respuesta (envelope success/data/error)

Business Layer
└── Services
    ├── SolarService      — integración APIs externas, transformación de datos
    ├── AiService          — cliente HTTP para Groq con rotación de claves
    └── SolarIndexService  — lógica del Índice Solar (singleton, sin estado)

Data Layer (externo)
├── Open-Meteo API   — datos meteorológicos en tiempo real y pronóstico
├── NASA POWER API   — serie histórica satelital
└── Groq / LLaMA     — generación de texto con contexto solar
```

### Inyección de dependencias

| Servicio | Lifetime | Justificación |
|---|---|---|
| `SolarIndexService` | `Singleton` | Solo contiene fórmulas matemáticas sin estado |
| `SolarService` | `Scoped` | Una instancia por request HTTP |
| `AiService` | `Scoped` | Una instancia por request HTTP |
| `HttpClient` | Factory (`AddHttpClient`) | Gestión del pool de conexiones TCP |

### Índice Solar — Fórmula propia

```csharp
score = Clamp(
    (radiation / 7.0) * 70 +        // Radiación: hasta 70 puntos (máx ~7 kWh/m²)
    (windKmh > 20 ? 15 : 10) +      // Viento: 15 pts si hay viento, 10 si no
    (temp > 34 ? 8 : 15),           // Temperatura: 8 pts si > 34°C, 15 si no
    0, 100
)
```

| Score | Etiqueta | Color |
|---|---|---|
| ≥ 85 | Día Óptimo | green |
| ≥ 65 | Día Favorable | lime |
| ≥ 45 | Día Moderado | yellow |
| ≥ 25 | Día Bajo | orange |
| < 25 | Día Crítico | red |

### Mejoras futuras recomendadas

| Área | Mejora | Prioridad |
|---|---|---|
| **Caché backend** | Agregar `IMemoryCache` con TTL de 15 min para `/today` y `/forecast` | Alta |
| **Autenticación** | JWT Bearer para endpoints privados en producción | Alta |
| **Rate limiting** | Middleware para limitar llamadas a `/ai/*` (evitar abuso del tier Groq) | Alta |
| **CORS producción** | Restringir a orígenes específicos | Alta |
| **Secretos** | Migrar `Groq:KEY` a variable de entorno o Key Vault | Alta |
| **Logging estructurado** | Integrar Serilog con correlación de IDs por request | Media |
| **Métricas** | Tiempo de respuesta por endpoint, tasa de éxito IA | Media |
| **Ubicación dinámica** | Parametrizar LAT/LON para soportar múltiples ciudades | Media |
| **WebSockets** | Push de actualizaciones solares al frontend sin polling | Baja |
| **Base de datos** | Persistir histórico de recomendaciones por perfil | Baja |

### Recomendaciones para despliegue en producción

```
[ Azure App Service / Railway / Render ]
├── Backend (.NET 10):
│   ├── ASPNETCORE_ENVIRONMENT=Production
│   ├── Groq__KEY= (variable de entorno, no en código)
│   └── HTTPS obligatorio
└── Frontend (Laravel):
    ├── SOLAR_API_BASE=https://api.tudominio.com
    ├── php artisan config:cache
    └── npm run build (artefactos en public/build/)
```

**Resumen de puertos:**

| Servicio | Desarrollo | Producción |
|---|---|---|
| Backend API | `http://localhost:5016` | HTTPS :443 |
| Frontend | `http://localhost:8000` | HTTPS :443 |
| Documentación Scalar | `http://localhost:5016/scalar/v1` | Desactivar en prod |

---

*Documentación generada con código real del proyecto Agente Solar · Hackathon 2026 · Riohacha, La Guajira*
