# BlakePlugin.RSS

Always reference these instructions first and fallback to search or bash commands only when you encounter unexpected information that does not match the info here.

BlakePlugin.RSS is a .NET 9.0 library plugin for Blake static site generator that adds an RSS feed generator.

## Development Guidelines

**Always search Microsoft documentation (MS Learn) when working with .NET, Windows, or Microsoft features, or APIs.** Use the `microsoft_docs_search` tool to find the most current information about capabilities, best practices, and implementation patterns before making changes.

## Working Effectively

### Prerequisites and Environment Setup
- **CRITICAL**: Install .NET 9.0 SDK if not present. The project targets net9.0 and will not build with older versions.
- Download and install .NET 9.0: `wget https://dot.net/v1/dotnet-install.sh -O dotnet-install.sh && chmod +x dotnet-install.sh && ./dotnet-install.sh --version 9.0.101`
- Set environment variables: `export PATH="/home/runner/.dotnet:$PATH"` and `export DOTNET_ROOT="/home/runner/.dotnet"`
- Verify installation: `dotnet --version` should return 9.0.101 or higher

### Build and Package Commands
- **Restore packages**: `dotnet restore` -- takes ~9 seconds. NEVER CANCEL. Set timeout to 3+ minutes.
- **Build (Debug)**: `dotnet build` -- takes ~8.7 seconds. NEVER CANCEL. Set timeout to 3+ minutes.
- **Build (Release)**: `dotnet build --configuration Release` -- takes ~8.7 seconds. NEVER CANCEL. Set timeout to 3+ minutes.
- **Create NuGet package**: `dotnet pack --configuration Release` -- takes ~2.2 seconds. Set timeout to 2+ minutes.

### Code Quality and Validation
- **Format validation**: `dotnet format --verify-no-changes` -- currently fails with 25+ whitespace formatting errors. This is a known issue but does NOT prevent building.
- **Apply code formatting**: `dotnet format` -- fixes whitespace issues automatically. Run this before committing changes.
- **No unit tests exist**: `dotnet test` passes but runs zero tests. This is expected - the project has no test projects.
- **IMPORTANT**: PRs should NOT include formatting changes. Only modify files that are directly related to the issue being addressed. Do not run `dotnet format` as part of regular PR changes.

### Manual Validation and Testing
Since this is a Blake plugin library, validation requires integrating with the Blake ecosystem:

1. **Install Blake CLI**: `dotnet tool install --global blake.cli`
2. **Set Blake environment**: Ensure `export DOTNET_ROOT="/home/runner/.dotnet"` and `export PATH="/home/runner/.dotnet:/home/runner/.dotnet/tools:$PATH"`
3. **Create test site**: `blake new test-site`
4. **Add plugin reference**: Edit the test-site.csproj to include `<ProjectReference Include="[path-to-plugin]/src/BlakePlugin.DocsRenderer.csproj" />`
5. **Test plugin**: `blake bake test-site` -- should complete successfully
6. **Serve site**: `blake serve test-site` -- validates full integration

### CI Validation Commands
- The GitHub Actions workflow in `.github/workflows/ci.yml` runs the same build commands
- **Do NOT run** `dotnet format` in PRs unless specifically fixing formatting issues
- **Always test** your changes by running the full build pipeline: restore → build → pack

## Common Tasks and File Locations

### Key Project Structure
```
/
├── src/                           # Main source directory
│   ├── BlakePlugin.RSS.csproj     # Main project file (net9.0)
│   ├── Plugin.cs                  # Main plugin entry point
├── .github/workflows/ci.yml       # CI build pipeline
├── BlakePlugin.RSS.sln   # Visual Studio solution
└── README.md                      # Usage documentation
```

### Critical Build Times and Timeouts
- **NEVER CANCEL BUILDS**: All operations complete within 10 seconds under normal conditions
- **Set minimum 3-minute timeouts** for all dotnet commands to handle slow networks or first-time package downloads
- **Expected timing**:
  - `dotnet restore`: ~9 seconds (first time may take longer)
  - `dotnet build`: ~8.7 seconds  
  - `dotnet pack`: ~2.2 seconds
  - `dotnet format`: ~10 seconds

### Known Issues and Limitations
1. **Code formatting**: Project has whitespace formatting issues that must be fixed with `dotnet format` before committing
2. **No tests**: Zero unit tests exist. Validation requires manual Blake integration testing
3. **Environment dependency**: Requires exact .NET 9.0 - will not build with .NET 8.0 or earlier
4. **Blake dependency**: Runtime testing requires Blake CLI and understanding of Blake static site generator workflow

### Plugin Architecture

BlakePlugin.RSS follows the standard Blake plugin architecture pattern:

#### Blake Plugin Interface
All Blake plugins implement the `IBlakePlugin` interface from `Blake.BuildTools`, which provides two main extension points:

```csharp
public interface IBlakePlugin
{
    Task BeforeBakeAsync(BlakeContext context, ILogger? logger = null);
    Task AfterBakeAsync(BlakeContext context, ILogger? logger = null);
}
```

#### Plugin Lifecycle
- **BeforeBakeAsync**: Executed before Blake processes the site content. Used for preprocessing tasks like validating configuration, setting up resources, or modifying content before the main bake process.
- **AfterBakeAsync**: Executed after Blake has processed all content. Used for post-processing tasks like generating additional files, optimizing output, or performing cleanup.

#### RSS Plugin Implementation
The RSS plugin specifically operates in the **AfterBakeAsync** phase to:

1. **Template Processing**: Reads the RSS template file (`wwwroot/feed.template.xml`)
2. **Content Extraction**: Accesses site content through `BlakeContext` to get posts/pages
3. **Placeholder Resolution**: Replaces template placeholders using a hierarchical resolution system:
   - CLI arguments (highest priority): `--rss:BaseUrl=https://example.com`
   - PageModel properties: `Title`, `Description`, `PublishedUtc`, `Slug`, `Tags`, `Html`
   - PageModel.Metadata dictionary: Custom metadata like `author`, `summary`, `audioUrl`
   - Default/derived values: Calculated values like permalinks and RFC 1123 dates
4. **Feed Generation**: Duplicates `<item>` templates for each post and writes final `wwwroot/feed.xml`

#### BlakeContext Integration
The `BlakeContext` provides access to:
- Site configuration and metadata
- Content index (all pages/posts)
- CLI arguments (accessible via pattern matching like `--rss:*`)
- Build environment information
- Logging services

#### Error Handling
The plugin implements fail-fast behavior:
- Missing required placeholders cause build failures with helpful error messages
- Clear indication of resolution order attempted
- Guidance on how to provide missing values via CLI or metadata

#### Zero Configuration Design
The plugin is designed to work with minimal setup:
- Requires only a template file placement
- Sensible defaults for most RSS fields
- CLI arguments available for customization without code changes
- Works with existing Blake site structure and content

### Dependencies
- **Blake.BuildTools**: Core Blake plugin interface and build tools
- **Microsoft.NET.SDK**: .NET SDK
- **Targets**: net9.0 framework only

Refer to the Blake code for further contex: https://github.com/matt-goldman/blake
You can also refer to the docs repo: https://github.com/matt-goldman/blakedocs
And the published version of the docs: https://blake-ssg.org

## Validation Scenarios

### After Making Changes
1. **Build validation**: Run `dotnet build --configuration Release` to ensure compilation succeeds
2. **Package creation**: Run `dotnet pack --configuration Release` to verify NuGet package generation
3. **Integration test**: Create a simple Blake site and verify the plugin loads and functions correctly
4. **Manual verification**: Check that generated TOCs, sections, and code highlighting work as expected in a Blake site
5. **Code formatting**: Only run `dotnet format` if specifically addressing formatting issues, not as part of regular changes

Always build and test your changes before committing. Avoid including formatting changes in PRs unless that's the specific issue being addressed.