namespace DepsUpgradeAssistant;

public class Result
{
    public string PackageName { get; set; }
    public string Version { get; set; }
    public bool IsSupportStandard20 { get; set; }
    public string MinimalSupportStandard20 { get; set; }
    public string LatestVersion { get; set; }
}