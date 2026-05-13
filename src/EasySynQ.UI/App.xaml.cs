using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;

using EasySynQ.Services.Abstractions;
using EasySynQ.Services.Identity;
using EasySynQ.UI.Identity;
using EasySynQ.UI.Navigation;
using EasySynQ.UI.Pulse;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Serilog;
using Serilog.Events;

namespace EasySynQ.UI;

/// <summary>
/// Application entry point. Owns the WPF startup sequence, the Serilog
/// logging pipeline, and the Microsoft.Extensions.Hosting
/// service-provider lifecycle.
/// </summary>
/// <remarks>
/// <para>
/// <b>Chunk E5.2.</b> Serilog is the sole logging provider. The static
/// <see cref="Log.Logger"/> is configured first (so bootstrap and
/// shutdown lines can be emitted before / after the host's lifecycle),
/// then wired into the host via
/// <see cref="LoggingBuilderExtensions.ClearProviders"/> +
/// <c>AddSerilog(dispose: true)</c> so every <see cref="ILogger{T}"/>
/// resolution in DI routes through the same configured sink set.
/// </para>
/// <para>
/// <b>Sink set.</b> A daily-rolling file sink writes to
/// <c>%LOCALAPPDATA%\EasySynQ\logs\easysynq-{yyyyMMdd}.log</c>; the
/// directory is created on startup since the File sink does not create
/// parents. The Debug sink mirrors output to the Visual Studio Output
/// window in any build. No Console sink — WPF has no console.
/// </para>
/// <para>
/// <b>Still to land in Chunk E5.</b> E5.3 installs a global
/// unhandled-exception handler. E5.4 swaps <c>LoginWindow</c> in as the
/// entry point and drives the login → shell flow. E5.5 adds the EF Core
/// migration check on startup.
/// </para>
/// </remarks>
public partial class App : Application
{
    private IHost? _host;

    /// <inheritdoc />
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ConfigureSerilog();

        Log.Information(
            "EasySynQ starting (assembly {AssemblyVersion})",
            Assembly.GetExecutingAssembly().GetName().Version);

        var builder = Host.CreateApplicationBuilder(e.Args);

        // Drop the framework's default providers (Console, Debug,
        // EventSource, EventLog) and route the host's logger factory
        // through Serilog instead. dispose: true ties the Serilog
        // pipeline's disposal to the host's logger-factory disposal,
        // which fires inside _host.Dispose() in OnExit.
        builder.Logging.ClearProviders();
        builder.Logging.AddSerilog(dispose: true);

        ConfigureServices(builder.Services);
        _host = builder.Build();
        _host.Start();

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    /// <inheritdoc />
    protected override void OnExit(ExitEventArgs e)
    {
        // Emit the shutdown line BEFORE disposing the host. The host's
        // logger factory carries Serilog with dispose: true, so
        // _host.Dispose() flushes and closes the Serilog pipeline; any
        // Log.* call after that point is a no-op.
        Log.Information("EasySynQ shutting down");

        if (_host is not null)
        {
            _host.StopAsync().GetAwaiter().GetResult();
            _host.Dispose();
            _host = null;
        }

        // Defensive — the AddSerilog(dispose: true) above already
        // flushed the pipeline. Belt and braces for the case where
        // OnStartup threw before the host was built (Log.Logger is set,
        // _host is null) and OnExit still fires.
        Log.CloseAndFlush();

        base.OnExit(e);
    }

    /// <summary>
    /// Configures the static <see cref="Log.Logger"/> with the file +
    /// debug sink set and the global level overrides. Called once at
    /// startup, before the host is built.
    /// </summary>
    private static void ConfigureSerilog()
    {
        var logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EasySynQ",
            "logs");

        // Serilog's File sink does not create parent directories.
        // CreateDirectory is idempotent if the path already exists.
        Directory.CreateDirectory(logDir);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Debug(formatProvider: CultureInfo.InvariantCulture)
            .WriteTo.File(
                path: Path.Combine(logDir, "easysynq-.log"),
                rollingInterval: RollingInterval.Day,
                formatProvider: CultureInfo.InvariantCulture,
                outputTemplate:
                    "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }

    /// <summary>
    /// Populates the host's service collection with the shell's
    /// dependency graph. Every registration is a singleton — view
    /// models own observable state bound 1:1 by views, the user
    /// accessor is shared app-wide state, and <see cref="MainWindow"/>
    /// can only be <c>Show()</c>-n once per instance.
    /// </summary>
    /// <param name="services">The service collection to populate.</param>
    private static void ConfigureServices(IServiceCollection services)
    {
        // Tile source. Real publishers ship in Phases 2 / 5 / 8 and
        // replace this registration one at a time.
        services.AddSingleton<IPulseSource, MockPulseSource>();

        // Current-user accessor: one concrete instance exposed under
        // three contracts. The forwarding factories guarantee both
        // interfaces resolve to the same singleton, so the future
        // login flow's SetCurrentUser write through the writable
        // interface is visible to every downstream consumer of
        // ICurrentUserAccessor (audit interceptor, standard-fields
        // interceptor, signature service).
        services.AddSingleton<WpfCurrentUserAccessor>();
        services.AddSingleton<IWritableCurrentUserAccessor>(
            sp => sp.GetRequiredService<WpfCurrentUserAccessor>());
        services.AddSingleton<ICurrentUserAccessor>(
            sp => sp.GetRequiredService<WpfCurrentUserAccessor>());

        services.AddSingleton<PulseDrawerViewModel>();
        services.AddSingleton<MainShellViewModel>();
        services.AddSingleton<MainWindow>();
    }
}
