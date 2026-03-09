using Microsoft.AspNetCore.Builder;
using Serilog;

namespace DreamGenClone.Infrastructure.Logging;

public static class LoggingSetup
{
    public static void ConfigureSerilog(WebApplicationBuilder builder)
    {
        builder.Host.UseSerilog((context, services, configuration) =>
        {
            configuration
                .ReadFrom.Configuration(context.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext();
        });
    }
}
