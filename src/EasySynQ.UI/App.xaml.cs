using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;

using EasySynQ.Data.Extensions;
using EasySynQ.Services.Abstractions;
using EasySynQ.Services.Audit;
using EasySynQ.Services.Identity;
using EasySynQ.Services.Time;
using EasySynQ.UI.Audit;
using EasySynQ.UI.Identity;
using EasySynQ.UI.Login;
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
/// logging pipeline, the Microsoft.Extensions.Hosting service-provider
/// lifecycle, and the global exception-handler safety net.
/// </summary>
/// <remarks>
/// <para>
/// <b>Chunk E5.3.</b> Wires the three global exception handlers
/// (UI-dispatcher, AppDomain-wide, unobserved <see cref="Task"/>) after
/// the host is up. They are the safety net under code that should
/// handle its own exceptions but did not — they exist to keep the app
/// alive (dispatcher case), to record the death rattle (AppDomain
/// case), and to keep background-task failures from silently rotting
/// the runtime (TaskScheduler case). Real fixes for the exceptions
/// they catch belong at the source, not here.
/// </para>
/// <para>
/// <b>Sink set (from E5.2).</b> A daily-rolling file sink writes to
/// <c>%LOCALAPPDATA%\EasySynQ\logs\easysynq-{yyyyMMdd}.log</c>; the
/// directory is created on startup since the File sink does not create
/// parents. The Debug sink mirrors output to the Visual Studio Output
/// window in any build. No Console sink — WPF has no console.
/// </para>
/// <para>
/// <b>Chunk E5.4.</b> <see cref="LoginWindow"/> is the entry point;
/// <see cref="MainWindow"/> is resolved lazily on successful sign-in.
/// The post-success handler populates
/// <see cref="IWritableCurrentUserAccessor"/> with the authenticated
/// user, logs the transition, opens MainWindow, then closes
/// LoginWindow — in that order, so default
/// <c>ShutdownMode.OnLastWindowClose</c> does not trigger app
/// shutdown during the zero-window gap.
/// </para>
/// <para>
/// <b>Still to land in Chunk E5.</b> E5.5 adds the EF Core migration
/// check on startup, ordered last so a failed migration can log via
/// the now-real logger and surface via the now-installed global
/// handler.
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

        ConfigureExceptionHandlers(_host);

        ConfigureLoginFlow(_host);
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
    /// dependency graph. UI singletons (view models, MainWindow, the
    /// current-user accessor) sit alongside the data layer's full
    /// registration (DbContext, interceptors, repositories,
    /// AuthenticationService) brought in via
    /// <see cref="ServiceCollectionExtensions.AddEasySynQDataServices"/>.
    /// LoginWindow and LoginViewModel are Transient — a fresh sign-in
    /// attempt (first-launch today; future sign-out re-login) gets a
    /// fresh view model with no stale lockout state and a fresh
    /// Window (WPF Window instances are single-<c>Show()</c>).
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
        //
        // Lifetime is Singleton (not Scoped as
        // AddEasySynQDataServices' doc-example suggests): app-wide
        // identity belongs to the app lifetime, not per EF scope;
        // Singleton-into-Scoped consumption is valid in MS DI.
        services.AddSingleton<WpfCurrentUserAccessor>();
        services.AddSingleton<IWritableCurrentUserAccessor>(
            sp => sp.GetRequiredService<WpfCurrentUserAccessor>());
        services.AddSingleton<ICurrentUserAccessor>(
            sp => sp.GetRequiredService<WpfCurrentUserAccessor>());

        // Cross-cutting abstractions consumed by the data layer's
        // interceptors and services (per AddEasySynQDataServices'
        // contract, the caller registers these first). All Singleton:
        // SystemClock and CurrentTimeTemporalResolver are stateless
        // time sources, UiAuditCorrelationProvider is immutable.
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<ITemporalResolver, CurrentTimeTemporalResolver>();
        services.AddSingleton<IAuditCorrelationProvider, UiAuditCorrelationProvider>();

        // Data layer: DbContext, both interceptors, generic +
        // concrete repositories, UnitOfWork, IPasswordPolicy,
        // IPasswordHasher, IAuthenticationService, ISignatureService.
        // Parent directory created before registration since neither
        // EF Core nor the SQLite provider creates it on open.
        var dbPath = GetDatabasePath();
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        services.AddEasySynQDataServices($"Data Source={dbPath}");

        services.AddTransient<LoginViewModel>();
        services.AddTransient<LoginWindow>();

        services.AddSingleton<PulseDrawerViewModel>();
        services.AddSingleton<MainShellViewModel>();
        services.AddSingleton<MainWindow>();
    }

    /// <summary>
    /// Full path to the EasySynQ SQLite master database. Single
    /// source of truth shared by host wiring (E5.4) and the migration
    /// check on startup (E5.5). Path is
    /// <c>%LOCALAPPDATA%\EasySynQ\db\EasySynQ_Master.db</c>; callers
    /// are responsible for creating the parent directory before use
    /// (same pattern as <see cref="ConfigureSerilog"/>'s log
    /// directory handling).
    /// </summary>
    private static string GetDatabasePath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EasySynQ",
            "db",
            "EasySynQ_Master.db");

    /// <summary>
    /// Wires the three global exception handlers — UI-dispatcher,
    /// AppDomain-wide, and unobserved <see cref="Task"/>. Captures
    /// <see cref="ILogger{App}"/> at wiring time so the two handlers
    /// that route through the host's logger factory do not pay a DI
    /// resolve cost at fire time.
    /// </summary>
    /// <remarks>
    /// Logger choice per handler is deliberately split. Do NOT
    /// normalize to a single logger source — see the per-handler
    /// remarks for the death-rattle reliability reasoning behind the
    /// AppDomain handler's use of static <see cref="Log.Logger"/>.
    /// </remarks>
    /// <param name="host">The host whose service provider supplies the
    /// captured logger.</param>
    private void ConfigureExceptionHandlers(IHost host)
    {
        var logger = host.Services.GetRequiredService<ILogger<App>>();

        DispatcherUnhandledException +=
            (_, args) => OnDispatcherUnhandled(logger, args);

        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandled;

        TaskScheduler.UnobservedTaskException +=
            (_, args) => OnUnobservedTaskException(logger, args);
    }

    /// <summary>
    /// Handles exceptions that escape to the WPF dispatcher. Logs at
    /// <see cref="LogLevel.Error"/>, surfaces a dialog so the user
    /// knows something went wrong, and sets <c>args.Handled = true</c>
    /// so the app continues running.
    /// </summary>
    /// <remarks>
    /// Uses the captured <see cref="ILogger{App}"/> rather than
    /// <see cref="Log.Logger"/>. The host is alive when this handler
    /// fires; routing through it preserves
    /// <c>SourceContext = "EasySynQ.UI.App"</c> attribution in log
    /// lines, which matters when the same log file accumulates
    /// emissions from multiple components.
    /// </remarks>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Last-resort handler — must not propagate exceptions back to the dispatcher.")]
    private static void OnDispatcherUnhandled(
        ILogger<App> logger,
        DispatcherUnhandledExceptionEventArgs args)
    {
        try
        {
            LogDispatcherUnhandled(logger, args.Exception);

            MessageBox.Show(
                "Something went wrong. The application will try to continue.\n\n" +
                $"{args.Exception.GetType().Name}: {args.Exception.Message}",
                "EasySynQ — Unexpected error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch (Exception)
        {
            // Logger or MessageBox itself failed. Swallow — propagating
            // from inside the dispatcher's last-resort handler risks
            // re-entry and a tight crash loop. The original exception
            // is already on its way to disk via whatever sink succeeded
            // earlier (or lost entirely if the very first logger call
            // failed; nothing else to try).
        }
        finally
        {
            // Mark handled even if log/dialog threw. The point of the
            // global handler is to keep the app alive past the original
            // exception, not to require that log + dialog both succeed
            // first.
            args.Handled = true;
        }
    }

    /// <summary>
    /// Handles exceptions that escape on non-dispatcher threads (the
    /// thread pool, the finalizer, anywhere outside WPF's loop). Logs
    /// at <see cref="LogEventLevel.Fatal"/> and flushes; no dialog —
    /// the dispatcher may not be pumping when this fires, and the
    /// process is terminating in any case.
    /// </summary>
    /// <remarks>
    /// Bypasses the host's logger factory and writes directly to
    /// <see cref="Log.Logger"/>. The factory may already be disposed
    /// by the time this fires (shutdown races, partial teardown);
    /// routing through <see cref="ILogger{T}"/> in that state silently
    /// writes to a closed Serilog pipeline and the death-rattle line
    /// is lost. The static path skips the factory entirely and writes
    /// straight to the configured sinks. The trade is loss of
    /// <c>SourceContext</c> attribution, which is acceptable because
    /// the AppDomain handler is wired in exactly one place and the
    /// line's origin is unambiguous.
    /// </remarks>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Last-resort handler in a terminating process — must not throw.")]
    private static void OnDomainUnhandled(object sender, UnhandledExceptionEventArgs args)
    {
        try
        {
            if (args.ExceptionObject is Exception ex)
            {
                Log.Fatal(
                    ex,
                    "Unhandled non-UI-thread exception (terminating: {IsTerminating})",
                    args.IsTerminating);
            }
            else
            {
                Log.Fatal(
                    "Unhandled non-UI-thread non-Exception object (terminating: {IsTerminating}): {ExceptionObject}",
                    args.IsTerminating,
                    args.ExceptionObject);
            }
        }
        catch (Exception)
        {
            // Process is dying. Whatever we could log is gone; the
            // global handler cannot help itself.
        }
        finally
        {
            // Maximize chance the log line hits disk before the process
            // exits. Wrapped because the close path could also throw
            // and we are still in the death rattle.
            try
            {
                Log.CloseAndFlush();
            }
            catch (Exception)
            {
                // Nothing else to try.
            }
        }
    }

    /// <summary>
    /// Handles <see cref="Task"/> exceptions that were never observed
    /// by an <c>await</c>, <c>Wait()</c>, <c>Result</c>, or
    /// <c>ContinueWith</c>. Logs at <see cref="LogLevel.Error"/> and
    /// calls <see cref="UnobservedTaskExceptionEventArgs.SetObserved"/>
    /// so the runtime does not escalate.
    /// </summary>
    /// <remarks>
    /// Uses the captured <see cref="ILogger{App}"/> for the same
    /// reason as the dispatcher handler — the host is normally alive
    /// when this fires (finalizer thread, not a process-terminating
    /// path). No dialog: unobserved-task exceptions are often
    /// fire-and-forget background work whose visible failure mode is
    /// "nothing at all," and surfacing every one would be noisy and
    /// unactionable for the user.
    /// </remarks>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Last-resort handler — must not propagate exceptions to the finalizer.")]
    private static void OnUnobservedTaskException(
        ILogger<App> logger,
        UnobservedTaskExceptionEventArgs args)
    {
        try
        {
            LogUnobservedTaskException(logger, args.Exception);
        }
        catch (Exception)
        {
            // Logger failed; nothing else to try at finalizer time.
        }
        finally
        {
            // Observe even if logging threw. Historical escalation
            // policy (.NET Framework 4.0 default) terminated the
            // process for unobserved task exceptions; modern runtimes
            // default to ignore, but observing keeps behavior explicit
            // regardless of host runtime policy.
            args.SetObserved();
        }
    }

    /// <summary>
    /// Source-generated emit for the dispatcher last-resort handler.
    /// The static-partial form lets <see cref="OnDispatcherUnhandled"/>
    /// stay static (no app-instance capture) while still using the
    /// compiled-template logging pattern the rest of the codebase
    /// relies on.
    /// </summary>
    [LoggerMessage(
        EventId = 5001,
        Level = LogLevel.Error,
        Message = "Unhandled UI-thread exception (dispatcher last resort)")]
    private static partial void LogDispatcherUnhandled(ILogger<App> logger, Exception exception);

    /// <summary>
    /// Source-generated emit for the unobserved-task last-resort
    /// handler. Same static-partial pattern as
    /// <see cref="LogDispatcherUnhandled"/>.
    /// </summary>
    [LoggerMessage(
        EventId = 5002,
        Level = LogLevel.Error,
        Message = "Unobserved task exception")]
    private static partial void LogUnobservedTaskException(ILogger<App> logger, AggregateException exception);

    /// <summary>
    /// Resolves <see cref="LoginWindow"/> from the host, subscribes
    /// the host-side <see cref="LoginViewModel.LoginSucceeded"/>
    /// handler, and shows the window. This is the only entry-point
    /// path — MainWindow is not resolved here; it is resolved lazily
    /// inside <see cref="OnLoginSucceeded"/> after the user
    /// authenticates.
    /// </summary>
    /// <remarks>
    /// The dependencies (<see cref="IWritableCurrentUserAccessor"/>
    /// and <see cref="ILogger{App}"/>) are captured at wiring time so
    /// the handler does not pay a DI resolve cost when the event
    /// fires. The captured <see cref="IHost"/> is needed for the lazy
    /// <see cref="MainWindow"/> resolve.
    /// </remarks>
    /// <param name="host">The host providing the login surface and
    /// its dependencies.</param>
    private static void ConfigureLoginFlow(IHost host)
    {
        var loginWindow = host.Services.GetRequiredService<LoginWindow>();
        var accessor = host.Services.GetRequiredService<IWritableCurrentUserAccessor>();
        var logger = host.Services.GetRequiredService<ILogger<App>>();

        loginWindow.ViewModel.LoginSucceeded += (_, args) =>
            OnLoginSucceeded(host, loginWindow, accessor, logger, args);

        loginWindow.Show();
    }

    /// <summary>
    /// Post-authentication transition. Populates the current-user
    /// accessor, logs the sign-in success, resolves and shows
    /// <see cref="MainWindow"/>, then closes
    /// <paramref name="loginWindow"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Order matters.</b>
    /// </para>
    /// <list type="number">
    /// <item><c>SetCurrentUser</c> first so any binding evaluated
    /// during MainWindow construction or Show — including
    /// MainShellViewModel's user-chip bindings — observes a populated
    /// identity rather than the empty default.</item>
    /// <item><c>MainWindow.Show()</c> before <c>LoginWindow.Close()</c>
    /// so the app is never in a zero-windows state. Default
    /// <c>ShutdownMode.OnLastWindowClose</c> would initiate shutdown
    /// in that gap and race the pending MainWindow show.</item>
    /// </list>
    /// <para>
    /// <b>Role-name placeholder.</b> The literal <c>"Authenticated User"</c>
    /// string is a deliberate placeholder until the real role-resolution
    /// design lands (Phase 1 Follow-Up: plumb the authenticated user's
    /// effective role through <see cref="AuthenticationResult.Success"/>
    /// and <c>AuthenticatedUserEventArgs</c>; ADR 0006 amendment
    /// required to define semantics — single role / primary role /
    /// selected-at-login). Grep for the literal string to find both
    /// this call site and the future replacement point.
    /// </para>
    /// </remarks>
    private static void OnLoginSucceeded(
        IHost host,
        LoginWindow loginWindow,
        IWritableCurrentUserAccessor accessor,
        ILogger<App> logger,
        AuthenticatedUserEventArgs args)
    {
        accessor.SetCurrentUser(args.User, "Authenticated User");
        LogSignInSucceeded(logger, args.User.Username);

        var mainWindow = host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
        loginWindow.Close();
    }

    /// <summary>
    /// Source-generated emit for the successful-sign-in entry-point
    /// transition. Fires once per app session (until sign-out / re-login
    /// flows arrive). The matching failure emit is
    /// <see cref="LoginViewModel"/>'s <c>LogSignInSystemError</c>
    /// (EventId 1001).
    /// </summary>
    [LoggerMessage(
        EventId = 6001,
        Level = LogLevel.Information,
        Message = "User {Username} signed in successfully.")]
    private static partial void LogSignInSucceeded(ILogger<App> logger, string username);
}
