# Agente Solar — Monorepo

Plataforma de inteligencia energética solar para PYMES en Riohacha, La Guajira. Combina datos satelitales en tiempo real, análisis con LLM y simulación de ROI para convertir el potencial solar de la región en ventaja competitiva concreta.

---

## Estructura del repositorio

```
Olympus/
├── Olympus/          # Backend — ASP.NET Core 10 REST API
├── solar-ai/         # Frontend — Laravel 11 + Tailwind v4 + Vite
├── DOCUMENTACION_API.md
└── README.md
```

---

## Stack

| Capa | Tecnología |
|------|-----------|
| Backend | ASP.NET Core 10 / C# 13 |
| Frontend | Laravel 11, Blade, Tailwind CSS v4, Vite 8 |
| IA | Groq API — LLaMA 3.3 70B Versatile |
| Datos solares | Open-Meteo (tiempo real), NASA POWER (histórico) |
| Mapas | Leaflet.js |
| Gráficas | Chart.js 4 |

---

## Requisitos

- .NET 10 SDK
- PHP 8.2+ con extensiones: `openssl`, `pdo`, `mbstring`, `tokenizer`, `xml`, `ctype`, `json`
- Composer 2+
- Node.js 20+ y npm 10+
- Clave de API de [Groq](https://console.groq.com/)

---

## Configuración rápida

### 1. Backend (API)

```bash
cd Olympus

# Copia la configuración de desarrollo
cp appsettings.Development.json.example appsettings.Development.json
# Edita el archivo y agrega tu GroqApiKey

dotnet restore
dotnet run
# API disponible en https://localhost:7001
```

Variables de entorno requeridas (`appsettings.Development.json`):

```json
{
  "GroqApiKey": "gsk_...",
  "AllowedOrigins": "http://localhost:8000"
}
```

### 2. Frontend

```bash
cd solar-ai

cp .env.example .env
php artisan key:generate

npm install
npm run build

php artisan serve --port=8000
# Frontend disponible en http://localhost:8000
```

Variable de entorno requerida (`.env`):

```
SOLAR_API_BASE=http://localhost:5000
```

---

## Endpoints de la API

| Método | Ruta | Descripción |
|--------|------|-------------|
| `GET` | `/api/solar/today` | Radiación, temperatura y métricas del día |
| `GET` | `/api/solar/forecast` | Pronóstico solar 10 días |
| `GET` | `/api/solar/score` | Índice solar 0–100 con comparativa histórica |
| `GET` | `/api/solar/history` | Serie histórica NASA POWER |
| `POST` | `/api/ai/recommendations` | Recomendaciones energéticas por perfil de negocio |
| `POST` | `/api/ai/alerts` | Alertas críticas/advertencias/info del día |
| `POST` | `/api/ai/insights` | Análisis ejecutivo con insights estratégicos |
| `POST` | `/api/ai/chat` | Consulta conversacional al agente energético |

Ver [`DOCUMENTACION_API.md`](./DOCUMENTACION_API.md) para referencia completa con ejemplos de request/response.

---

## Perfiles de demostración

El frontend incluye cuatro perfiles preconfigurados que modelan clientes reales de Riohacha:

| Perfil | Tipo | Consumo/mes | Tarifa Air-e |
|--------|------|-------------|--------------|
| Hotel Majayura | Comercial | 12,000 kWh | $1,050/kWh |
| Hielera del Caribe | Industrial | 28,000 kWh | $850/kWh |
| Restaurante Sazón Guajira | Comercial | 8,500 kWh | $1,050/kWh |
| Riohacha | Comunidad | 246,000 hab. | $780/kWh |

---

## Contexto del problema

Riohacha recibe **6.5–7.0 kWh/m²/día** de radiación solar — el mayor índice de Colombia. Sin embargo, las PYMES de la región no cuentan con herramientas para optimizar su consumo energético frente a las tarifas de Air-e La Guajira. Agente Solar cierra ese gap con inteligencia artificial contextualizada.

---

## Licencia

MIT
