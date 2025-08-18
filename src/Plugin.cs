using Blake.BuildTools;
using Blake.Types;
using Microsoft.Extensions.Logging;
using System.Globalization;

namespace BlakePlugin.RSS;

public class Plugin : IBlakePlugin
{
    private const string DefaultTemplateFileName = "feed.template.xml";
    private const string RssTemplateCliPrefix = "--rss:template=";
    private const string RssCliPrefix = "--rss:";
    
    public async Task AfterBakeAsync(BlakeContext context, ILogger? logger = null)
    {
        // Check for CLI argument --rss:template=[file]
        var customTemplatePath = GetCustomTemplateFromCli(context);
        
        string templatePath;
        var isCustomTemplate = !string.IsNullOrEmpty(customTemplatePath);
        
        if (isCustomTemplate)
        {
            // Custom template specified via CLI
            templatePath = Path.IsPathRooted(customTemplatePath!) 
                ? customTemplatePath! 
                : Path.Combine(GetProjectPath(context), customTemplatePath!);
        }
        else
        {
            // Default template path: wwwroot/feed.template.xml
            templatePath = Path.Combine(GetProjectPath(context), "wwwroot", DefaultTemplateFileName);
        }
        
        if (!File.Exists(templatePath))
        {
            if (isCustomTemplate)
            {
                // Custom template not found - fail without creating
                throw new FileNotFoundException($"The specified RSS template file was not found: {templatePath}");
            }
            else
            {
                // Default template not found - create it and fail with helpful message
                await CreateDefaultTemplate(templatePath);
                throw new InvalidOperationException(
                    $"Required RSS template wasn't found and has been created at {templatePath}. " +
                    "Please fill in the missing details before running again.");
            }
        }
        
        // Template exists - process it and generate feed.xml
        await ProcessTemplateAndGenerateFeed(templatePath, context, logger);
    }
    
    private async Task ProcessTemplateAndGenerateFeed(string templatePath, BlakeContext context, ILogger? logger = null)
    {
        // Read the template
        var templateContent = await File.ReadAllTextAsync(templatePath);
        logger?.LogInformation("Processing RSS template from: {TemplatePath}", templatePath);
        
        // Extract CLI arguments
        var cliArgs = ExtractRssCliArguments(context);
        
        // Process channel level placeholders
        var (processedContent, baseUrl) = ProcessChannelPlaceholders(templateContent, cliArgs);
        
        // Validate that required RSS elements exist in the processed content
        ValidateRequiredRssElements(processedContent);
        
        // Process Items section - extract template and generate items for each page
        processedContent = ProcessItemsSection(processedContent, context, cliArgs, baseUrl);
        
        // Write to feed.xml
        var outputPath = Path.Combine(GetProjectPath(context), "wwwroot", "feed.xml");
        
        // Ensure wwwroot directory exists
        var outputDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDirectory) && !Directory.Exists(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }
        
        await File.WriteAllTextAsync(outputPath, processedContent);
        logger?.LogInformation("RSS feed generated at: {OutputPath}", outputPath);
    }
    
    private Dictionary<string, string> ExtractRssCliArguments(BlakeContext context)
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
    
    private (string content, string? baseUrl) ProcessChannelPlaceholders(string content, Dictionary<string, string> cliArgs)
    {
        // Channel level placeholders to process
        var placeholders = new Dictionary<string, string>
        {
            // LastBuildDate - always auto-generated
            ["LastBuildDate"] = DateTime.UtcNow.ToString("R") // RFC 1123 format
        };

        // Process mandatory fields: Title, Description, Link
        ProcessMandatoryPlaceholder(content, cliArgs, placeholders, "Title");
        ProcessMandatoryPlaceholder(content, cliArgs, placeholders, "Description");
        ProcessMandatoryPlaceholder(content, cliArgs, placeholders, "Link");
        
        // Validate Link if provided and extract base URL
        string? baseUrl = null;
        if (placeholders.TryGetValue("Link", out var value))
        {
            ValidateUrl(value);
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
            var linkMatch = System.Text.RegularExpressions.Regex.Match(content, @"<link>([^<]+)</link>", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (linkMatch.Success)
            {
                baseUrl = linkMatch.Groups[1].Value.TrimEnd('/');
            }
        }
        
        return (content, baseUrl);
    }
    
    private void ProcessMandatoryPlaceholder(string content, Dictionary<string, string> cliArgs, 
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
    
    private void ValidateUrl(string url)
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
    
    private static string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return string.Empty;
            
        // Trim leading slashes but preserve trailing slashes
        return path.TrimStart('/');
    }
    
    private static string[] ParsePaths(string? pathsString)
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
    
    private static bool ShouldIncludePage(string pageSlug, Dictionary<string, string> cliArgs)
    {
        var normalizedSlug = NormalizePath(pageSlug);
        
        // Check include-paths first (if specified, only include matching paths)
        var includePathsValue = cliArgs.TryGetValue("include-paths", out var includePaths) ? includePaths : null;
        if (!string.IsNullOrEmpty(includePathsValue))
        {
            var includePathsArray = ParsePaths(includePathsValue);
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
            var ignorePathsArray = ParsePaths(ignorePathsValue);
            var shouldIgnore = ignorePathsArray.Any(path => 
                normalizedSlug.StartsWith(path, StringComparison.OrdinalIgnoreCase));
            if (shouldIgnore)
                return false;
        }
        
        return true;
    }
    
    private void ValidateRequiredRssElements(string content)
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
    
    private string ProcessItemsSection(string content, BlakeContext context, Dictionary<string, string> cliArgs, string? baseUrl)
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
            allPages.AddRange(context.GeneratedPages.Where(gp => ShouldIncludePage(gp.Page.Slug, cliArgs)));
        }
        else
        {
            // Fallback: create dummy GeneratedPage from MarkdownPages for RSS generation
            foreach (var markdownPage in context.MarkdownPages)
            {
                if (ShouldIncludePage(markdownPage.Slug, cliArgs))
                {
                    // Create a basic PageModel from MarkdownPage
                    var pageModel = new PageModel
                    {
                        Title = "Untitled", // Will be extracted from frontmatter if available
                        Slug = markdownPage.Slug,
                        Description = "No description available"
                    };
                    
                    var dummyGeneratedPage = new GeneratedPage(pageModel, "", markdownPage.RawMarkdown);
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
    
    private string ProcessItemPlaceholders(string itemTemplate, GeneratedPage generatedPage, string? baseUrl, 
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
        var categoriesXml = GenerateCategoriesXml(page.Tags);
        if (itemTemplate.Contains("{{Item.CategoriesXml}}", StringComparison.OrdinalIgnoreCase))
        {
            placeholders["Item.CategoriesXml"] = categoriesXml;
        }
        
        // Generate Item.ContentEncoded from rendered HTML
        if (itemTemplate.Contains("{{Item.ContentEncoded}}", StringComparison.OrdinalIgnoreCase))
        {
            // Use the rendered HTML content if available, otherwise fallback to Description
            var htmlContent = !string.IsNullOrEmpty(generatedPage.RazorHtml) 
                ? generatedPage.RazorHtml 
                : page.Description ?? "";
                
            var contentEncoded = !string.IsNullOrEmpty(htmlContent) 
                ? $"<content:encoded><![CDATA[{htmlContent}]]></content:encoded>"
                : "";
            placeholders["Item.ContentEncoded"] = contentEncoded;
        }
        
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
    
    private void ProcessItemPlaceholder(string template, string placeholderName, string? value, 
        Dictionary<string, string> placeholders, List<string> errors, string pageTitle)
    {
        var token = $"{{{{{placeholderName}}}}}";
        if (template.Contains(token, StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrEmpty(value))
            {
                placeholders[placeholderName] = value;
            }
            else
            {
                errors.Add($"Page '{pageTitle}': Missing value for {{{{{placeholderName}}}}}");
            }
        }
    }
    
    private string GenerateCategoriesXml(List<string> tags)
    {
        if (tags == null || tags.Count == 0)
        {
            return string.Empty;
        }
        
        return string.Join("\n        ", tags.Select(tag => $"<category>{System.Net.WebUtility.HtmlEncode(tag)}</category>"));
    }
    
    private void ProcessCustomItemPlaceholders(string template, PageModel page, Dictionary<string, string> cliArgs,
        Dictionary<string, string> placeholders, List<string> errors)
    {
        // Look for custom Item.* placeholders in template
        var customPlaceholderMatches = System.Text.RegularExpressions.Regex.Matches(template, 
            @"\{\{Item\.([A-Za-z][A-Za-z0-9_]*)\}\}", 
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        
        foreach (System.Text.RegularExpressions.Match match in customPlaceholderMatches)
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
                placeholders[placeholderName] = value;
            }
            else
            {
                errors.Add($"Page '{page.Title}': Missing value for {{{{{placeholderName}}}}}. " +
                          $"Checked CLI (--rss:{cliKey}), then PageModel.Metadata[\"{metadataKey}\"] (case insensitive).");
            }
        }
    }
    
    private bool IsStandardRssPlaceholder(string placeholderName)
    {
        var standardPlaceholders = new[] 
        {
            "Item.Title", "Item.Description", "Item.Link", "Item.Guid", 
            "Item.PubDate", "Item.CategoriesXml", "Item.ContentEncoded"
        };
        
        return standardPlaceholders.Any(sp => 
            string.Equals(sp, placeholderName, StringComparison.OrdinalIgnoreCase));
    }
    
    private string RemoveItemsSection(string content)
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
    
    private string? GetCustomTemplateFromCli(BlakeContext context)
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
    
    private string GetProjectPath(BlakeContext context)
    {
        return context.ProjectPath;
    }
    
    private async Task CreateDefaultTemplate(string templatePath)
    {
        // Ensure directory exists
        var directory = Path.GetDirectoryName(templatePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
        
        // Create template from README content
        var templateContent = GetDefaultTemplateContent();
        await File.WriteAllTextAsync(templatePath, templateContent);
    }
    
    private string GetDefaultTemplateContent()
    {
        return """
<?xml version="1.0" encoding="UTF-8"?>
<rss version="2.0" xmlns:content="http://purl.org/rss/1.0/modules/content/">
  <channel>
    <title>{{Title}}</title>
    <link>{{Link}}</link>
    <description>{{Description}}</description>
    <lastBuildDate>{{LastBuildDate}}</lastBuildDate>

    <Items>
      <item>
        <title>{{Item.Title}}</title>
        <link>{{Item.Link}}</link>
        <guid isPermaLink="true">{{Item.Guid}}</guid>
        <pubDate>{{Item.PubDate}}</pubDate>
        <description><![CDATA[{{Item.Description}}]]></description>
        {{Item.CategoriesXml}}
        {{Item.ContentEncoded}}
      </item>
    </Items>
  </channel>
</rss>
""";
    }
}