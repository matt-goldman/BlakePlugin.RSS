namespace BlakePlugin.RSS;

/// <summary>
/// Validator class for RSS content and URL validation.
/// </summary>
internal static class RssValidator
{
    /// <summary>
    /// Validates that required RSS elements exist in the processed content.
    /// </summary>
    /// <param name="content">The RSS content to validate</param>
    /// <exception cref="InvalidOperationException">Thrown when required elements are missing</exception>
    public static void ValidateRequiredRssElements(string content)
    {
        // Required RSS elements according to RSS 2.0 spec
        string[] requiredElements = { "title", "link", "description" };
        var missingElements = new List<string>();
        
        foreach (var element in requiredElements)
        {
            var startTag = $"<{element}";
            var endTag = $"</{element}>";
            
            // Check if element exists in the content
            // Look for start tag (allowing for attributes like <link href="...">)
            var startIndex = content.IndexOf(startTag, StringComparison.OrdinalIgnoreCase);
            var endIndex = content.IndexOf(endTag, StringComparison.OrdinalIgnoreCase);
            
            // Element must have both opening and closing tags
            if (startIndex == -1 || endIndex == -1 || endIndex <= startIndex)
            {
                missingElements.Add(element);
            }
            else
            {
                // Check if the element has content (not empty)
                // Find the actual start of content (after the closing >)
                var contentStart = content.IndexOf('>', startIndex);
                if (contentStart != -1 && contentStart < endIndex)
                {
                    var elementContent = content.Substring(contentStart + 1, endIndex - contentStart - 1).Trim();
                    if (string.IsNullOrWhiteSpace(elementContent))
                    {
                        missingElements.Add(element + " (empty)");
                    }
                }
                else
                {
                    missingElements.Add(element + " (malformed)");
                }
            }
        }
        
        if (missingElements.Count > 0)
        {
            throw new InvalidOperationException(
                $"RSS plugin error: Required RSS elements are missing or empty in the template: {string.Join(", ", missingElements)}.\n" +
                $"Each RSS feed must have <title>, <link>, and <description> elements in the <channel>.\n" +
                $"Either add these elements directly to your template or use placeholders like {{{{Title}}}}, {{{{Link}}}}, {{{{Description}}}} with corresponding CLI arguments.");
        }
    }

    /// <summary>
    /// Validates that a URL is properly formatted and uses http/https scheme.
    /// </summary>
    /// <param name="url">The URL to validate</param>
    /// <exception cref="InvalidOperationException">Thrown when URL is invalid</exception>
    public static void ValidateUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new InvalidOperationException("RSS plugin error: Link URL cannot be empty.");
        }
        
        // Check for invalid patterns
        if (url.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("localhost", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("RSS plugin error: Link URL cannot be 'localhost'.");
        }
        
        // Check if it's a relative URL (no scheme)
        if (!url.Contains("://"))
        {
            throw new InvalidOperationException("RSS plugin error: Link URL must be absolute (include http:// or https://).");
        }
        
        // Try to parse as URI for basic validation
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsedUri))
        {
            throw new InvalidOperationException($"RSS plugin error: Link URL '{url}' is not a valid URL format.");
        }
        
        // Ensure it's http or https
        if (parsedUri.Scheme != "http" && parsedUri.Scheme != "https")
        {
            throw new InvalidOperationException($"RSS plugin error: Link URL must use http or https scheme, got '{parsedUri.Scheme}'.");
        }
    }
}