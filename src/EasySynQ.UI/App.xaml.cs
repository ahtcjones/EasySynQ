using System.Windows;

using EasySynQ.UI.Navigation;
using EasySynQ.UI.Pulse;

using Microsoft.Extensions.Logging.Abstractions;

namespace EasySynQ.UI;

/// <summary>
/// Application entry point. Owns the WPF startup sequence.
/// </summary>
/// <remarks>
/// Chunk E2.2-B / E2.3 wiring — minimum viable. Constructs the
/// dependency graph by hand: a <see cref="MockPulseSource"/>, the
/// drawer view model over it, and the shell view model that hosts
/// the drawer. Each view model receives <see cref="NullLogger{T}"/>.
/// Chunk E5 replaces this with a full DI host
/// (Microsoft.Extensions.Hosting), an EF Core migration check on
/// startup, a global unhandled-exception handler, and the
/// LoginWindow-then-MainWindow flow. The two
/// <see cref="NullLogger{T}"/> seams and the
/// <see cref="MockPulseSource"/> seam all live in this method on
/// purpose — they are the things E5 deletes in one place when DI
/// wiring lands.
/// </remarks>
public partial class App : Application
{
    /// <inheritdoc />
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var pulseSource = new MockPulseSource();
        var pulseDrawer = new PulseDrawerViewModel(
            pulseSource,
            NullLogger<PulseDrawerViewModel>.Instance);
        var shellVm = new MainShellViewModel(
            NullLogger<MainShellViewModel>.Instance,
            pulseDrawer);
        var mainWindow = new MainWindow(shellVm);
        mainWindow.Show();
    }
}
