# Security Policy

## Known Vulnerabilities

### SixLabors.ImageSharp (NU1902, NU1903)

The `Vali-Blob.ImageSharp` package currently uses **SixLabors.ImageSharp 3.1.5**, which has known NuGet vulnerabilities (NU1902, NU1903).

**Status**: Suppressed via `<NoWarn>NU1902;NU1903</NoWarn>` in `Vali-Blob.ImageSharp.csproj`

**Rationale**: 
- ImageSharp is widely used and maintained by the community
- Vulnerabilities are suppressed because they do not pose a direct risk to Vali-Blob's core functionality
- The library is isolated to an optional module and not required for core storage operations
- Consumers who require image processing can evaluate the risk independently

**Mitigation**:
- Monitor for ImageSharp security updates
- Update to 3.1.6+ when available if vulnerabilities are addressed
- For security-critical deployments, consumers may choose to disable the ImageSharp module

## Security Best Practices

1. **Input Validation**: All user-provided paths and metadata are validated at system boundaries
2. **Error Handling**: Error messages do not expose sensitive information like connection strings or file paths
3. **Dependency Management**: Dependencies are pinned via Central Package Management (`Directory.Packages.props`)
4. **Async/Await**: All I/O operations use async patterns to prevent thread pool starvation

## Reporting Security Issues

If you discover a security vulnerability in Vali-Blob, please report it to the maintainers directly rather than using public issue tracking. Include:

- Affected component and version
- Vulnerability description
- Steps to reproduce (if applicable)
- Potential impact

## Changelog

Security-related changes and vulnerability updates are tracked in [CHANGELOG.md](CHANGELOG.md).
