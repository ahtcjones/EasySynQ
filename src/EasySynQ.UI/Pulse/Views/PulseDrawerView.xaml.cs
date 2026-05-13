using System.Windows.Controls;
using System.Windows.Data;

namespace EasySynQ.UI.Pulse.Views;

/// <summary>
/// Code-behind for <see cref="PulseDrawerView"/>. Minimal — its only
/// job is to set the
/// <see cref="System.Windows.Data.CollectionViewSource"/>'s Source
/// once the DataContext resolves, because bindings inside a
/// <c>ResourceDictionary</c> don't carry an inherited DataContext.
/// Mirrors the pattern <see cref="MainWindow"/> uses for the nav
/// tree's CollectionViewSource.
/// </summary>
public partial class PulseDrawerView : UserControl
{
    /// <summary>Parameterless constructor for XAML instantiation.</summary>
    public PulseDrawerView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is PulseDrawerViewModel vm)
        {
            ((CollectionViewSource)Resources["GroupedTiles"]).Source = vm.Tiles;
        }
    }
}
