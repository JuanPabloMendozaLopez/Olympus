# Agente Solar — Monorepo

Plataforma de inteligencia energética solar para PYMES en Riohacha, La Guajira. Combina datos satelitales en tiempo real, análisis con LLM y simulación de ROI para convertir el potencial solar de la región en ventaja competitiva concreta.

---

## Estructura del repositorio

```
Olympus/
├── Olympus/          # Backend — ASP.NET Core 10 REST API
├── solar-ai/         # Frontend web — Laravel 11 + Tailwind v4 + Vite
├── app-movil/        # App Android — Kotlin + Jetpack Compose
├── DOCUMENTACION_API.md
└── README.md
```

> Los tres componentes (`solar-ai` y `app-movil`) están registrados como **submódulos git**.
> Para clonar todo de una vez:
> ```bash
> git clone --recurse-submodules https://github.com/JuanPabloMendozaLopez/Olympus
> ```

---

## Stack

| Capa | Tecnología |
|------|-----------|
| Backend | ASP.NET Core 10 / C# 13 |
| Frontend web | Laravel 11, Blade, Tailwind CSS v4, Vite 8 |
| App móvil | Kotlin, Jetpack Compose, Material 3 |
| IA | Groq API — LLaMA 3.3 70B Versatile (rotación de 10 claves) |
| Datos solares | Open-Meteo (tiempo real + pronóstico), NASA POWER (histórico) |
| Red (móvil) | Retrofit 2 + OkHttp |
| Mapas | Leaflet.js |
| Gráficas | Chart.js 4 |

---

## Requisitos

**Backend:**
- .NET 10 SDK

**Frontend web:**
- PHP 8.2+ con extensiones: `openssl`, `pdo`, `mbstring`, `tokenizer`, `xml`, `ctype`, `json`
- Composer 2+
- Node.js 20+ y npm 10+

**App móvil:**
- Android Studio Hedgehog o superior
- Android SDK 24+ (Android 7.0 mínimo)

**IA:**
- Una o más claves de API de [Groq](https://console.groq.com/) (gratuitas)

---

## Configuración rápida

### 1. Backend (API)

```bash
cd Olympus

dotnet restore
dotnet run --launch-profile http
# API disponible en http://localhost:5016
# Documentación interactiva: http://localhost:5016/scalar/v1
```

Crea `Olympus/appsettings.Development.json` (no se sube al repo):

```json
{
  "Ai": {
    "KEYS": [
      "gsk_tu_clave_1",
      "gsk_tu_clave_2"
    ],
    "MODEL": "llama-3.3-70b-versatile",
    "URL": "https://api.groq.com/openai/v1/chat/completions"
  }
}
```

> El sistema rota automáticamente entre las claves cuando una alcanza el límite de peticiones (HTTP 429). Con múltiples claves se multiplica la capacidad efectiva.

### 2. Frontend web

```bash
cd solar-ai

cp .env.example .env
php artisan key:generate

npm install
npm run build

php artisan serve --port=8000
# Dashboard disponible en http://localhost:8000
```

Variable requerida en `.env`:

```
SOLAR_API_BASE=http://localhost:5016
```

### 3. App móvil (Android)

1. Abre `app-movil/AppSolar/` en Android Studio
2. Edita `app/src/main/java/com/example/appsolar/Model/RetrofitClient.kt` y cambia la IP:
   ```kotlin
   .baseUrl("http://<IP_DE_TU_PC>:5016/api/")
   ```
   > Usa la IP local de tu máquina (no `localhost`) para que el dispositivo físico o emulador pueda alcanzar el backend.
3. Conecta un dispositivo con Android 7.0+ o inicia un emulador (API 24+)
4. Ejecuta `Run > Run 'app'`

---

## Endpoints de la API

| Método | Ruta | Descripción |
|--------|------|-------------|
| `GET` | `/api/solar/today` | Radiación, temperatura, UV, amanecer/atardecer y horas óptimas |
| `GET` | `/api/solar/forecast?days=N` | Pronóstico solar hasta 16 días |
| `GET` | `/api/solar/score` | Índice solar 0–100 con comparativa histórica 90 días |
| `GET` | `/api/solar/history` | Serie histórica NASA POWER con agregados mensuales |
| `POST` | `/api/ai/recommendations` | Recomendaciones energéticas por perfil de negocio o comunidad |
| `POST` | `/api/ai/alerts` | Alertas críticas/advertencias/info del día |
| `POST` | `/api/ai/insights` | Análisis ejecutivo con insights estratégicos |
| `POST` | `/api/ai/chat` | Consulta conversacional al agente energético |
| `POST` | `/api/ai/simulate` | Simulador de ROI solar (cálculo matemático, sin LLM) |

Ver [`DOCUMENTACION_API.md`](./DOCUMENTACION_API.md) para referencia completa con ejemplos de request/response.

---

## Perfiles de demostración

El frontend y la app incluyen cuatro perfiles preconfigurados que modelan clientes reales de Riohacha:

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
