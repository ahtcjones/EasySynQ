using System.Windows;

using EasySynQ.Services.Abstractions;
using EasySynQ.Services.Identity;
using EasySynQ.UI.Identity;
using EasySynQ.UI.Navigation;
using EasySynQ.UI.Pulse;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EasySynQ.UI;

/// <summary>
/// Application entry point. Owns the WPF startup sequence and the
/// Microsoft.Extensions.Hosting service-provider lifecycle.
/// </summary>
/// <remarks>
/// <para>
/// <b>Chunk E5.1.</b> The shell's dependency graph is constructed
/// through a <see cref="HostApplicationBuilder"/> rather than by hand in
/// <see cref="OnStartup"/>. The three seams the prior code concentrated
/// in this method (a <see cref="MockPulseSource"/> and two
/// <c>NullLogger&lt;T&gt;</c> placeholders) are now DI registrations:
/// the mock pulse source as <see cref="IPulseSource"/>, and the loggers
/// via the host's own
/// <see cref="HostApplicationBuilder.Logging"/> pipeline with all
/// providers cleared — so resolved <c>ILogger&lt;T&gt;</c> instances are
/// behaviorally no-op, matching the <c>NullLogger&lt;T&gt;.Instance</c>
/// behavior the prior hand-built graph relied on.
/// </para>
/// <para>
/// <b>Still to land in Chunk E5.</b> E5.2 wires the Serilog provider
/// inside <see cref="OnStartup"/>. E5.3 installs a global
/// unhandled-exception handler. E5.4 swaps <c>LoginWindow</c> in as the
/// entry point and drives the login → shell flow.
/// </para>
/// </remarks>
public partial class App : Application
{
    private IHost? _host;

    /// <inheritdoc />
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var builder = Host.CreateApplicationBuilder(e.Args);

        // Drop the framework's default logging providers (Console,
        // Debug, EventSource, EventLog) so resolved ILogger<T> instances
        // produce no output — preserves the NullLogger<T>.Instance
        // semantics the prior hand-built graph relied on. E5.2
        // reintroduces a single provider (Serilog) inside this same
        // builder, with no other change to the dependency graph.
        builder.Logging.ClearProviders();

        ConfigureServices(builder.Services);
        _host = builder.Build();
        _host.Start();

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    /// <inheritdoc />
    protected override void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            _host.StopAsync().GetAwaiter().GetResult();
            _host.Dispose();
            _host = null;
        }
        base.OnExit(e);
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
