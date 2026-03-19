# Changelog

All notable changes to ValiBlob will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased]

---

## [1.0.0] - 2026-03-17

### Added

#### Core abstractions
- `IStorageProvider` interface as the unified abstraction over all cloud storage backends
- `StorageResult<T>` discriminated result type for explicit, exception-free error handling across all operations
- `IResumableUploadProvider` interface for TUS-style chunked uploads with MD5 checksum validation
- `IPresignedUrlProvider` interface for generating time-limited signed URLs for upload and download

#### Storage providers
- **ValiBlob.AWS** — AWS S3 provider with full `IStorageProvider`, `IResumableUploadProvider`, and `IPresignedUrlProvider` support
- **ValiBlob.Azure** — Azure Blob Storage provider with full `IStorageProvider`, `IResumableUploadProvider`, and `IPresignedUrlProvider` support
- **ValiBlob.GCP** — Google Cloud Storage provider with full `IStorageProvider`, `IResumableUploadProvider`, and `IPresignedUrlProvider` support
- **ValiBlob.OCI** — Oracle Cloud Infrastructure Object Storage provider with full `IStorageProvider`, `IResumableUploadProvider`, and `IPresignedUrlProvider` support
- **ValiBlob.Supabase** — Supabase Storage provider with full `IStorageProvider`, `IResumableUploadProvider`, and `IPresignedUrlProvider` support
- **ValiBlob.Testing** — `InMemoryStorageProvider` implementing all provider interfaces for unit and integration testing

#### Middleware pipeline
- Composable `IStorageMiddleware` pipeline executed on every upload and download
- `ValidationMiddleware` — validates file size, MIME type, and extension before storage operations
- `CompressionMiddleware` — transparent GZip/Deflate compression and decompression
- `EncryptionMiddleware` — AES-based encryption and decryption at the pipeline layer

#### Event system
- `StorageEventDispatcher` for publishing and subscribing to upload, download, and delete lifecycle events

#### Observability
- OpenTelemetry integration with a named `ActivitySource` for distributed tracing of all storage operations
- `Meter` with operation counters (uploads, downloads, deletes) and latency histograms

#### Resilience
- Polly-based retry policy with configurable attempts and backoff
- Polly circuit breaker policy to prevent cascading failures
- Polly timeout policy for bounding long-running storage operations

#### Health checks
- **ValiBlob.HealthChecks** — ASP.NET Core `IHealthCheck` implementation for connectivity checks on registered providers, compatible with the standard `/health` endpoint

#### Developer experience
- Fluent DI registration via `AddValiBlob()` with per-provider extension methods (`UseAWS`, `UseAzure`, `UseGCP`, `UseOCI`, `UseSupabase`)
- Full documentation in English (`docs/en/`) and Spanish (`docs/es/`)
- Multi-target framework support: `netstandard2.0`, `netstandard2.1`, `net6.0`, `net8.0`, `net9.0`
