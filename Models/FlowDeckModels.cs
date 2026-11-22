namespace FlowDeck;

public record DiscoveredApp(string Name, string Path, bool IsShortcut);
public record FlowDeckCommand(string Name, string Description);
