using Blake.BuildTools;
using Microsoft.Extensions.Logging;

namespace BlakePlugin.RSS;

public class Plugin : IBlakePlugin
{
    private const string DefaultTemplateFileName = "feed.template.xml";
    
    public async Task AfterBakeAsync(BlakeContext context, ILogger? logger = null)
    {
        // Check for CLI argument --rss:template=[file]
        var customTemplatePath = RssArgumentParser.GetCustomTemplateFromCli(context);
        
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
                await RssTemplateManager.CreateDefaultTemplate(templatePath);
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
        var cliArgs = RssArgumentParser.ExtractRssCliArguments(context);
        
        // Process channel level placeholders
        var (processedContent, baseUrl) = RssFeedGenerator.ProcessChannelPlaceholders(templateContent, cliArgs);
        
        // Validate that required RSS elements exist in the processed content
        RssValidator.ValidateRequiredRssElements(processedContent);
        
        // Process Items section - extract template and generate items for each page
        processedContent = RssFeedGenerator.ProcessItemsSection(processedContent, context, cliArgs, baseUrl);
        
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
    
    private string GetProjectPath(BlakeContext context)
    {
        return context.ProjectPath;
    }
}