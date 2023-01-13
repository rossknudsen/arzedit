using Microsoft.Extensions.Logging;

namespace ArzEdit.Service;

public static class Logger
{
    private static readonly ILoggerFactory LoggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(x =>
    {
        x.AddConsole();
    });

    public static ILogger Log { get; } = LoggerFactory.CreateLogger("Default");
}