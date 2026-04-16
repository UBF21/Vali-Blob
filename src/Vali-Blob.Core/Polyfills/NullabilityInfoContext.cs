#if NETSTANDARD2_0
// Minimal polyfill stub so the project compiles on netstandard2.0.
// Full NullabilityInfoContext is only available on .NET 6+.
namespace System.Reflection;

internal sealed class NullabilityInfoContext
{
    public NullabilityInfo Create(ParameterInfo parameterInfo) =>
        throw new PlatformNotSupportedException("NullabilityInfoContext is not supported on netstandard2.0.");

    public NullabilityInfo Create(PropertyInfo propertyInfo) =>
        throw new PlatformNotSupportedException("NullabilityInfoContext is not supported on netstandard2.0.");

    public NullabilityInfo Create(FieldInfo fieldInfo) =>
        throw new PlatformNotSupportedException("NullabilityInfoContext is not supported on netstandard2.0.");
}

internal sealed class NullabilityInfo
{
    public NullabilityState ReadState { get; internal set; }
    public NullabilityState WriteState { get; internal set; }
    public Type Type { get; internal set; } = typeof(object);
    public NullabilityInfo[] GenericTypeArguments { get; internal set; } = Array.Empty<NullabilityInfo>();
    public NullabilityInfo? ElementType { get; internal set; }
}

internal enum NullabilityState
{
    Unknown,
    NotNull,
    Nullable
}
#endif
