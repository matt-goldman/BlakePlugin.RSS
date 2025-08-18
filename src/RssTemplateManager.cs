namespace BlakePlugin.RSS;

/// <summary>
/// Manager class for RSS template creation and management.
/// </summary>
internal static class RssTemplateManager
{
    /// <summary>
    /// Creates a default RSS template at the specified path.
    /// </summary>
    /// <param name="templatePath">The path where the template should be created</param>
    public static async Task CreateDefaultTemplate(string templatePath)
    {
        // Ensure directory exists
        var directory = Path.GetDirectoryName(templatePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
        
        // Create template from default content
        var templateContent = GetDefaultTemplateContent();
        await File.WriteAllTextAsync(templatePath, templateContent);
    }

    /// <summary>
    /// Gets the default RSS template content.
    /// </summary>
    /// <returns>The default template content as a string</returns>
    public static string GetDefaultTemplateContent()
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