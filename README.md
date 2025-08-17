# Blake RSS Feed Plugin (MVP Spec)

This plugin generates an RSS feed for your Blake site with zero configuration.

## How it works

* You add a template file: wwwroot/feed.template.xml
* The plugin reads this template, duplicates the <item> seed inside <Items>…</Items> for each post in your content index, replaces placeholders, and writes the output to wwwroot/feed.xml.

## Setup

1. Add a template:

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
        {{Item.ContentEncoded}}
      </item>
    </Items>
  </channel>
</rss>
```

2. Update your csproj so the template isn’t served directly:
```xml
<ItemGroup>
  <None Include="wwwroot\feed.template.xml" />
  <Content Remove="wwwroot\feed.template.xml" />
</ItemGroup>
```

3. Install the NuGet package for the plugin.

That’s it - call `blake bake` and the plugin generates `wwwroot/feed.xml`.

## Placeholders

### Channel level

* `{{Title}}` → Site title
* `{{Link}}` → Base URL (must be provided with CLI arg if not in template)
* `{{Description}}` → Site description
* `{{LastBuildDate}}` → Current bake time (RFC 1123)

### Item level

* `{{Item.Title}}`
* `{{Item.Link}}` (absolute URL)
* `{{Item.Guid}}` (permalink or stable hash)
* `{{Item.PubDate}}` (RFC 1123 UTC)
* `{{Item.Description}}`
* `{{Item.CategoriesXml}}` → <category>…</category> tags from metadata
* `{{Item.ContentEncoded}}` → full HTML (if present in template)

### CLI arguments

You can replace or inject any token via CLI:
```bash
blake bake --rss:BaseUrl=https://example.com --rss:Title="My Blog"
```

Even custom tokens work:
```bash
--rss:cabbageId=x45Rg2   → replaces {{cabbageId}} anywhere in the template
```

Arguments are passed to all plugins via `BlakeContext` so this plugin can just look for anything matching this pattern.

### Behavior

* If a required placeholder has no value, the bake fails with a helpful error.
* Posts are ordered newest → oldest (default 20 items).
* All links are absolute.
* Valid RFC 1123 dates.

### Placeholder resolution order

When the plugin encounters a `{{Token}}`, it looks for a value in this order:

1. CLI argument
If you pass `--rss:BaseUrl=https://example.com`, it replaces `{{BaseUrl}}` or `{{Link}}` directly.

CLI always wins at the channel level, since it’s the most explicit.

2. PageModel properties
For item-level placeholders (`{{Item.Title}}`, `{{Item.Description}}`, etc.), the plugin first looks at strongly typed properties on the page/post model (`Title`, `Description`, `PublishedUtc`, `Slug`, `Tags`, `Html`).

3. PageModel.Metadata dictionary
If not found as a property, the plugin checks metadata (e.g. `author`, `summary`, `audioUrl`). This allows arbitrary keys to be surfaced without config.

4. Default/derived values
For some fields the plugin derives sensible defaults:

* `Item.Guid` → defaults to the permalink (`BaseUrl + slug`)
* `Item.Link` → always `BaseUrl + slug`
* `Item.PubDate` → defaults to `Date` (from the `PageModel`) in RFC 1123 UTC

5. Fail fast
If the token is still unresolved and appears in the template, the bake fails with a clear message:

```bash
RSS plugin error: Missing value for {{BaseUrl}}.
Checked CLI (--rss:BaseUrl), then PageModel.Metadata["BaseUrl"].
Provide a CLI argument or add a value to the template.
```
