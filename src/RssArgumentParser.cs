using Blake.BuildTools;

namespace BlakePlugin.RSS;

/// <summary>
/// Parser class for RSS CLI arguments and content filtering logic.
/// </summary>
internal static class RssArgumentParser
{
    private const string RssCliPrefix = "--rss:";
    private const string RssTemplateCliPrefix = "--rss:template=";

    /// <summary>
    /// Extracts RSS-specific CLI arguments from the Blake context.
    /// </summary>
    /// <param name="context">The Blake context containing CLI arguments</param>
    /// <returns>Dictionary of RSS CLI arguments</returns>
    public static Dictionary<string, string> ExtractRssCliArguments(BlakeContext context)
    {
        var rssArgs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        
        foreach (var arg in context.Arguments)
        {
            if (arg.StartsWith(RssCliPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var keyValue = arg.Substring(RssCliPrefix.Length);
                var equalsIndex = keyValue.IndexOf('=');
                if (equalsIndex > 0)
                {
                    var key = keyValue.Substring(0, equalsIndex);
                    var value = keyValue.Substring(equalsIndex + 1);
                    rssArgs[key] = value;
                }
            }
        }
        
        return rssArgs;
    }

    /// <summary>
    /// Gets custom template path from CLI arguments.
    /// </summary>
    /// <param name="context">The Blake context containing CLI arguments</param>
    /// <returns>Custom template path if specified, null otherwise</returns>
    public static string? GetCustomTemplateFromCli(BlakeContext context)
    {
        // Look for --rss:template= in CLI arguments
        foreach (var arg in context.Arguments)
        {
            if (arg.StartsWith(RssTemplateCliPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return arg.Substring(RssTemplateCliPrefix.Length);
            }
        }
        return null;
    }

    /// <summary>
    /// Processes a mandatory placeholder from CLI arguments or validates existence in template.
    /// </summary>
    /// <param name="content">The template content</param>
    /// <param name="cliArgs">CLI arguments dictionary</param>
    /// <param name="placeholders">Placeholders dictionary to populate</param>
    /// <param name="fieldName">The field name to process</param>
    /// <exception cref="InvalidOperationException">Thrown when required placeholder is missing</exception>
    public static void ProcessMandatoryPlaceholder(string content, Dictionary<string, string> cliArgs, 
        Dictionary<string, string> placeholders, string fieldName)
    {
        var token = $"{{{{{fieldName}}}}}";
        var hasPlaceholder = content.Contains(token);
        
        // Check CLI first (highest priority)
        if (cliArgs.TryGetValue(fieldName, out var cliValue))
        {
            placeholders[fieldName] = cliValue;
            return;
        }
        
        // If template has placeholder but no CLI value, it's an error
        if (hasPlaceholder)
        {
            throw new InvalidOperationException(
                $"RSS plugin error: Missing value for {{{{{fieldName}}}}}.\n" +
                $"Checked CLI (--rss:{fieldName}), but no value found.\n" +
                $"Provide a CLI argument or replace the placeholder with a value in the template.");
        }
        
        // If no placeholder in template and no CLI value, that's fine - 
        // the template might have the value directly embedded
    }

    /// <summary>
    /// Determines if a page should be included in the RSS feed based on CLI filter arguments.
    /// </summary>
    /// <param name="pageSlug">The page slug to check</param>
    /// <param name="cliArgs">CLI arguments dictionary</param>
    /// <returns>True if page should be included, false otherwise</returns>
    public static bool ShouldIncludePage(string pageSlug, Dictionary<string, string> cliArgs)
    {
        var normalizedSlug = RssStringUtils.NormalizePath(pageSlug);
        
        // Check include-paths first (if specified, only include matching paths)
        var includePathsValue = cliArgs.TryGetValue("include-paths", out var includePaths) ? includePaths : null;
        if (!string.IsNullOrEmpty(includePathsValue))
        {
            var includePathsArray = RssStringUtils.ParsePaths(includePathsValue);
            if (includePathsArray.Length > 0)
            {
                var shouldInclude = includePathsArray.Any(path => 
                    normalizedSlug.StartsWith(path, StringComparison.OrdinalIgnoreCase));
                if (!shouldInclude)
                    return false;
            }
        }
        
        // Check ignore-paths (support both old singular and new plural forms)
        var ignorePathsValue = cliArgs.TryGetValue("ignore-paths", out var ignorePaths) ? ignorePaths :
                              cliArgs.TryGetValue("ignore-path", out var ignorePath) ? ignorePath : null;
        if (!string.IsNullOrEmpty(ignorePathsValue))
        {
            var ignorePathsArray = RssStringUtils.ParsePaths(ignorePathsValue);
            var shouldIgnore = ignorePathsArray.Any(path => 
                normalizedSlug.StartsWith(path, StringComparison.OrdinalIgnoreCase));
            if (shouldIgnore)
                return false;
        }
        
        return true;
    }
}