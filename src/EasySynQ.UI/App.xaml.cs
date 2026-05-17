using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;

using EasySynQ.Data.Context;
using EasySynQ.Data.Extensions;
using EasySynQ.Domain.Entities.Documents;
using EasySynQ.Services.Abstractions;
using EasySynQ.Services.Audit;
using EasySynQ.Services.Bootstrap;
using EasySynQ.Services.Identity;
using EasySynQ.Services.Time;
using EasySynQ.UI.Audit;
using EasySynQ.UI.Bootstrap;
using EasySynQ.UI.Documents;
using EasySynQ.UI.Documents.CreateDocument;
using EasySynQ.UI.Documents.Detail;
using EasySynQ.UI.Documents.EditMetadata;
using EasySynQ.UI.Documents.ReturnToDraft;
using EasySynQ.UI.Documents.ReviewAndSign;
using EasySynQ.UI.Documents.SubmitForReview;
using EasySynQ.UI.Documents.List;
using EasySynQ.UI.Identity;
using EasySynQ.UI.LockInspector;
using EasySynQ.UI.Login;
using EasySynQ.UI.Navigation;
using EasySynQ.UI.Pulse;
using EasySynQ.UI.Signing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;

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
/// <b>Chunk E5.5.</b> Applies any pending EF Core migrations between
/// the exception-handler install and the login flow. First-run
/// creates the full schema (the prod DB at
/// <c>%LOCALAPPDATA%\EasySynQ\db\EasySynQ_Master.db</c> is empty
/// until E5.5 runs); steady-state is a no-op. A migration failure
/// logs Critical, shows a "Cannot start" dialog, and queues an
/// exit-code-1 shutdown — the global dispatcher handler's
/// "keep the app alive" contract is wrong for a database that
/// cannot accept writes.
/// </para>
/// <para>
/// <b>Chunk F1 Commit 2.</b> Bootstrap detection runs between the
/// migration check and the login flow. When
/// <see cref="IBootstrapService.IsBootstrapRequiredAsync"/> returns
/// <see langword="true"/> (empty Users table after migrations
/// applied), routes to <see cref="BootstrapWindow"/> instead of
/// <see cref="LoginWindow"/>; on successful bootstrap, mirrors the
/// post-login transition (<c>SetCurrentUser</c>, log success, open
/// MainWindow, close BootstrapWindow). The idempotency-guard
/// failure path (a user appeared between detection and create) shows
/// a "setup not required" dialog and exits cleanly with code 0; the
/// next launch routes to LoginWindow because the user count is now
/// non-zero.
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

        if (!ApplyPendingMigrations(_host))
        {
            return;
        }

        if (!CheckWebView2Runtime(_host))
        {
            return;
        }

        if (IsBootstrapRequired(_host))
        {
            ConfigureBootstrapFlow(_host);
        }
        else
        {
            ConfigureLoginFlow(_host);
        }
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
                // ~90 files at the daily roll cadence ≈ 90 days of
                // history. The file sink is operational/diagnostic
                // only — compliance evidence lives in the audit log
                // table (SPEC §7.3), not in these files, so the
                // retention bound is tuned for debug usefulness, not
                // for any auditor-facing retention requirement.
                retainedFileCountLimit: 90,
                formatProvider: CultureInfo.InvariantCulture,
                outputTemplate: FileSinkOutputTemplate)
            .CreateLogger();
    }

    /// <summary>
    /// Output template for the daily-rolling file sink. Extracted to a
    /// named constant so the test project can pin its format against
    /// accidental regressions and so future format changes
    /// (timestamp adjustments, exception rendering, level width) have
    /// one canonical home.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>EventId is intentionally not rendered inline.</b> An earlier
    /// Phase 1 follow-up proposed adding <c>{EventId}</c> to the
    /// template for raw-log grep convenience. Investigation closed
    /// the item as not-feasible: Serilog's output-template grammar
    /// does not support nested property access (so
    /// <c>{EventId.Id}</c> cannot reach into the <c>StructureValue</c>
    /// the <c>Serilog.Extensions.Logging</c> adapter produces from
    /// <see cref="Microsoft.Extensions.Logging.EventId"/>), and bare
    /// <c>{EventId}</c> renders the verbose
    /// <c>[{ Id: 6001, Name: "…" }]</c> structured form that uglifies
    /// every text line. The EventId property remains attached to each
    /// <see cref="Serilog.Events.LogEvent"/> for structured-log
    /// consumers (JSON sink, Seq, future aggregators); only the raw
    /// text rendering omits it. See the 2026-05-14 entry in
    /// docs/SESSION_NOTES.md for the full rationale.
    /// </para>
    /// </remarks>
    internal const string FileSinkOutputTemplate =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}";

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

        services.AddTransient<BootstrapViewModel>();
        services.AddTransient<BootstrapWindow>();

        services.AddSingleton<PulseDrawerViewModel>();
        services.AddSingleton<NavigationContentFactory>();
        services.AddSingleton<MainShellViewModel>();
        services.AddSingleton<MainWindow>();

        // ADR 0009 C4 — signature role prompter scaffolding. The
        // dialog itself is constructed per call by the prompter (a
        // Window can only ShowDialog once), so only the prompter is
        // DI-registered. Singleton because the prompter is stateless
        // and composes with the singleton role-resolution service.
        services.AddSingleton<ISignatureRolePrompter, SignatureRolePrompter>();

        // ADR 0008 C6a — Document Controller UI wiring.
        //
        // File picker abstraction over Microsoft.Win32.OpenFileDialog.
        // Stateless wrapper; singleton.
        services.AddSingleton<IFilePicker, WpfFilePicker>();

        // Modal prompters mirror the SignatureRolePrompter pattern —
        // singletons that construct fresh Windows per call (Windows
        // are single-ShowDialog) and translate dialog results into
        // nullable record returns.
        services.AddSingleton<ICreateDocumentPrompter, CreateDocumentPrompter>();
        services.AddSingleton<IEditMetadataPrompter, EditMetadataPrompter>();
        // C6b — submit-for-review prompter. Singleton like the rest;
        // the dialog VM is heavy (loads candidates + invokes
        // role-prompter + calls lifecycle service internally) per
        // plan §C.
        services.AddSingleton<ISubmitForReviewPrompter, SubmitForReviewPrompter>();
        // C6b — review-and-sign prompter. Same shape as
        // SubmitForReview; resolves role on Loaded, signs on
        // command, surfaces post-sign state.
        services.AddSingleton<IReviewAndSignPrompter, ReviewAndSignPrompter>();
        // C6b — return-to-draft prompter. Lightest of the three:
        // no role prompter (per the stop-5 plan-vs-service
        // reconciliation, ReturnToDraft is not a signed transition);
        // dialog captures the required reason and forwards to the
        // lifecycle service.
        services.AddSingleton<IReturnToDraftPrompter, ReturnToDraftPrompter>();
        // C7b — lock-inspector prompter (ADR 0012). Singleton like
        // the other prompters; the prompter holds the scoped lock-
        // reason repository + resolver registry as captive
        // dependencies, matching the project-wide prompter pattern.
        services.AddSingleton<ILockInspectorPrompter, LockInspectorPrompter>();

        // List view model — transient so navigating to the documents
        // entry produces a fresh VM each time (the factory is
        // re-invoked on every NavigationContentFactory.CreateContentFor
        // call). Detail VM construction is parameterized by Document
        // and goes through the factory delegate below.
        services.AddTransient<DocumentListViewModel>();

        // Detail view model factory delegate — Func<Document, VM>
        // resolved by the list VM. ActivatorUtilities populates the
        // remaining constructor parameters from the scoped provider;
        // the Document parameter is supplied at call time.
        services.AddSingleton<Func<Document, DocumentDetailViewModel>>(
            sp => doc => ActivatorUtilities.CreateInstance<DocumentDetailViewModel>(sp, doc));
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
    /// Applies any pending EF Core migrations to the registered
    /// <see cref="EasySynQDbContext"/>. Returns <c>true</c> if the
    /// database is ready (no pending migrations, or migrations applied
    /// successfully) and <c>false</c> if migration failed and an
    /// exit-code-1 shutdown has been queued.
    /// </summary>
    /// <remarks>
    /// <para>
    /// On first run the production database file is created by the
    /// SQLite provider when EF opens its connection; without this
    /// step the file exists but has no schema, and every subsequent
    /// query throws <c>SqliteException: no such table</c>. On
    /// steady-state runs the helper observes zero pending migrations
    /// and returns silently — no log line, per the no-op-suppression
    /// rule (keeps the steady-state log readable instead of stamping
    /// "nothing to do" on every launch).
    /// </para>
    /// <para>
    /// <b>Why try/catch instead of relying on the global dispatcher
    /// handler.</b> The dispatcher handler's contract is "log,
    /// dialog, keep the app alive." A database that cannot accept
    /// writes makes the app unusable; continuing would just produce
    /// cascading <c>SqliteException</c>s and a confused user. The
    /// catch here is terminal-state handling — log Critical, show
    /// one "Cannot start" dialog, queue a shutdown — not safety-net
    /// handling.
    /// </para>
    /// <para>
    /// <b>Why no extracted helper class for unit testing.</b> The
    /// happy path is exercised in every integration-test run via
    /// <c>TempSqliteDb.Create</c>, which calls
    /// <c>Database.Migrate()</c> against a fresh SQLite file. The
    /// failure path needs a corrupt DB or a buggy migration —
    /// neither reproducible against a fake DbContext. Extraction
    /// here would be ceremonial. Revisit if the helper grows
    /// decision logic (retry, partial-migration handling,
    /// categorization).
    /// </para>
    /// </remarks>
    /// <param name="host">The host providing the scoped DbContext
    /// and the host-side logger.</param>
    /// <returns><c>true</c> on success (schema applied or already
    /// up to date); <c>false</c> if migration failed (caller must
    /// short-circuit further startup wiring so the login window
    /// does not show during the queued shutdown).</returns>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Migration failure is terminal — catch-all so any provider, IO, or permission failure surfaces uniformly via dialog + log + shutdown.")]
    private static bool ApplyPendingMigrations(IHost host)
    {
        var logger = host.Services.GetRequiredService<ILogger<App>>();

        try
        {
            using var scope = host.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<EasySynQDbContext>();

            var pending = db.Database.GetPendingMigrations().ToList();
            if (pending.Count == 0)
            {
                return true;
            }

            LogMigrationsApplying(logger, pending.Count, pending);
            db.Database.Migrate();
            return true;
        }
        catch (Exception ex)
        {
            LogMigrationFailed(logger, ex);

            MessageBox.Show(
                "EasySynQ could not initialize its database and must exit. " +
                "Please contact support and reference the log file at " +
                "%LOCALAPPDATA%\\EasySynQ\\logs\\.\n\n" +
                $"{ex.GetType().Name}: {ex.Message}",
                "EasySynQ — Cannot start",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Current.Shutdown(1);
            return false;
        }
    }

    /// <summary>
    /// Source-generated emit for the migration-application path.
    /// Fires only when there are migrations to apply (steady-state
    /// launches produce no 6002 line). The destructuring prefix on
    /// <c>{@Migrations}</c> preserves the collection's structure
    /// for any future log-analysis tooling while still rendering
    /// as a readable JSON array in the file sink.
    /// </summary>
    [LoggerMessage(
        EventId = 6002,
        Level = LogLevel.Information,
        Message = "Applying {Count} pending migration(s): {@Migrations}")]
    private static partial void LogMigrationsApplying(
        ILogger<App> logger,
        int count,
        IReadOnlyList<string> migrations);

    /// <summary>
    /// Source-generated emit for migration failure. Fires once per
    /// failed startup; the matching user-visible surface is the
    /// "EasySynQ — Cannot start" dialog and the process exit code 1.
    /// </summary>
    [LoggerMessage(
        EventId = 6003,
        Level = LogLevel.Critical,
        Message = "Database migration failed; application cannot start.")]
    private static partial void LogMigrationFailed(
        ILogger<App> logger,
        Exception ex);

    /// <summary>
    /// Detects whether the Microsoft Edge WebView2 Runtime is
    /// installed (ADR 0010 C5). Returns <c>true</c> when present;
    /// returns <c>false</c> after logging Critical, showing a
    /// "WebView2 Runtime required" dialog, and queueing exit code 1.
    /// Mirrors <see cref="ApplyPendingMigrations"/>'s
    /// terminal-state shape.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why startup detection, not lazy.</b> The PDF viewer is the
    /// only feature that consumes WebView2 today, but reaching the
    /// document detail view and only then learning "this app needs a
    /// system component installed" is bad UX. Detecting at launch
    /// produces a clear actionable error before the user has invested
    /// effort. The check itself is cheap — a single
    /// <see cref="CoreWebView2Environment.GetAvailableBrowserVersionString()"/>
    /// call that returns a string or throws.
    /// </para>
    /// <para>
    /// <b>Why try/catch + Shutdown(1) instead of the global handler.</b>
    /// Same rationale as <see cref="ApplyPendingMigrations"/>: a missing
    /// WebView2 Runtime is a terminal startup state, not a "log + dialog
    /// + keep running" case. The dispatcher handler's contract is
    /// "keep the app alive"; that's wrong here because the app cannot
    /// render any document without the runtime.
    /// </para>
    /// </remarks>
    /// <param name="host">The host providing the host-side logger.</param>
    /// <returns><c>true</c> when the runtime is present;  <c>false</c>
    /// when it is missing (caller short-circuits further startup
    /// wiring so no window shows during the queued shutdown).</returns>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Runtime detection is terminal-state; catch-all so any failure shape (missing runtime, registry access denied, unexpected COM error) surfaces uniformly via dialog + log + shutdown.")]
    private static bool CheckWebView2Runtime(IHost host)
    {
        var logger = host.Services.GetRequiredService<ILogger<App>>();

        try
        {
            var version = CoreWebView2Environment.GetAvailableBrowserVersionString();
            // GetAvailableBrowserVersionString returns null when the
            // runtime is not installed (older WebView2 versions threw;
            // current versions return null). Treat both shapes as
            // "missing" via the same code path.
            if (string.IsNullOrWhiteSpace(version))
            {
                throw new WebView2RuntimeNotFoundException(
                    "Microsoft Edge WebView2 Runtime is not installed " +
                    "(GetAvailableBrowserVersionString returned null/empty).");
            }
            LogWebView2RuntimeDetected(logger, version);
            return true;
        }
        catch (Exception ex)
        {
            LogWebView2RuntimeMissing(logger, ex);

            MessageBox.Show(
                "EasySynQ requires the Microsoft Edge WebView2 Runtime to be installed. " +
                "Please contact your IT administrator and reference the deployment " +
                "documentation.\n\n" +
                "The WebView2 Runtime is a free Microsoft component available from " +
                "https://developer.microsoft.com/microsoft-edge/webview2/.",
                "EasySynQ — WebView2 Runtime required",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Current.Shutdown(1);
            return false;
        }
    }

    /// <summary>
    /// Source-generated emit for the runtime-detected path. Fires
    /// once per successful startup; the version string is logged so
    /// support can correlate viewer behavior to a specific runtime
    /// release.
    /// </summary>
    [LoggerMessage(
        EventId = 6006,
        Level = LogLevel.Information,
        Message = "WebView2 Runtime detected; version {WebView2Version}.")]
    private static partial void LogWebView2RuntimeDetected(
        ILogger<App> logger,
        string webView2Version);

    /// <summary>
    /// Source-generated emit for the runtime-missing path. Fires
    /// once per failed startup; the matching user-visible surface is
    /// the "WebView2 Runtime required" dialog and the process exit
    /// code 1.
    /// </summary>
    [LoggerMessage(
        EventId = 6007,
        Level = LogLevel.Critical,
        Message = "WebView2 Runtime missing; application cannot start.")]
    private static partial void LogWebView2RuntimeMissing(
        ILogger<App> logger,
        Exception ex);

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
    /// <b>Roles + permissions plumbing (ADR 0007).</b> The role and
    /// permission snapshots arrive in
    /// <paramref name="args"/> already resolved by the auth service at
    /// sign-in time and are passed verbatim to
    /// <see cref="IWritableCurrentUserAccessor.SetCurrentUser"/>. The
    /// session-long accessor state is fixed at this call site; no
    /// further re-resolution happens for the lifetime of the session.
    /// </para>
    /// </remarks>
    private static void OnLoginSucceeded(
        IHost host,
        LoginWindow loginWindow,
        IWritableCurrentUserAccessor accessor,
        ILogger<App> logger,
        AuthenticatedUserEventArgs args)
    {
        accessor.SetCurrentUser(
            args.User.Id,
            args.User.Username,
            args.User.DisplayName,
            args.Roles,
            args.Permissions,
            args.RolePermissions);
        LogSignInSucceeded(logger, args.User.Username, args.Roles, args.Permissions);

        var mainWindow = host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
        loginWindow.Close();
    }

    /// <summary>
    /// Source-generated emit for the successful-sign-in entry-point
    /// transition. Fires once per app session (until sign-out / re-login
    /// flows arrive). The matching failure emit is
    /// <see cref="LoginViewModel"/>'s <c>LogSignInSystemError</c>
    /// (EventId 1001). The <c>{@Roles}</c> and <c>{@Permissions}</c>
    /// destructuring prefixes carry the session-long ADR 0007 snapshot
    /// as structured collections so log-analysis tools see the names,
    /// not just type metadata; the file sink renders them as readable
    /// JSON-style arrays inline.
    /// </summary>
    [LoggerMessage(
        EventId = 6001,
        Level = LogLevel.Information,
        Message = "User {Username} signed in successfully. Roles: {@Roles}. Permissions: {@Permissions}.")]
    private static partial void LogSignInSucceeded(
        ILogger<App> logger,
        string username,
        IReadOnlyCollection<string> roles,
        IReadOnlyCollection<string> permissions);

    /// <summary>
    /// Detects whether the application is in the first-run bootstrap
    /// state. Resolves <see cref="IBootstrapService"/> from a fresh
    /// service scope, calls
    /// <see cref="IBootstrapService.IsBootstrapRequiredAsync"/>
    /// synchronously, and returns the result. Mirrors
    /// <see cref="ApplyPendingMigrations"/>'s shape (scoped resolve,
    /// sync bridge via <c>GetAwaiter().GetResult()</c>).
    /// </summary>
    /// <remarks>
    /// No try/catch wrapper: detection failure is not a
    /// graceful-degradation scenario. If the bootstrap service throws
    /// (the DbContext fails to open, the user repository's
    /// <c>AnyAsync</c> blows up), the exception propagates to the
    /// global dispatcher handler and the user sees the standard
    /// "Something went wrong" dialog. That's the correct shape — we
    /// cannot proceed to either window with a broken data layer, and
    /// silently defaulting to one of them would mask the real
    /// failure.
    /// </remarks>
    /// <param name="host">The host providing the scoped
    /// <see cref="IBootstrapService"/>.</param>
    /// <returns><see langword="true"/> when no users exist (bootstrap
    /// flow);  <see langword="false"/> when at least one user exists
    /// (login flow).</returns>
    private static bool IsBootstrapRequired(IHost host)
    {
        using var scope = host.Services.CreateScope();
        var bootstrap = scope.ServiceProvider.GetRequiredService<IBootstrapService>();
        return bootstrap.IsBootstrapRequiredAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Resolves <see cref="BootstrapWindow"/> from the host,
    /// subscribes the host-side
    /// <see cref="BootstrapViewModel.BootstrapSucceeded"/> and
    /// <see cref="BootstrapViewModel.IdempotencyGuardFired"/>
    /// handlers, and shows the window. Mirrors
    /// <see cref="ConfigureLoginFlow"/>'s shape — MainWindow is not
    /// resolved here; it is resolved lazily inside
    /// <see cref="OnBootstrapSucceeded"/> after the user creates the
    /// administrator account.
    /// </summary>
    /// <param name="host">The host providing the bootstrap surface
    /// and its dependencies.</param>
    private static void ConfigureBootstrapFlow(IHost host)
    {
        var bootstrapWindow = host.Services.GetRequiredService<BootstrapWindow>();
        var accessor = host.Services.GetRequiredService<IWritableCurrentUserAccessor>();
        var logger = host.Services.GetRequiredService<ILogger<App>>();

        bootstrapWindow.ViewModel.BootstrapSucceeded += (_, args) =>
            OnBootstrapSucceeded(host, bootstrapWindow, accessor, logger, args);

        bootstrapWindow.ViewModel.IdempotencyGuardFired += (_, _) =>
            OnIdempotencyGuardFired(logger);

        bootstrapWindow.Show();
    }

    /// <summary>
    /// Post-bootstrap transition. Populates the current-user accessor
    /// with the newly-created administrator, logs the success,
    /// resolves and shows <see cref="MainWindow"/>, then closes
    /// <paramref name="bootstrapWindow"/>. Mirrors
    /// <see cref="OnLoginSucceeded"/>'s ordering and rationale (see
    /// that method's remarks for the SetCurrentUser-before-Show and
    /// MainWindow.Show-before-Close requirements).
    /// </summary>
    /// <remarks>
    /// <b>Roles + permissions plumbing (ADR 0007).</b>
    /// <see cref="BootstrapSucceededEventArgs"/> arrives with the
    /// snapshots returned by
    /// <see cref="IBootstrapService.CreateAdministratorAsync"/> — the
    /// canonical <c>["Administrator"]</c> role and the eleven Phase 1
    /// system permissions. Passed verbatim to the accessor and to the
    /// log emit.
    /// </remarks>
    private static void OnBootstrapSucceeded(
        IHost host,
        BootstrapWindow bootstrapWindow,
        IWritableCurrentUserAccessor accessor,
        ILogger<App> logger,
        BootstrapSucceededEventArgs args)
    {
        accessor.SetCurrentUser(
            args.Administrator.Id,
            args.Administrator.Username,
            args.Administrator.DisplayName,
            args.Roles,
            args.Permissions,
            args.RolePermissions);
        LogBootstrapSucceeded(logger, args.Administrator.Username, args.Roles, args.Permissions);

        var mainWindow = host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
        bootstrapWindow.Close();
    }

    /// <summary>
    /// Handles the bootstrap idempotency-guard event — a user
    /// appeared between startup detection and the create call
    /// (vanishingly unlikely on a single-process desktop app, but
    /// the service-tier contract preserves the check). Logs at
    /// <see cref="LogLevel.Warning"/>, surfaces a "setup not
    /// required" dialog, and calls
    /// <see cref="Application.Shutdown(int)"/> with exit code 0.
    /// </summary>
    /// <remarks>
    /// Exit code 0 (not 1): the resolution is clean, not failed —
    /// the user just needs to relaunch to land at LoginWindow. No
    /// migration ran, no data state is corrupted, no support
    /// intervention required.
    /// </remarks>
    private static void OnIdempotencyGuardFired(ILogger<App> logger)
    {
        LogBootstrapIdempotencyGuardFired(logger);

        MessageBox.Show(
            "Setup is no longer required. Please restart the application to sign in.",
            "EasySynQ — Setup not required",
            MessageBoxButton.OK,
            MessageBoxImage.Information);

        Current.Shutdown(0);
    }

    /// <summary>
    /// Source-generated emit for the successful-bootstrap entry-point
    /// transition. Fires once per app session (the bootstrap window
    /// is single-use per install). The matching service-tier emit is
    /// <see cref="BootstrapService"/>'s <c>LogBootstrapCompleted</c>
    /// (EventId 7001). Carries the session-long ADR 0007 snapshot via
    /// destructured <c>{@Roles}</c> and <c>{@Permissions}</c> — same
    /// shape as <see cref="LogSignInSucceeded"/>.
    /// </summary>
    [LoggerMessage(
        EventId = 6004,
        Level = LogLevel.Information,
        Message = "Bootstrap completed; signing in administrator {Username}. Roles: {@Roles}. Permissions: {@Permissions}.")]
    private static partial void LogBootstrapSucceeded(
        ILogger<App> logger,
        string username,
        IReadOnlyCollection<string> roles,
        IReadOnlyCollection<string> permissions);

    /// <summary>
    /// Source-generated emit for the bootstrap idempotency-guard
    /// path. Logged at Warning rather than Error because the
    /// resolution is clean (exit 0; next launch routes correctly);
    /// the line exists so operators investigating "why did the
    /// process exit immediately after starting" find an explicit
    /// signal in the log.
    /// </summary>
    [LoggerMessage(
        EventId = 6005,
        Level = LogLevel.Warning,
        Message = "Bootstrap idempotency guard fired — a user appeared between startup detection and create call. Application will exit cleanly; next launch will route to LoginWindow.")]
    private static partial void LogBootstrapIdempotencyGuardFired(ILogger<App> logger);
}
