using Blake.BuildTools;
using Microsoft.Extensions.Logging;

namespace BlakePlugin.RSS;

public class Plugin : IBlakePlugin
{
    private const string DefaultTemplateFileName = "feed.template.xml";
    private const string RssTemplateCliPrefix = "--rss:template=";
    
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
        
        // Template exists - continue with RSS generation (placeholder for now)
        logger?.LogInformation($"RSS template found at: {templatePath}");
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