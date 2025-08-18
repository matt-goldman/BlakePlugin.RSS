# Blake RSS Feed Plugin

A zero-configuration RSS feed generator plugin for [Blake](https://github.com/matt-goldman/blake) static sites.

## What it does

This plugin automatically generates an RSS feed for your Blake static site. Simply add a template file, and the plugin will create a complete RSS feed (`feed.xml`) with all your posts, properly formatted with absolute URLs, valid dates, and metadata.

Perfect for blogs, news sites, or any content site where you want readers to subscribe to updates via RSS.

## Installation

1. **Install the NuGet package** in your Blake site project:
   ```bash
   dotnet add package BlakePlugin.RSS
   ```

2. **Create a feed template** at `wwwroot/feed.template.xml`:
   ```xml
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
         </item>
       </Items>
     </channel>
   </rss>
   ```

3. **Exclude template from serving** (add to your `.csproj`):
   ```xml
   <ItemGroup>
     <Content Remove="wwwroot\feed.template.xml" />
   </ItemGroup>
   ```

4. **Build your site**:
   ```bash
   blake bake --rss:Link=https://yoursite.com
   ```
   
5. (Optional) Supply any additional metadata via command line options, such as:
   ```bash
   blake bake --rss:Title="My Blog" --rss:Description="Latest posts about tech"
   ```

That's it! Your RSS feed will be generated at `wwwroot/feed.xml`.

## Configuration

### Basic placeholders

The template supports these placeholders that are automatically filled:

**Feed level:**
- `{{Title}}` - Your site title
- `{{Link}}` - Your site URL (provide with `--rss:Link`)
- `{{Description}}` - Your site description
- `{{LastBuildDate}}` - Build timestamp (auto-generated)

**Post level:**
- `{{Item.Title}}` - Post title
- `{{Item.Link}}` - Post URL (absolute)
- `{{Item.Description}}` - Post description/excerpt
- `{{Item.PubDate}}` - Post publication date
- `{{Item.CategoriesXml}}` - Post tags as RSS categories

### CLI options

You can set any placeholder value via command line:

```bash
blake bake --rss:Title="My Awesome Blog" --rss:Description="Latest posts about web development"
```

For custom metadata, use any placeholder name:
```bash
blake bake --rss:author="Jane Doe" --rss:language="en-us"
```

**Content filtering and limiting:**

Control which pages are included in your RSS feed:

```bash
# Ignore specific paths (no leading slash needed)
blake bake --rss:ignore-paths="pages,drafts"

# Include only specific paths  
blake bake --rss:include-paths="blog,news"

# Limit number of items (default: 20)
blake bake --rss:max-items=10
```

**Path handling:**
- Leading slashes are automatically handled - specify `pages` instead of `/pages`
- Trailing slashes are honored - `page/` won't match `/pages/`  
- Multiple paths can be separated by commas or semicolons
- `ignore-paths` takes precedence if both include and ignore patterns match
- Items are automatically sorted by publication date (newest first)

**Backward compatibility:**
- `--rss:ignore-path` (singular) still works for single path exclusions

## Blake Resources

- **Blake Repository**: [https://github.com/matt-goldman/blake](https://github.com/matt-goldman/blake)
- **Blake Documentation**: [https://blake-ssg.org](https://blake-ssg.org)
- **Blake Getting Started**: [https://blake-ssg.org/docs/getting-started](https://blake-ssg.org/docs/getting-started)

## Roadmap

Post-MVP features under consideration:
- Support for RSS extensions (iTunes podcasting, media RSS)
- ~~Custom feed item limits and filtering~~ ✅ **Implemented**
- Multiple feed generation (e.g., category-specific feeds)
- RSS 2.0 extensions for enhanced metadata

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.