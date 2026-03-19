# Resiliencia

ValiBlob incluye soporte integrado para políticas de resiliencia mediante Polly: reintentos con backoff exponencial, circuit breaker y timeout. Estas políticas se aplican automáticamente a todas las operaciones del proveedor.

---

## Integración con Polly

Las políticas de resiliencia se configuran a través de `ResilienceOptions` y se aplican como una capa que envuelve todas las llamadas al proveedor cloud. No necesitás escribir código adicional — sólo configurar las opciones.

```csharp
builder.Services
    .AddValiBlob()
    .UseAWS()
    .WithResiliencePolicies(opts =>
    {
        opts.RetryCount = 3;
        opts.RetryDelay = TimeSpan.FromSeconds(1);
        opts.UseExponentialBackoff = true;
        opts.CircuitBreakerThreshold = 5;
        opts.CircuitBreakerDuration = TimeSpan.FromSeconds(30);
        opts.Timeout = TimeSpan.FromSeconds(60);
    })
    .WithDefaultProvider("AWS");
```

---

## Política de reintentos

El reintento automático protege tu aplicación de fallas transitorias: picos de latencia, errores temporales de red, throttling del proveedor cloud.

### Parámetros

| Parámetro | Descripción |
|---|---|
| `RetryCount` | Número máximo de reintentos. `0` desactiva los reintentos |
| `RetryDelay` | Tiempo base de espera entre reintentos |
| `UseExponentialBackoff` | Si es `true`, el delay se duplica con cada intento (con jitter aleatorio) |

### Comportamiento con backoff exponencial

Con `RetryCount = 3`, `RetryDelay = 1s` y `UseExponentialBackoff = true`:

| Intento | Delay aproximado |
|---|---|
| 1 (original) | 0s |
| 2 (primer reintento) | ~1s |
| 3 (segundo reintento) | ~2s |
| 4 (tercer reintento) | ~4s |

El jitter aleatorio (±20%) evita que múltiples instancias de la aplicación reintenten exactamente al mismo tiempo (thundering herd problem).

### Configuración conservadora para producción

```csharp
opts.RetryCount = 3;
opts.RetryDelay = TimeSpan.FromSeconds(2);
opts.UseExponentialBackoff = true;
```

### Sin reintentos (para operaciones idempotentes que no deberían reintentar)

```csharp
opts.RetryCount = 0; // desactiva reintentos
```

---

## Circuit Breaker

El circuit breaker evita que tu aplicación bombardee un servicio que ya está fallando. Después de N fallas consecutivas, el circuito se "abre" y las siguientes llamadas fallan inmediatamente sin llegar al proveedor, durante el período de corte.

### Estados del circuit breaker

```
CERRADO ──N fallas──> ABIERTO ──duración──> SEMIABIERTO ──éxito──> CERRADO
(normal)              (cortando)             (probando)              (normal)
                                                 │
                                              falla
                                                 │
                                              ABIERTO
```

### Parámetros

| Parámetro | Descripción |
|---|---|
| `CircuitBreakerThreshold` | Número de fallas consecutivas para abrir el circuito |
| `CircuitBreakerDuration` | Tiempo que el circuito permanece abierto antes de pasar a semi-abierto |

### Configuración ejemplo

```csharp
opts.CircuitBreakerThreshold = 5;                        // abrir después de 5 fallas
opts.CircuitBreakerDuration = TimeSpan.FromSeconds(30);  // esperar 30s antes de probar de nuevo
```

### Comportamiento cuando el circuito está abierto

Cuando el circuit breaker está abierto, las llamadas fallan inmediatamente con `StorageErrorCode.ProviderError` y un mensaje que indica que el circuito está abierto. Esto permite a tu aplicación responder rápidamente (ej: servir desde caché, retornar un error 503) en lugar de esperar timeouts.

---

## Timeout

El timeout limita el tiempo máximo que una operación puede tardar. Si se excede, la operación se cancela y retorna `StorageErrorCode.Timeout`.

```csharp
opts.Timeout = TimeSpan.FromSeconds(60); // 60 segundos máximo por operación
```

> **💡 Tip:** Para uploads de archivos grandes, considerá usar un timeout más largo o `TimeSpan.FromMinutes(10)`. El timeout se aplica por operación completa, incluyendo los reintentos.

> **⚠️ Advertencia:** El timeout de resiliencia actúa sobre la operación completa (incluyendo reintentos). Si tenés `RetryCount = 3` y `Timeout = 60s`, los tres reintentos deben completarse dentro de esos 60 segundos.

---

## Referencia completa de `ResilienceOptions`

| Propiedad | Tipo | Default | Descripción |
|---|---|---|---|
| `RetryCount` | `int` | `3` | Número de reintentos automáticos |
| `RetryDelay` | `TimeSpan` | `00:00:01` (1 segundo) | Delay base entre reintentos |
| `UseExponentialBackoff` | `bool` | `true` | Si es `true`, el delay crece exponencialmente con jitter |
| `CircuitBreakerThreshold` | `int` | `5` | Fallas consecutivas para abrir el circuito |
| `CircuitBreakerDuration` | `TimeSpan` | `00:00:30` (30 segundos) | Tiempo que el circuito permanece abierto |
| `Timeout` | `TimeSpan` | `00:01:00` (60 segundos) | Timeout por operación |

---

## Configuración via appsettings.json

```json
{
  "ValiBlob": {
    "Resilience": {
      "RetryCount": 3,
      "RetryDelay": "00:00:01",
      "UseExponentialBackoff": true,
      "CircuitBreakerThreshold": 5,
      "CircuitBreakerDuration": "00:00:30",
      "Timeout": "00:01:00"
    }
  }
}
```

---

## Ejemplos de configuración por escenario

### Aplicación de alta disponibilidad (crítica)

```csharp
.WithResiliencePolicies(opts =>
{
    opts.RetryCount = 5;
    opts.RetryDelay = TimeSpan.FromSeconds(2);
    opts.UseExponentialBackoff = true;
    opts.CircuitBreakerThreshold = 10;
    opts.CircuitBreakerDuration = TimeSpan.FromSeconds(60);
    opts.Timeout = TimeSpan.FromMinutes(2);
})
```

### Aplicación con SLA bajo (puede fallar rápido)

```csharp
.WithResiliencePolicies(opts =>
{
    opts.RetryCount = 1;
    opts.RetryDelay = TimeSpan.FromMilliseconds(500);
    opts.UseExponentialBackoff = false;
    opts.CircuitBreakerThreshold = 3;
    opts.CircuitBreakerDuration = TimeSpan.FromSeconds(15);
    opts.Timeout = TimeSpan.FromSeconds(10);
})
```

### Background job (puede esperar)

```csharp
.WithResiliencePolicies(opts =>
{
    opts.RetryCount = 10;
    opts.RetryDelay = TimeSpan.FromSeconds(5);
    opts.UseExponentialBackoff = true;
    opts.CircuitBreakerThreshold = 5;
    opts.CircuitBreakerDuration = TimeSpan.FromMinutes(2);
    opts.Timeout = TimeSpan.FromMinutes(10);
})
```

---

## Cómo interactúa con el pipeline

Las políticas de resiliencia se aplican **después** del pipeline de middleware, justo antes de la llamada al SDK del proveedor. El orden completo de ejecución es:

```
UploadAsync(request)
   │
   ├─ ValidationMiddleware
   ├─ CompressionMiddleware
   ├─ EncryptionMiddleware
   └─ [ResiliencePolicy]
         ├─ Timeout policy
         ├─ Circuit breaker policy
         └─ Retry policy
               └─ SDK call (AWS S3 / Azure / GCP / ...)
```

Esto significa:
- El pipeline de middleware se ejecuta una sola vez
- Sólo la llamada al SDK del proveedor se reintenta
- El contenido transformado (comprimido/cifrado) se reutiliza en los reintentos sin re-procesar
