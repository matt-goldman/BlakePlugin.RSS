using Blake.BuildTools;
using Blake.Types;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace BlakePlugin.RSS;

/// <summary>
/// Generator class for RSS feed content processing and generation.
/// </summary>
internal static class RssFeedGenerator
{
    /// <summary>
    /// Processes channel-level placeholders in the RSS template.
    /// </summary>
    /// <param name="content">The template content</param>
    /// <param name="cliArgs">CLI arguments dictionary</param>
    /// <returns>Tuple containing processed content and base URL</returns>
    public static (string content, string? baseUrl) ProcessChannelPlaceholders(string content, Dictionary<string, string> cliArgs)
    {
        // Channel level placeholders to process
        var placeholders = new Dictionary<string, string>
        {
            // LastBuildDate - always auto-generated
            ["LastBuildDate"] = DateTime.UtcNow.ToString("R") // RFC 1123 format
        };

        // Process mandatory fields: Title, Description, Link
        RssArgumentParser.ProcessMandatoryPlaceholder(content, cliArgs, placeholders, "Title");
        RssArgumentParser.ProcessMandatoryPlaceholder(content, cliArgs, placeholders, "Description");
        RssArgumentParser.ProcessMandatoryPlaceholder(content, cliArgs, placeholders, "Link");
        
        // Validate Link if provided and extract base URL
        string? baseUrl = null;
        if (placeholders.TryGetValue("Link", out var value))
        {
            RssValidator.ValidateUrl(value);
            baseUrl = value.TrimEnd('/');
        }
        
        // Replace placeholders in content
        foreach (var placeholder in placeholders)
        {
            var token = $"{{{{{placeholder.Key}}}}}";
            content = content.Replace(token, placeholder.Value);
        }
        
        // If we don't have a base URL from CLI/placeholders, extract it from the processed content
        if (baseUrl == null)
        {
            var linkMatch = Regex.Match(content, @"<link>([^<]+)</link>", 
                RegexOptions.IgnoreCase);
            if (linkMatch.Success)
            {
                baseUrl = linkMatch.Groups[1].Value.TrimEnd('/');
            }
        }
        
        return (content, baseUrl);
    }

    /// <summary>
    /// Processes the Items section of the RSS template and generates feed items.
    /// </summary>
    /// <param name="content">The template content</param>
    /// <param name="context">The Blake context</param>
    /// <param name="cliArgs">CLI arguments dictionary</param>
    /// <param name="baseUrl">The base URL for the site</param>
    /// <returns>Processed content with generated items</returns>
    public static string ProcessItemsSection(string content, BlakeContext context, Dictionary<string, string> cliArgs, string? baseUrl)
    {
        // Find the <Items>...</Items> section
        var itemsStart = content.IndexOf("<Items>", StringComparison.OrdinalIgnoreCase);
        var itemsEnd = content.IndexOf("</Items>", StringComparison.OrdinalIgnoreCase);
        
        if (itemsStart < 0 || itemsEnd < 0)
        {
            // No Items section found - return content as is
            return content;
        }
        
        // Extract the item template
        var itemsSection = content.Substring(itemsStart + "<Items>".Length, 
            itemsEnd - itemsStart - "<Items>".Length);
        var itemTemplate = itemsSection.Trim();
        
        if (string.IsNullOrWhiteSpace(itemTemplate))
        {
            // Empty items section - just remove it
            return RemoveItemsSection(content);
        }
        
        // Get max-items limit (default to 20 if not specified)
        var maxItems = 20; // Sensible default
        if (cliArgs.TryGetValue("max-items", out var maxItemsStr) && int.TryParse(maxItemsStr, out var parsedMaxItems))
        {
            maxItems = parsedMaxItems;
        }
        
        // Collect all pages to process
        var allPages = new List<GeneratedPage>();
        var errors = new List<string>();
        
        // Use GeneratedPages if available, otherwise fallback to creating minimal items from MarkdownPages
        if (context.GeneratedPages.Count > 0)
        {
            allPages.AddRange(context.GeneratedPages.Where(gp => RssArgumentParser.ShouldIncludePage(gp.Page.Slug, cliArgs)));
        }
        else
        {
            // Fallback: create dummy GeneratedPage from MarkdownPages for RSS generation
            foreach (var markdownPage in context.MarkdownPages)
            {
                if (RssArgumentParser.ShouldIncludePage(markdownPage.Slug, cliArgs))
                {
                    // Create a basic PageModel from MarkdownPage
                    var pageModel = new PageModel
                    {
                        Title = "Untitled", // Will be extracted from frontmatter if available
                        Slug = markdownPage.Slug,
                        Description = "No description available"
                    };
                    
                    var dummyGeneratedPage = new GeneratedPage(pageModel, "", markdownPage.RawMarkdown, "");
                    allPages.Add(dummyGeneratedPage);
                }
            }
        }
        
        // Sort by date descending (newest first)
        allPages = allPages
            .OrderByDescending(p => p.Page.Date ?? DateTime.MinValue)
            .Take(maxItems)
            .ToList();
        
        // Process each page and generate RSS items
        var generatedItems = new List<string>();
        
        foreach (var generatedPage in allPages)
        {
            try
            {
                var processedItem = ProcessItemPlaceholders(itemTemplate, generatedPage, baseUrl, cliArgs, errors);
                generatedItems.Add(processedItem);
            }
            catch (Exception ex)
            {
                errors.Add($"Error processing page '{generatedPage.Page.Title}': {ex.Message}");
            }
        }
        
        // Check for accumulated errors
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                $"RSS plugin error: Missing required item values:\n{string.Join("\n", errors)}");
        }
        
        // Replace the Items section with generated items
        var beforeItems = content.Substring(0, itemsStart).TrimEnd();
        var afterItems = content.Substring(itemsEnd + "</Items>".Length).TrimStart();
        
        var generatedItemsContent = generatedItems.Count > 0 
            ? "\n" + string.Join("\n", generatedItems) + "\n"
            : "\n";
            
        return beforeItems + generatedItemsContent + afterItems;
    }

    /// <summary>
    /// Processes placeholders for a single RSS item.
    /// </summary>
    /// <param name="itemTemplate">The item template</param>
    /// <param name="generatedPage">The generated page</param>
    /// <param name="baseUrl">The base URL</param>
    /// <param name="cliArgs">CLI arguments dictionary</param>
    /// <param name="errors">List to collect errors</param>
    /// <returns>Processed item content</returns>
    public static string ProcessItemPlaceholders(string itemTemplate, GeneratedPage generatedPage, string? baseUrl, 
        Dictionary<string, string> cliArgs, List<string> errors)
    {
        var page = generatedPage.Page;
        var placeholders = new Dictionary<string, string>();
        
        // Process standard RSS item placeholders
        ProcessItemPlaceholder(itemTemplate, "Item.Title", page.Title, placeholders, errors, page.Title);
        ProcessItemPlaceholder(itemTemplate, "Item.Description", page.Description, placeholders, errors, page.Title);
        
        // Generate Item.Link (absolute URL)
        if (!string.IsNullOrEmpty(baseUrl) && !string.IsNullOrEmpty(page.Slug))
        {
            var link = $"{baseUrl}/{page.Slug.TrimStart('/')}";
            ProcessItemPlaceholder(itemTemplate, "Item.Link", link, placeholders, errors, page.Title);
        }
        else if (itemTemplate.Contains("{{Item.Link}}", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"Page '{page.Title}': Cannot generate Item.Link - missing base URL or page slug");
        }
        
        // Generate Item.Guid (defaults to permalink)
        if (placeholders.TryGetValue("Item.Link", out var value))
        {
            ProcessItemPlaceholder(itemTemplate, "Item.Guid", value, placeholders, errors, page.Title);
        }
        else if (itemTemplate.Contains("{{Item.Guid}}", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"Page '{page.Title}': Cannot generate Item.Guid - missing Item.Link");
        }
        
        // Generate Item.PubDate from Date property
        if (page.Date.HasValue)
        {
            var pubDate = page.Date.Value.ToString("R", CultureInfo.InvariantCulture); // RFC 1123 format
            ProcessItemPlaceholder(itemTemplate, "Item.PubDate", pubDate, placeholders, errors, page.Title);
        }
        else if (itemTemplate.Contains("{{Item.PubDate}}", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"Page '{page.Title}': Missing Date property for Item.PubDate");
        }
        
        // Generate Item.CategoriesXml from Tags
        var categoriesXml = RssStringUtils.GenerateCategoriesXml(page.Tags);
        if (itemTemplate.Contains("{{Item.CategoriesXml}}", StringComparison.OrdinalIgnoreCase))
        {
            placeholders["Item.CategoriesXml"] = categoriesXml;
        }
        
        // Generate Item.ContentEncoded from rendered HTML
        if (itemTemplate.Contains("{{Item.ContentEncoded}}", StringComparison.OrdinalIgnoreCase))
        {
            // Use the rendered HTML content if available, otherwise fallback to Description
            var htmlContent = !string.IsNullOrEmpty(generatedPage.RawHtml) 
                ? generatedPage.RawHtml 
                : page.Description ?? "";
                
            var contentEncoded = !string.IsNullOrEmpty(htmlContent) 
                ? $"<content:encoded><![CDATA[{htmlContent}]]></content:encoded>"
                : "";
            placeholders["Item.ContentEncoded"] = contentEncoded;
        }

        // Process standard RSS item placeholders
        ProcessItemPlaceholder(itemTemplate, "Item.Image", page.Image, placeholders, errors, page.Title);

        // Process custom placeholders from metadata and CLI
        ProcessCustomItemPlaceholders(itemTemplate, page, cliArgs, placeholders, errors);
        
        // Replace placeholders in template
        var processedItem = itemTemplate;
        foreach (var placeholder in placeholders)
        {
            var token = $"{{{{{placeholder.Key}}}}}";
            processedItem = processedItem.Replace(token, placeholder.Value);
        }
        
        return processedItem;
    }

    /// <summary>
    /// Processes a single item placeholder.
    /// </summary>
    /// <param name="template">The template content</param>
    /// <param name="placeholderName">The placeholder name</param>
    /// <param name="value">The placeholder value</param>
    /// <param name="placeholders">Placeholders dictionary to populate</param>
    /// <param name="errors">List to collect errors</param>
    /// <param name="pageTitle">The page title for error messages</param>
    public static void ProcessItemPlaceholder(string template, string placeholderName, string? value, 
        Dictionary<string, string> placeholders, List<string> errors, string pageTitle)
    {
        var token = $"{{{{{placeholderName}}}}}";
        if (template.Contains(token, StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrEmpty(value))
            {
                // HTML escape the value if it's not already HTML encoded
                value = WebUtility.HtmlEncode(value);
                placeholders[placeholderName] = value;
            }
            else
            {
                errors.Add($"Page '{pageTitle}': Missing value for {{{{{placeholderName}}}}}");
            }
        }
    }

    /// <summary>
    /// Processes custom Item.* placeholders from metadata and CLI.
    /// </summary>
    /// <param name="template">The template content</param>
    /// <param name="page">The page model</param>
    /// <param name="cliArgs">CLI arguments dictionary</param>
    /// <param name="placeholders">Placeholders dictionary to populate</param>
    /// <param name="errors">List to collect errors</param>
    public static void ProcessCustomItemPlaceholders(string template, PageModel page, Dictionary<string, string> cliArgs,
        Dictionary<string, string> placeholders, List<string> errors)
    {
        // Look for custom Item.* placeholders in template
        var customPlaceholderMatches = Regex.Matches(template, 
            @"\{\{Item\.([A-Za-z][A-Za-z0-9_]*)\}\}", 
            RegexOptions.IgnoreCase);
        
        foreach (Match match in customPlaceholderMatches)
        {
            var fullPlaceholder = match.Groups[0].Value; // {{Item.Something}}
            var placeholderName = $"Item.{match.Groups[1].Value}"; // Item.Something
            var metadataKey = match.Groups[1].Value; // Something
            
            // Skip if we already processed this placeholder
            if (placeholders.ContainsKey(placeholderName))
                continue;
                
            // Skip standard RSS placeholders (they're handled above)
            if (IsStandardRssPlaceholder(placeholderName))
                continue;
            
            string? value = null;
            
            // 1. Check CLI args first (only for custom placeholders)
            var cliKey = metadataKey.ToLowerInvariant();
            if (cliArgs.TryGetValue(cliKey, out var cliValue))
            {
                value = cliValue;
            }
            // 2. Check metadata dictionary (case insensitive)
            else
            {
                var metadataMatch = page.Metadata.FirstOrDefault(kvp => 
                    string.Equals(kvp.Key, metadataKey, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(metadataMatch.Key))
                {
                    value = metadataMatch.Value;
                }
            }
            
            if (!string.IsNullOrEmpty(value))
            {
                // HTML escape the value if it's not already HTML encoded
                value = WebUtility.HtmlEncode(value);
                placeholders[placeholderName] = value;
            }
            else
            {
                errors.Add($"Page '{page.Title}': Missing value for {{{{{placeholderName}}}}}. " +
                          $"Checked CLI (--rss:{cliKey}), then PageModel.Metadata[\"{metadataKey}\"] (case insensitive).");
            }
        }
    }

    /// <summary>
    /// Checks if a placeholder name is a standard RSS placeholder.
    /// </summary>
    /// <param name="placeholderName">The placeholder name to check</param>
    /// <returns>True if it's a standard RSS placeholder, false otherwise</returns>
    public static bool IsStandardRssPlaceholder(string placeholderName)
    {
        var standardPlaceholders = new[] 
        {
            "Item.Title", "Item.Description", "Item.Link", "Item.Guid", 
            "Item.PubDate", "Item.CategoriesXml", "Item.ContentEncoded"
        };
        
        return standardPlaceholders.Any(sp => 
            string.Equals(sp, placeholderName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Removes the Items section from content when it's empty.
    /// </summary>
    /// <param name="content">The content to process</param>
    /// <returns>Content with Items section removed</returns>
    public static string RemoveItemsSection(string content)
    {
        // Find and remove the <Items>...</Items> section
        var itemsStart = content.IndexOf("<Items>", StringComparison.OrdinalIgnoreCase);
        var itemsEnd = content.IndexOf("</Items>", StringComparison.OrdinalIgnoreCase);
        
        if (itemsStart >= 0 && itemsEnd >= 0)
        {
            // Remove the entire Items section
            var beforeItems = content.Substring(0, itemsStart).TrimEnd();
            var afterItems = content.Substring(itemsEnd + "</Items>".Length).TrimStart();
            
            // Join with appropriate spacing
            content = beforeItems + "\n\n" + afterItems;
        }
        
        return content;
    }
}