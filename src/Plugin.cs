using Blake.BuildTools;
using Microsoft.Extensions.Logging;

namespace BlakePlugin.RSS;

public class Plugin : IBlakePlugin
{
    public Task BeforeBakeAsync(BlakeContext context, ILogger? logger = null)
    {
        throw new NotImplementedException();
    }

    public Task AfterBakeAsync(BlakeContext context, ILogger? logger = null)
    {
        throw new NotImplementedException();
    }
}