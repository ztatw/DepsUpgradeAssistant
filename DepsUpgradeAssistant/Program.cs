// See https://aka.ms/new-console-template for more information


using System.Collections.Specialized;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Xml;
using DepsUpgradeAssistant;

var projectPath = args[0];
Console.WriteLine($"project root path: {projectPath}");

var cache = new Dictionary<string, OrderedDictionary>();
const string DUA_ROOT = "d:\\dua";
const string DUA_CACHE = $"{DUA_ROOT}\\cache";
if (!Directory.Exists(DUA_ROOT))
{
    Directory.CreateDirectory(DUA_ROOT);
}

if (!Directory.Exists(DUA_CACHE))
{
    Directory.CreateDirectory(DUA_CACHE);
}

var cacheFiles = Directory.GetFiles(DUA_CACHE, "*.json");
if (cacheFiles.Any())
{
    foreach (var cacheFile in cacheFiles)
    {
        var entry = JsonSerializer.Deserialize<CacheEntry>(File.ReadAllText(cacheFile));
        if (entry != null)
        {
            var versions = new OrderedDictionary();
            entry.VersionInfos.ForEach(vi => versions.Add(vi.Version, vi.Tfs));
            cache.Add(entry.PkgId, versions);
        }
    }

    Console.WriteLine($"restored {cacheFiles.Length} pkg infos");
}

var httpClient = new HttpClient();
httpClient.BaseAddress = new Uri("https://api.nuget.org/v3-flatcontainer/");
var duaResult = new List<Result>();
foreach (var path in Directory.GetFiles(projectPath, "packages.config", SearchOption.AllDirectories))
{
    duaResult.AddRange(await Analyze(path));
}

File.WriteAllText($"{DUA_ROOT}\\dua.json", JsonSerializer.Serialize(duaResult));

// Console.WriteLine($"total package references: {results.Count}");
// 1620

// Console.WriteLine($"unique id count: {results.Select(r => r.PackageName).Distinct().Count()}");
// 190

async Task<List<Result>> Analyze(string packagesConfigPath)
{
    var projectResult = new List<Result>();
    var doc = new XmlDocument();
    doc.Load(packagesConfigPath);
    var packageNodes = doc.SelectNodes("//packages/package");
    if (packageNodes == null) return projectResult;
    foreach (XmlElement node in packageNodes)
    {
        if (!node.HasAttribute("id")) continue;
        Console.WriteLine($"processing: {node.OuterXml}");
        var pkgId = node.GetAttribute("id");
        var pkgVersion = node.GetAttribute("version");
        if (!cache.ContainsKey(pkgId))
        {
            cache.Add(pkgId, await QueryPackageById(pkgId));
        }

        var pkgVersionInfo = cache[pkgId];
        var currentTfs = (List<string>) (pkgVersionInfo[pkgVersion]?? new List<string>());
        projectResult.Add(new Result
        {
            PackageName = pkgId,
            Version = pkgVersion,
            IsSupportStandard20 = currentTfs.Contains(".NETStandard2.0"),
            LatestVersion = pkgVersionInfo.Keys.Cast<string>().LastOrDefault()?? "NA",
            MinimalSupportStandard20 = pkgVersionInfo.Keys.Cast<string>().FirstOrDefault(v =>
            {
                var tfs = pkgVersionInfo[v];
                if (tfs != null)
                {
                    return ((List<string>) tfs).Contains(".NETStandard2.0");
                }

                return false;
            })?? "NA"
        });
    }
    return projectResult;
}

async Task<OrderedDictionary> QueryPackageById(string packageId)
{
    var versionInfos = new OrderedDictionary();
    var resp = await httpClient.GetAsync($"{packageId}/index.json");
    if (resp.StatusCode == HttpStatusCode.NotFound) return versionInfos;
    var versionsResponse = await resp.Content.ReadFromJsonAsync<VersionsResponse>();
    if (versionsResponse == null)
    {
        throw new ApplicationException($"can not find package versions: {packageId}");
    }

    
    foreach (var version in versionsResponse.Versions)
    {
        if (version.Contains('-')) continue;
        Console.WriteLine($"get nuspec: {packageId}/{version}");
        var nuspecResp = await httpClient.GetAsync($"{packageId}/{version}/{packageId}.nuspec");
        nuspecResp.EnsureSuccessStatusCode();
        var tfs = await ExtractTargetFrameworksAsync(await nuspecResp.Content.ReadAsStringAsync());
        Console.WriteLine($"tfs for {packageId}/{version}: {string.Join(",", tfs)}");
        versionInfos.Add(version, tfs);
    }

    await SaveRestoreFile(packageId, versionInfos);
    
    return versionInfos;
}

Task<List<string>> ExtractTargetFrameworksAsync(string nuspec)
{
    var tfs = new List<string>();
    var doc = new XmlDocument();
    doc.LoadXml(nuspec);
    var dependencyGroups = doc.SelectNodes("//*[local-name()='dependencies']/*[local-name()='group']");
    if (dependencyGroups == null) return Task.FromResult(tfs);
    foreach (XmlElement dependencyGroup in dependencyGroups)
    {
        tfs.Add(dependencyGroup.GetAttribute("targetFramework"));
    }

    return Task.FromResult(tfs);
}

async Task SaveRestoreFile(string packageId, OrderedDictionary result)
{
    var entry = new CacheEntry
    {
        PkgId = packageId,
        VersionInfos = result.Keys.Cast<string>().Select(k => new VersionInfo
        {
            Version = k,
            Tfs = (List<string>) result[k]
        }).ToList()
    };
    await File.WriteAllTextAsync($"{DUA_CACHE}\\{entry.PkgId}.json", JsonSerializer.Serialize(entry));
}