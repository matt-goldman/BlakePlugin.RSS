using System.Net;

namespace BlakePlugin.RSS;

/// <summary>
/// Utility class for string and path manipulation operations for RSS plugin.
/// </summary>
internal static class RssStringUtils
{
    /// <summary>
    /// Normalizes a path by trimming leading slashes but preserving trailing slashes.
    /// </summary>
    /// <param name="path">The path to normalize</param>
    /// <returns>The normalized path</returns>
    public static string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return string.Empty;
            
        // Trim leading slashes but preserve trailing slashes
        return path.TrimStart('/');
    }
    
    /// <summary>
    /// Parses a comma or semicolon separated string of paths into an array.
    /// </summary>
    /// <param name="pathsString">The paths string to parse</param>
    /// <returns>Array of normalized paths</returns>
    public static string[] ParsePaths(string? pathsString)
    {
        if (string.IsNullOrWhiteSpace(pathsString))
            return Array.Empty<string>();
            
        // Split on comma or semicolon, trim whitespace, and normalize paths
        return pathsString
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => NormalizePath(p.Trim()))
            .Where(p => !string.IsNullOrEmpty(p))
            .ToArray();
    }

    /// <summary>
    /// Generates RSS categories XML from a list of tags.
    /// </summary>
    /// <param name="tags">The tags to convert to categories</param>
    /// <returns>RSS categories XML</returns>
    public static string GenerateCategoriesXml(List<string> tags)
    {
        if (tags == null || tags.Count == 0)
        {
            return string.Empty;
        }
        
        return string.Join("\n        ", tags.Select(tag => $"<category>{WebUtility.HtmlEncode(tag)}</category>"));
    }
}