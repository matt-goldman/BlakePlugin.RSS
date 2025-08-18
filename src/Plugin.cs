using Blake.BuildTools;
using Microsoft.Extensions.Logging;

namespace BlakePlugin.RSS;

public class Plugin : IBlakePlugin
{
    private const string DefaultTemplateFileName = "feed.template.xml";
    private const string RssTemplateCliPrefix = "--rss:template=";
    private const string RssCliPrefix = "--rss:";
    
    public async Task AfterBakeAsync(BlakeContext context, ILogger? logger = null)
    {
        // Check for CLI argument --rss:template=[file]
        string? customTemplatePath = GetCustomTemplateFromCli(context);
        
        string templatePath;
        bool isCustomTemplate = !string.IsNullOrEmpty(customTemplatePath);
        
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
        string templateContent = await File.ReadAllTextAsync(templatePath);
        logger?.LogInformation($"Processing RSS template from: {templatePath}");
        
        // Extract CLI arguments
        var cliArgs = ExtractRssCliArguments(context);
        
        // Process channel level placeholders
        string processedContent = ProcessChannelPlaceholders(templateContent, cliArgs);
        
        // Remove Items section for now (will be implemented later)
        processedContent = RemoveItemsSection(processedContent);
        
        // Write to feed.xml
        string outputPath = Path.Combine(GetProjectPath(context), "wwwroot", "feed.xml");
        
        // Ensure wwwroot directory exists
        string? outputDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDirectory) && !Directory.Exists(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }
        
        await File.WriteAllTextAsync(outputPath, processedContent);
        logger?.LogInformation($"RSS feed generated at: {outputPath}");
    }
    
    private Dictionary<string, string> ExtractRssCliArguments(BlakeContext context)
    {
        var rssArgs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        
        foreach (string arg in context.Arguments)
        {
            if (arg.StartsWith(RssCliPrefix, StringComparison.OrdinalIgnoreCase))
            {
                string keyValue = arg.Substring(RssCliPrefix.Length);
                int equalsIndex = keyValue.IndexOf('=');
                if (equalsIndex > 0)
                {
                    string key = keyValue.Substring(0, equalsIndex);
                    string value = keyValue.Substring(equalsIndex + 1);
                    rssArgs[key] = value;
                }
            }
        }
        
        return rssArgs;
    }
    
    private string ProcessChannelPlaceholders(string content, Dictionary<string, string> cliArgs)
    {
        // Channel level placeholders to process
        var placeholders = new Dictionary<string, string>();
        
        // LastBuildDate - always auto-generated
        placeholders["LastBuildDate"] = DateTime.UtcNow.ToString("R"); // RFC 1123 format
        
        // Process mandatory fields: Title, Description, Link
        ProcessMandatoryPlaceholder(content, cliArgs, placeholders, "Title");
        ProcessMandatoryPlaceholder(content, cliArgs, placeholders, "Description");
        ProcessMandatoryPlaceholder(content, cliArgs, placeholders, "Link");
        
        // Validate Link if provided
        if (placeholders.ContainsKey("Link"))
        {
            ValidateUrl(placeholders["Link"]);
        }
        
        // Replace placeholders in content
        foreach (var placeholder in placeholders)
        {
            string token = $"{{{{{placeholder.Key}}}}}";
            content = content.Replace(token, placeholder.Value);
        }
        
        return content;
    }
    
    private void ProcessMandatoryPlaceholder(string content, Dictionary<string, string> cliArgs, 
        Dictionary<string, string> placeholders, string fieldName)
    {
        string token = $"{{{{{fieldName}}}}}";
        bool hasPlaceholder = content.Contains(token);
        
        // Check CLI first (highest priority)
        if (cliArgs.TryGetValue(fieldName, out string? cliValue))
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
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? parsedUri))
        {
            throw new InvalidOperationException($"RSS plugin error: Link URL '{url}' is not a valid URL format.");
        }
        
        // Ensure it's http or https
        if (parsedUri.Scheme != "http" && parsedUri.Scheme != "https")
        {
            throw new InvalidOperationException($"RSS plugin error: Link URL must use http or https scheme, got '{parsedUri.Scheme}'.");
        }
    }
    
    private string RemoveItemsSection(string content)
    {
        // Find and remove the <Items>...</Items> section
        int itemsStart = content.IndexOf("<Items>", StringComparison.OrdinalIgnoreCase);
        int itemsEnd = content.IndexOf("</Items>", StringComparison.OrdinalIgnoreCase);
        
        if (itemsStart >= 0 && itemsEnd >= 0)
        {
            // Remove the entire Items section
            string beforeItems = content.Substring(0, itemsStart).TrimEnd();
            string afterItems = content.Substring(itemsEnd + "</Items>".Length).TrimStart();
            
            // Join with appropriate spacing
            content = beforeItems + "\n\n" + afterItems;
        }
        
        return content;
    }
    
    private string? GetCustomTemplateFromCli(BlakeContext context)
    {
        // Look for --rss:template= in CLI arguments
        foreach (string arg in context.Arguments)
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
        string? directory = Path.GetDirectoryName(templatePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
        
        // Create template from README content
        string templateContent = GetDefaultTemplateContent();
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