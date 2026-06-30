using System.Reflection;

namespace Tharga.MongoDB;

/// <summary>
/// Helpers for reading a library's display version at runtime.
/// </summary>
public static class AssemblyVersionExtensions
{
    /// <summary>
    /// The library version of <paramref name="assembly"/> for display — the informational/NuGet version
    /// with any build-metadata suffix (e.g. the <c>+&lt;sha&gt;</c> appended by SourceLink) stripped.
    /// Falls back to the assembly name version, or <c>null</c> when neither is available.
    /// </summary>
    public static string GetLibraryVersion(this Assembly assembly)
    {
        if (assembly == null) return null;

        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrEmpty(informational))
        {
            var plus = informational.IndexOf('+');
            return plus >= 0 ? informational[..plus] : informational;
        }

        return assembly.GetName().Version?.ToString();
    }
}
