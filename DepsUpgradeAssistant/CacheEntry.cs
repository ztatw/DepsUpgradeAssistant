namespace DepsUpgradeAssistant;

public class CacheEntry
{
    public string PkgId { get; set; }
    public List<VersionInfo> VersionInfos { get; set; }
}

public class VersionInfo
{
    public string Version { get; set; }
    public List<string> Tfs { get; set; }
}