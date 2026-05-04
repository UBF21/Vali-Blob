# Changelog

All notable changes to ValiBlob will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased]

---

## [2.0.0] - 2026-05-04

### Added

#### Decorators and instrumentation
- `StorageTelemetryDecorator` — wraps any `IStorageProvider` with OpenTelemetry tracing and metrics via decorator pattern
- `StorageEventDecorator` — wraps any `IStorageProvider` with event dispatching via decorator pattern
- `ConflictResolutionOptions` and conflict resolution middleware — rename on conflict, fail on conflict, or overwrite
- `StorageProviderType` enum — type-safe provider identification; replaces hardcoded string keys in DI registration
- `PipelineContextKeys` static class — eliminates magic strings in pipeline context items

#### Performance
- Pipeline caching in `StoragePipelineBuilder` — compiled once, reused per path
- `InMemoryDeduplicationHashIndex` in `DeduplicationMiddleware` — O(1) lookup replaces O(n) metadata scan

#### Testing
- Expanded test suite from 315 to 601 tests with negative paths for all core operations
- Dedicated `CompressionMiddleware` test suite (9 tests)
- `LocalStorageProvider` integration test suite (17 tests)
- CI/CD with coverlet integration

#### Developer experience
- XML documentation on all public API members across all packages
- All Options classes marked `sealed`
- `TreatWarningsAsErrors` enabled

### Changed

#### Architecture and SRP
- `BaseStorageProvider` refactored for Single Responsibility Principle — extracted resilience pipeline factory and composite operations
- All provider helpers extracted to dedicated internal classes:
  - **AWS:** `S3ResumableHandler`, `S3PresignedUrlHelper`
  - **Azure:** `AzureResumableHandler`, `AzureBatchHelper`, `AzureSasHelper`
  - **GCP:** `GcpObjectHelper`
  - **OCI:** `OciResumableUploadHandler`, `OciPresignedUrlHandler`, `OciObjectRequestBuilder`
  - **Supabase:** `SupabaseTusHandler`, `SupabaseHttpHelper`, `SupabaseUrlBuilder`
  - **Local:** `LocalStorageResumableHandler`, `LocalStorageFolderOperations`, `LocalStorageSidecarHelper`, `LocalStoragePathHelper`
  - **Testing:** `InMemoryResumableHandler`, `InMemoryStoreOperations`
- `StorageFactory.GetAll()` now derives provider keys from `StorageProviderType` enum instead of hardcoded array

### Fixed

#### Security
- **CRITICAL:** `EncryptionMiddleware` now generates random IV per upload instead of reusing the same IV
- **HIGH:** `AllowedUploadHosts` allowlist in `UploadFromUrlAsync` to prevent SSRF attacks
- **HIGH:** Chunk offset and length validation in `UploadChunkAsync`
- **HIGH:** API key authentication on sample app endpoints
- `RateLimitMiddleware` with sliding window per scope added to pipeline
- Added `SECURITY.md` documenting security model and best practices

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
- Multi-target framework support: `netstandard2.0`, `netstandard2.1`, `net6.0`, `net7.0`, `net8.0`, `net9.0`
