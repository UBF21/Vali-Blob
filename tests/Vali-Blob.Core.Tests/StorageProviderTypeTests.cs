using FluentAssertions;
using ValiBlob.Core.Options;
using Xunit;

namespace ValiBlob.Core.Tests;

public sealed class StorageProviderTypeTests
{
    [Fact]
    public void StorageProviderType_Has_All_Providers()
    {
        var providerTypes = typeof(StorageProviderType).GetEnumValues().Cast<StorageProviderType>();

        providerTypes.Should().Contain(new[]
        {
            StorageProviderType.Local,
            StorageProviderType.InMemory,
            StorageProviderType.AWS,
            StorageProviderType.Azure,
            StorageProviderType.GCP,
            StorageProviderType.OCI,
            StorageProviderType.Supabase
        });
    }

    [Fact]
    public void StorageProviderType_Has_None_And_Custom()
    {
        var providerTypes = typeof(StorageProviderType).GetEnumValues().Cast<StorageProviderType>();

        providerTypes.Should().Contain(StorageProviderType.None);
        providerTypes.Should().Contain(StorageProviderType.Custom);
    }

    [Theory]
    [InlineData(StorageProviderType.Local, "Local")]
    [InlineData(StorageProviderType.InMemory, "InMemory")]
    [InlineData(StorageProviderType.AWS, "AWS")]
    [InlineData(StorageProviderType.Azure, "Azure")]
    [InlineData(StorageProviderType.GCP, "GCP")]
    [InlineData(StorageProviderType.OCI, "OCI")]
    [InlineData(StorageProviderType.Supabase, "Supabase")]
    public void StorageProviderType_ToString_Returns_Provider_Name(StorageProviderType type, string expectedName)
    {
        type.ToString().Should().Be(expectedName);
    }

    [Fact]
    public void StorageProviderType_Prevents_Invalid_Provider_Names_At_Compile_Time()
    {
        // This test documents the benefit: typos like StorageProviderType.Aws (lowercase 's')
        // would be caught at compile time, not runtime

        // Valid
        var valid = StorageProviderType.AWS;
        valid.Should().NotBe(StorageProviderType.None);

        // Invalid would not compile:
        // var invalid = StorageProviderType.Aws; // CS1061: 'StorageProviderType' does not contain a definition for 'Aws'
    }

    [Fact]
    public void StorageGlobalOptions_Can_Use_Enum()
    {
        var options = new StorageGlobalOptions
        {
            DefaultProvider = StorageProviderType.Local.ToString()
        };

        options.DefaultProvider.Should().Be("Local");

        // Can convert back to enum
        Enum.TryParse<StorageProviderType>(options.DefaultProvider, out var providerType).Should().BeTrue();
        providerType.Should().Be(StorageProviderType.Local);
    }

    [Fact]
    public void None_Provider_Type_Requires_Explicit_Configuration()
    {
        var options = new StorageGlobalOptions
        {
            DefaultProvider = StorageProviderType.None.ToString()
        };

        var providerType = options.GetDefaultProviderType();
        providerType.Should().Be(StorageProviderType.None);
    }

    [Fact]
    public void Custom_Provider_Type_Allows_Unregistered_Keys()
    {
        var options = new StorageGlobalOptions
        {
            DefaultProvider = "CustomVendor" // Not a standard enum value
        };

        var providerType = options.GetDefaultProviderType();
        providerType.Should().Be(StorageProviderType.Custom);
    }

    [Fact]
    public void GetDefaultProviderType_Is_Case_Insensitive()
    {
        var testCases = new[]
        {
            ("local", StorageProviderType.Local),
            ("LOCAL", StorageProviderType.Local),
            ("Local", StorageProviderType.Local),
            ("aws", StorageProviderType.AWS),
            ("AWS", StorageProviderType.AWS),
            ("Azure", StorageProviderType.Azure),
            ("AZURE", StorageProviderType.Azure),
        };

        foreach (var (input, expected) in testCases)
        {
            var options = new StorageGlobalOptions { DefaultProvider = input };
            options.GetDefaultProviderType().Should().Be(expected, $"for input '{input}'");
        }
    }
}
