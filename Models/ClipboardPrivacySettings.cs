namespace FlowDeck;

internal sealed class ClipboardPrivacySettings
{
    public ClipboardPrivacySettings()
    {
    }

    public bool CaptureEnabled { get; set; } = true;
    public string[] ExcludedApplications { get; set; } = [];
}
