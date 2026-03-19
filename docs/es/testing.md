# Testing

ValiBlob incluye el package `ValiBlob.Testing` con un proveedor en memoria para tests unitarios y de integración rápidos, sin necesidad de credenciales cloud ni infraestructura externa.

---

## Por qué ValiBlob.Testing

Sin `ValiBlob.Testing`, los tests que involucran storage tienen dos opciones:
1. **Mocks manuales**: `Moq.Mock<IStorageProvider>()` — funciona, pero perdés el comportamiento real del path, validaciones, etc.
2. **Storage real**: requiere credenciales, es lento, tiene costo y no es reproducible.

`InMemoryStorageProvider` ofrece un tercer camino: comportamiento real (guarda, devuelve y elimina archivos de verdad) sin ninguna dependencia externa.

---

## Instalación

```bash
dotnet add package ValiBlob.Testing
```

---

## Referencia de `InMemoryStorageProvider`

`InMemoryStorageProvider` implementa `IStorageProvider` completamente e incluye métodos adicionales para assertions en tests:

| Miembro | Tipo | Descripción |
|---|---|---|
| `HasFile(string path)` | `bool` | Retorna `true` si existe un archivo en esa ruta |
| `GetRawBytes(string path)` | `byte[]` | Retorna el contenido del archivo como bytes. Lanza `FileNotFoundException` si no existe |
| `AllPaths` | `IReadOnlyCollection<string>` | Todas las rutas actualmente en el store |
| `FileCount` | `int` | Cantidad de archivos en el store |
| `Clear()` | `void` | Elimina todos los archivos del store |
| `ProviderName` | `string` | Siempre retorna `"InMemory"` |

Todos los métodos de `IStorageProvider` están implementados:
- `UploadAsync` — guarda el contenido en un `ConcurrentDictionary`
- `DownloadAsync` — retorna un `MemoryStream` con el contenido
- `DeleteAsync` — elimina la entrada
- `ExistsAsync` — verifica si existe la clave
- `GetUrlAsync` — retorna `inmemory://{path}`
- `CopyAsync`, `MoveAsync`, `GetMetadataAsync`, `SetMetadataAsync`
- `ListFilesAsync`, `ListAllAsync`
- `DeleteManyAsync`, `DeleteFolderAsync`, `ListFoldersAsync`
- `UploadFromUrlAsync` — retorna `StorageErrorCode.NotSupported`

---

## Setup en tests

### Setup básico con `ServiceCollection`

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ValiBlob.Core.DependencyInjection;
using ValiBlob.Testing;
using ValiBlob.Testing.Extensions;

public sealed class MiServicioTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly InMemoryStorageProvider _storage;
    private readonly MiServicio _servicio;

    public MiServicioTests()
    {
        var services = new ServiceCollection();

        // IConfiguration vacía — requerida por BindConfiguration
        var configuration = new ConfigurationBuilder().Build();
        services.AddSingleton<IConfiguration>(configuration);

        services.AddLogging();
        services.AddValiBlob().UseInMemory();

        // Registrá tus servicios
        services.AddScoped<MiServicio>();

        _serviceProvider = services.BuildServiceProvider();
        _storage = _serviceProvider.GetRequiredService<InMemoryStorageProvider>();
        _servicio = _serviceProvider.GetRequiredService<MiServicio>();
    }

    public void Dispose() => _serviceProvider.Dispose();
}
```

### Setup con xUnit fixtures (reutilizar ServiceProvider)

```csharp
public sealed class StorageFixture : IDisposable
{
    public ServiceProvider ServiceProvider { get; }
    public InMemoryStorageProvider Storage { get; }

    public StorageFixture()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddValiBlob().UseInMemory();

        ServiceProvider = services.BuildServiceProvider();
        Storage = ServiceProvider.GetRequiredService<InMemoryStorageProvider>();
    }

    public void Dispose() => ServiceProvider.Dispose();
}

public sealed class DocumentServiceTests : IClassFixture<StorageFixture>
{
    private readonly StorageFixture _fixture;

    public DocumentServiceTests(StorageFixture fixture)
    {
        _fixture = fixture;
        _fixture.Storage.Clear(); // limpiar entre tests
    }
}
```

---

## Ejemplos de tests unitarios

### Tests básicos con xUnit y FluentAssertions

```csharp
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ValiBlob.Core.DependencyInjection;
using ValiBlob.Core.Models;
using ValiBlob.Testing;
using ValiBlob.Testing.Extensions;
using Xunit;

public sealed class InMemoryProviderTests
{
    private readonly InMemoryStorageProvider _provider;

    public InMemoryProviderTests()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddValiBlob().UseInMemory();

        var sp = services.BuildServiceProvider();
        _provider = sp.GetRequiredService<InMemoryStorageProvider>();
    }

    [Fact]
    public async Task Upload_deberiaAlmacenarElArchivo()
    {
        // Arrange
        var contenido = "Hola ValiBlob"u8.ToArray();
        var request = new UploadRequest
        {
            Path = StoragePath.From("test", "hola.txt"),
            Content = new MemoryStream(contenido),
            ContentType = "text/plain",
            ContentLength = contenido.Length
        };

        // Act
        var resultado = await _provider.UploadAsync(request);

        // Assert
        resultado.IsSuccess.Should().BeTrue();
        resultado.Value!.Path.Should().Be("test/hola.txt");
        resultado.Value.SizeBytes.Should().Be(contenido.Length);
        _provider.HasFile("test/hola.txt").Should().BeTrue();
    }

    [Fact]
    public async Task Download_cuandoExiste_deberiaRetornarContenido()
    {
        // Arrange
        var contenido = "contenido de prueba"u8.ToArray();
        await _provider.UploadAsync(new UploadRequest
        {
            Path = StoragePath.From("test", "archivo.txt"),
            Content = new MemoryStream(contenido)
        });

        // Act
        var resultado = await _provider.DownloadAsync(new DownloadRequest
        {
            Path = StoragePath.From("test", "archivo.txt")
        });

        // Assert
        resultado.IsSuccess.Should().BeTrue();
        var ms = new MemoryStream();
        await resultado.Value!.CopyToAsync(ms);
        ms.ToArray().Should().BeEquivalentTo(contenido);
    }

    [Fact]
    public async Task Download_cuandoNoExiste_deberiaRetornarFileNotFound()
    {
        var resultado = await _provider.DownloadAsync(new DownloadRequest
        {
            Path = StoragePath.From("no-existe.txt")
        });

        resultado.IsSuccess.Should().BeFalse();
        resultado.ErrorCode.Should().Be(StorageErrorCode.FileNotFound);
    }

    [Fact]
    public async Task Delete_deberiaEliminarElArchivo()
    {
        // Arrange
        await _provider.UploadAsync(new UploadRequest
        {
            Path = StoragePath.From("eliminar.txt"),
            Content = new MemoryStream("x"u8.ToArray())
        });
        _provider.HasFile("eliminar.txt").Should().BeTrue();

        // Act
        var resultado = await _provider.DeleteAsync("eliminar.txt");

        // Assert
        resultado.IsSuccess.Should().BeTrue();
        _provider.HasFile("eliminar.txt").Should().BeFalse();
    }

    [Fact]
    public async Task ListFiles_conPrefijo_deberiaFiltrarCorrectamente()
    {
        _provider.Clear();

        await _provider.UploadAsync(new UploadRequest { Path = StoragePath.From("docs", "a.pdf"), Content = new MemoryStream("x"u8.ToArray()) });
        await _provider.UploadAsync(new UploadRequest { Path = StoragePath.From("docs", "b.pdf"), Content = new MemoryStream("x"u8.ToArray()) });
        await _provider.UploadAsync(new UploadRequest { Path = StoragePath.From("images", "c.jpg"), Content = new MemoryStream("x"u8.ToArray()) });

        var resultado = await _provider.ListFilesAsync("docs/");

        resultado.IsSuccess.Should().BeTrue();
        resultado.Value.Should().HaveCount(2);
        resultado.Value.Should().AllSatisfy(f => f.Path.Should().StartWith("docs/"));
    }

    [Fact]
    public async Task DeleteMany_deberiaEliminarTodosLosEspecificados()
    {
        // Arrange
        _provider.Clear();
        await _provider.UploadAsync(new UploadRequest { Path = StoragePath.From("batch", "a.txt"), Content = new MemoryStream("x"u8.ToArray()) });
        await _provider.UploadAsync(new UploadRequest { Path = StoragePath.From("batch", "b.txt"), Content = new MemoryStream("x"u8.ToArray()) });
        await _provider.UploadAsync(new UploadRequest { Path = StoragePath.From("batch", "c.txt"), Content = new MemoryStream("x"u8.ToArray()) });

        var paths = new[]
        {
            StoragePath.From("batch", "a.txt"),
            StoragePath.From("batch", "b.txt")
        };

        // Act
        var resultado = await _provider.DeleteManyAsync(paths);

        // Assert
        resultado.IsSuccess.Should().BeTrue();
        resultado.Value!.TotalRequested.Should().Be(2);
        resultado.Value.Deleted.Should().Be(2);
        resultado.Value.Failed.Should().Be(0);
        _provider.HasFile("batch/a.txt").Should().BeFalse();
        _provider.HasFile("batch/b.txt").Should().BeFalse();
        _provider.HasFile("batch/c.txt").Should().BeTrue(); // no solicitado
    }

    [Fact]
    public async Task GetRawBytes_deberiaRetornarContenidoOriginal()
    {
        // Arrange
        var contenido = "contenido exacto para verificar"u8.ToArray();
        await _provider.UploadAsync(new UploadRequest
        {
            Path = StoragePath.From("verificacion.txt"),
            Content = new MemoryStream(contenido)
        });

        // Act
        var bytes = _provider.GetRawBytes("verificacion.txt");

        // Assert
        bytes.Should().BeEquivalentTo(contenido);
    }
}
```

### Tests de servicios que usan storage

```csharp
public sealed class DocumentoServiceTests
{
    private readonly InMemoryStorageProvider _storage;
    private readonly DocumentoService _servicio;

    public DocumentoServiceTests()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddValiBlob().UseInMemory();
        services.AddScoped<DocumentoService>();

        var sp = services.BuildServiceProvider();
        _storage = sp.GetRequiredService<InMemoryStorageProvider>();
        _servicio = sp.GetRequiredService<DocumentoService>();
    }

    [Fact]
    public async Task SubirDocumento_deberiaAlmacenarYRetornarId()
    {
        // Arrange
        var contenido = new MemoryStream("PDF content"u8.ToArray());

        // Act
        var id = await _servicio.SubirDocumentoAsync(contenido, "mi-factura.pdf");

        // Assert
        id.Should().NotBeNullOrEmpty();
        _provider.FileCount.Should().Be(1);
        _provider.AllPaths.Should().ContainSingle(p => p.EndsWith("mi-factura.pdf"));
    }

    [Fact]
    public async Task EliminarDocumento_deberiaBorrarDelStorage()
    {
        // Arrange — preparar estado
        await _provider.UploadAsync(new UploadRequest
        {
            Path = StoragePath.From("documentos", "doc-a-eliminar.pdf"),
            Content = new MemoryStream("contenido"u8.ToArray())
        });

        // Act
        await _servicio.EliminarDocumentoAsync("doc-a-eliminar.pdf");

        // Assert
        _provider.HasFile("documentos/doc-a-eliminar.pdf").Should().BeFalse();
    }
}
```

---

## Tests de integración con Testcontainers + MinIO

Para tests de integración que usen la API S3 real (sin depender de AWS), podés usar MinIO en un contenedor Docker vía Testcontainers.

### Requisitos

- Docker instalado y corriendo
- Package `Testcontainers.Minio`

```bash
dotnet add package Testcontainers.Minio
```

### Cómo configurar

```csharp
using DotNet.Testcontainers.Builders;
using Testcontainers.Minio;
using ValiBlob.Core.DependencyInjection;
using ValiBlob.AWS.Extensions;
using Xunit;

public sealed class IntegrationTests : IAsyncLifetime
{
    private readonly MinioContainer _minio = new MinioBuilder()
        .WithUsername("minioadmin")
        .WithPassword("minioadmin")
        .Build();

    private ServiceProvider? _serviceProvider;
    private IStorageProvider? _storage;

    public async Task InitializeAsync()
    {
        await _minio.StartAsync();

        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();

        services
            .AddValiBlob()
            .UseMinIO(opts =>
            {
                opts.Bucket = "test-bucket";
                opts.ServiceUrl = _minio.GetConnectionString();
                opts.AccessKeyId = "minioadmin";
                opts.SecretAccessKey = "minioadmin";
            })
            .WithDefaultProvider("AWS");

        _serviceProvider = services.BuildServiceProvider();

        var factory = _serviceProvider.GetRequiredService<IStorageFactory>();
        _storage = factory.Create();

        // Crear el bucket de prueba
        // (en producción esto lo hace AWS, en MinIO hay que crearlo manualmente)
        // Podés usar el SDK de AWS directamente para este setup
    }

    public async Task DisposeAsync()
    {
        _serviceProvider?.Dispose();
        await _minio.DisposeAsync();
    }

    [Fact]
    public async Task Upload_conMinIO_deberiaGuardarElArchivo()
    {
        // Este test usa el contenedor real de MinIO
        var contenido = "Contenido de integración"u8.ToArray();

        var result = await _storage!.UploadAsync(new UploadRequest
        {
            Path = StoragePath.From("test", "integracion.txt"),
            Content = new MemoryStream(contenido),
            ContentType = "text/plain",
            ContentLength = contenido.Length
        });

        result.IsSuccess.Should().BeTrue();

        // Verificar que existe
        var exists = await _storage.ExistsAsync("test/integracion.txt");
        exists.IsSuccess.Should().BeTrue();
        exists.Value.Should().BeTrue();
    }
}
```

---

## Cuándo usar tests unitarios vs de integración

| Escenario | Recomendación |
|---|---|
| Lógica de negocio que usa storage | Tests unitarios con `InMemoryStorageProvider` |
| Comportamiento del proveedor (subida multiparte, metadata, etc.) | Tests de integración con Testcontainers + MinIO |
| Pipeline de middleware | Tests unitarios con `InMemoryStorageProvider` |
| Configuración de autenticación | Tests de integración |
| CI/CD sin Docker | Tests unitarios únicamente |
| CI/CD con Docker disponible | Ambos |

> **💡 Tip:** Para el 90% de los tests de tu aplicación, `InMemoryStorageProvider` es suficiente. Los tests de integración con Testcontainers son valiosos para testear el comportamiento específico del proveedor (manejo de errores de red, multiparte, metadata) pero son más lentos y requieren Docker.
