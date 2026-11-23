using System.Diagnostics;
using System.Text.RegularExpressions;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using SystemX.Auth2.Server.Application;

namespace SystemX.Auth2.Server.Infrastructure.Operations;

/// <summary>
/// Logging (Serilog) and telemetry (OpenTelemetry). Logs are structured; every event carries <c>ApplicationId</c>, <c>ProjectKey</c>,
/// <c>TraceId</c> and <c>CorrelationId</c>. Properties that look like secrets are replaced by <c>***</c>. Request logging has no bodies and no query string.
/// Telemetry is exported with OTLP only when <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is set (the usual <c>OTEL_*</c> variables apply).
/// </summary>
public static class Auth2Observability
{
    /// <summary>Adds Serilog and OpenTelemetry to the host.</summary>
    public static WebApplicationBuilder AddAuth2Observability(this WebApplicationBuilder builder)
    {
        var otlp = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);
        var redis = !string.IsNullOrWhiteSpace(builder.Configuration["Auth2:Redis:ConnectionString"]);

        // Serilog is one logging provider next to the others (tests add their own). The logger is built when the first service asks for it,
        // because the enricher needs the service provider.
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<Serilog.ILogger>(sp =>
        {
            var logger = new LoggerConfiguration()
                .ReadFrom.Configuration(sp.GetRequiredService<IConfiguration>())
                .Enrich.FromLogContext()
                .Enrich.With(new Auth2Enricher(sp.GetRequiredService<IHttpContextAccessor>()))
                .Enrich.With<SecretRedactor>()
                .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}");
            if (otlp) logger.WriteTo.OpenTelemetry();   // endpoint and headers come from the OTEL_* variables
            return logger.CreateLogger();
        });
        builder.Services.AddSingleton(sp => new Serilog.Extensions.Hosting.DiagnosticContext(sp.GetRequiredService<Serilog.ILogger>()));
        builder.Services.AddSingleton<Serilog.IDiagnosticContext>(sp => sp.GetRequiredService<Serilog.Extensions.Hosting.DiagnosticContext>());
        builder.Services.AddSingleton<ILoggerProvider>(sp => new Serilog.Extensions.Logging.SerilogLoggerProvider(sp.GetRequiredService<Serilog.ILogger>(), dispose: true));

        var telemetry = builder.Services.AddOpenTelemetry().ConfigureResource(r => r.AddService("SystemX.Auth2"));
        telemetry.WithTracing(t =>
        {
            t.AddAspNetCoreInstrumentation(o => o.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health"))
                .AddHttpClientInstrumentation()
                .AddEntityFrameworkCoreInstrumentation()
                .AddSource("Npgsql");   // Npgsql writes its own traces
            if (redis) t.AddRedisInstrumentation();
        });
        telemetry.WithMetrics(m => m.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddMeter(Auth2Metrics.MeterName));
        if (otlp) telemetry.UseOtlpExporter();
        return builder;
    }

    /// <summary>Request logging without bodies and without the query string (links can hold tokens).</summary>
    public static WebApplication UseAuth2RequestLogging(this WebApplication app)
    {
        app.Use(async (http, next) =>
        {
            var id = http.Request.Headers["X-Correlation-Id"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(id) || id.Length > 64 || !Regex.IsMatch(id, "^[A-Za-z0-9._-]+$", RegexOptions.None, TimeSpan.FromMilliseconds(50))) id = http.TraceIdentifier;
            http.Items["Auth2.CorrelationId"] = id;
            http.Response.Headers["X-Correlation-Id"] = id;
            using (Serilog.Context.LogContext.PushProperty("CorrelationId", id)) await next();
        });
        app.UseSerilogRequestLogging(o =>
        {
            o.Logger = app.Services.GetRequiredService<Serilog.ILogger>();
            o.GetLevel = (ctx, _, ex) =>
            ex is not null || ctx.Response.StatusCode >= 500 ? LogEventLevel.Error
            : ctx.Request.Path.StartsWithSegments("/health") ? LogEventLevel.Verbose : LogEventLevel.Information;
        });
        return app;
    }
}

/// <summary>Adds the application, trace id and correlation id to every log event.</summary>
internal sealed class Auth2Enricher(IHttpContextAccessor http) : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory factory)
    {
        if (Activity.Current is { } activity) logEvent.AddPropertyIfAbsent(factory.CreateProperty("TraceId", activity.TraceId.ToString()));
        var context = http.HttpContext;
        if (context is null) return;
        if (context.Items.TryGetValue("Auth2.CorrelationId", out var c) && c is string correlation) logEvent.AddPropertyIfAbsent(factory.CreateProperty("CorrelationId", correlation));
        var scope = context.RequestServices?.GetService<IApplicationScope>();
        if (scope?.ApplicationId is { } id) logEvent.AddPropertyIfAbsent(factory.CreateProperty("ApplicationId", id));
        if (scope?.CurrentProjectKey is { } key) logEvent.AddPropertyIfAbsent(factory.CreateProperty("ProjectKey", key));
    }
}

/// <summary>Replaces the value of properties whose name suggests a secret. A last safety net: code must not log secrets in the first place.</summary>
internal sealed class SecretRedactor : ILogEventEnricher
{
    private static readonly string[] Words = ["password", "secret", "token", "authorization", "otp", "recoverycode", "authenticatorkey", "apikey", "cookie", "code"];

    public static bool IsSensitive(string name)
    {
        var n = name.ToLowerInvariant();
        return Words.Any(w => n.Contains(w, StringComparison.Ordinal)) && !n.EndsWith("count", StringComparison.Ordinal) && n != "statuscode";
    }

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory factory)
    {
        foreach (var name in logEvent.Properties.Keys.Where(IsSensitive).ToList())
            logEvent.AddOrUpdateProperty(new LogEventProperty(name, new ScalarValue("***")));
    }
}
