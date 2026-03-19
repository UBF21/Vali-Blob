# Resilience

ValiBlob integrates with [Polly](https://github.com/App-vNext/Polly) to provide automatic retry, circuit breaker, and timeout policies for all storage operations. Resilience wraps the provider calls underneath the pipeline, so it applies to uploads, downloads, deletes, and all other operations transparently.

---

## Overview

Cloud storage calls are inherently fallible — networks partition, services throttle, and regions experience transient outages. Resilience policies absorb these transient faults without requiring error handling in business code.

The three policies ValiBlob applies, in order:

```
Storage Operation
      │
      ▼
┌──────────────┐
│   Timeout    │  ← Cancels if operation exceeds configured duration
└──────┬───────┘
       │
       ▼
┌──────────────────┐
│ Circuit Breaker  │  ← Fails fast when error rate is too high
└──────┬───────────┘
       │
       ▼
┌──────────────┐
│    Retry     │  ← Retries on transient errors with back-off
└──────┬───────┘
       │
       ▼
  Cloud Provider
```

---

## Retry policy

The retry policy catches transient exceptions (network errors, timeouts, HTTP 5xx) and retries the operation up to `RetryCount` times.

### Exponential back-off with jitter (default)

When `UseExponentialBackoff` is `true`, the delay between retries grows exponentially with added random jitter to prevent the "thundering herd" problem when many clients retry simultaneously.

```
Attempt 1 → fails → wait ~1s
Attempt 2 → fails → wait ~2s
Attempt 3 → fails → wait ~4s
Attempt 4 → fails → propagate exception
```

The jitter adds up to ±10% randomness to each delay interval.

### Linear retry

When `UseExponentialBackoff` is `false`, each retry waits exactly `RetryDelay`:

```
Attempt 1 → fails → wait 1s
Attempt 2 → fails → wait 1s
Attempt 3 → fails → wait 1s
Attempt 4 → fails → propagate exception
```

---

## Circuit breaker

The circuit breaker monitors consecutive failures. When the number of failures in a rolling window reaches `CircuitBreakerThreshold`, the circuit opens and all subsequent calls fail immediately for `CircuitBreakerDuration` — preventing a struggling provider from being flooded with requests.

States:
- **Closed** (normal) — calls pass through
- **Open** — calls fail immediately with a circuit breaker exception (no network call made)
- **Half-open** — after the break duration, one test call is allowed; if it succeeds the circuit closes, otherwise it opens again

---

## Timeout

Each operation is wrapped with a `Timeout` policy. If the provider call exceeds `Timeout`, a `TimeoutRejectedException` is thrown, which the retry policy can then catch and retry.

---

## `ResilienceOptions` reference

| Property | Type | Default | Description |
|---|---|---|---|
| `RetryCount` | `int` | `3` | Maximum number of retry attempts after first failure |
| `RetryDelay` | `TimeSpan` | `00:00:01` (1s) | Base delay between retries |
| `UseExponentialBackoff` | `bool` | `true` | Double the delay on each retry attempt |
| `CircuitBreakerThreshold` | `int` | `5` | Consecutive failures before the circuit opens |
| `CircuitBreakerDuration` | `TimeSpan` | `00:00:30` (30s) | How long the circuit stays open before half-open |
| `Timeout` | `TimeSpan` | `00:01:00` (60s) | Maximum duration per operation attempt |

Configuration section: `ValiBlob:Resilience`

---

## Configuration examples

### Via `appsettings.json`

```json
{
  "ValiBlob": {
    "Resilience": {
      "RetryCount": 5,
      "RetryDelay": "00:00:02",
      "UseExponentialBackoff": true,
      "CircuitBreakerThreshold": 10,
      "CircuitBreakerDuration": "00:01:00",
      "Timeout": "00:02:00"
    }
  }
}
```

### Via code

```csharp
builder.Services
    .AddValiBlob()
    .UseAWS()
    .WithResiliencePolicies(r =>
    {
        r.RetryCount = 5;
        r.RetryDelay = TimeSpan.FromSeconds(2);
        r.UseExponentialBackoff = true;
        r.CircuitBreakerThreshold = 10;
        r.CircuitBreakerDuration = TimeSpan.FromMinutes(1);
        r.Timeout = TimeSpan.FromMinutes(2);
    });
```

### Disabling resilience

To disable all resilience (e.g., in tests where you want immediate failures):

```csharp
.WithResiliencePolicies(r =>
{
    r.RetryCount = 0;
    r.CircuitBreakerThreshold = int.MaxValue;  // effectively disabled
    r.Timeout = TimeSpan.FromMinutes(30);      // high ceiling
})
```

---

## How it interacts with the pipeline

Resilience wraps the **provider call** — the actual network operation — not the pipeline middlewares. This means:

1. The pipeline middlewares (validation, compression, encryption) run once before the first attempt
2. If the provider fails, the retry policy retries **only the network call** with the already-transformed content

This has one important implication for streaming: if the content stream is not seekable, it cannot be retried because the bytes have already been consumed. ValiBlob buffers non-seekable streams into a `MemoryStream` before the first provider call when retries are enabled (`RetryCount > 0`), ensuring the stream can be replayed on retry.

> **💡 Tip:** For very large files where memory buffering is undesirable, use a seekable stream (e.g., `FileStream`) as the upload content. ValiBlob will seek to the beginning on each retry attempt instead of buffering.

---

## Tuning recommendations

| Scenario | Suggestion |
|---|---|
| Interactive API — fast feedback needed | `RetryCount: 2`, `RetryDelay: 0.5s`, `Timeout: 10s` |
| Background job — fault tolerance priority | `RetryCount: 5`, `RetryDelay: 2s`, `Timeout: 120s` |
| Large file upload | Increase `Timeout` significantly (e.g., `10–30 min`) |
| High-traffic system | Lower `CircuitBreakerThreshold` (e.g., `3`) to fail fast |
| Development / local testing | `RetryCount: 0` to see errors immediately |
