using System.Collections.ObjectModel;

namespace FlowDeck.ViewModels;

internal sealed class MainWindowViewModel
{
    public List<string> Notes { get; } = [];
    public List<string> ClipboardHistory { get; } = [];
    public ObservableCollection<DiscoveredApp> DiscoveredApps { get; } = [];
    public Dictionary<string, int> AppLaunchCounts { get; } = new(StringComparer.OrdinalIgnoreCase);
    public ObservableCollection<DiscoveredApp> QuickApps { get; } = [];
    public ObservableCollection<object> Suggestions { get; } = [];
    public ObservableCollection<string> HistoryItems { get; } = [];
    public ObservableCollection<FlowDeckCommand> Commands { get; } = [];

    public void SetSuggestions(IEnumerable<object> suggestions)
    {
        Suggestions.Clear();
        foreach (var suggestion in suggestions)
            Suggestions.Add(suggestion);
    }

    public void SetHistory(IEnumerable<string> items)
    {
        HistoryItems.Clear();
        foreach (var item in items)
            HistoryItems.Add(item);
    }

    public void SetCommands(IEnumerable<FlowDeckCommand> commands)
    {
        Commands.Clear();
        foreach (var command in commands)
            Commands.Add(command);
    }
}
