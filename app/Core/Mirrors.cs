namespace SaberBridge.Core;

/// <summary>
/// Official download hosts only, in the order they are tried. A network that blocks one host often lets another
/// through; whatever host a file comes from, its Authenticode signature (Unity Technologies / Microsoft) is checked
/// before it runs, so a host can never hand over anything but the publisher's own file.
/// </summary>
public static class Mirrors
{
    /// <summary>Unity's own hosts for an editor build; the path is relative to the changeset folder.</summary>
    public static string[] Unity(string path) => new[]
    {
        $"https://download.unity3d.com/download_unity/{Compatibility.UnityChangeset}/{path}",
        $"https://netstorage.unity3d.com/unity/{Compatibility.UnityChangeset}/{path}",
        $"https://beta.unity3d.com/download/{Compatibility.UnityChangeset}/{path}",
        // Unity China's CDN serves the same signed builds
        $"https://download.unitychina.cn/download_unity/{Compatibility.UnityChangeset}/{path}",
    };

    /// <summary>The Visual C++ 2015-2022 x64 runtime: Microsoft's short link, then the file it points to.</summary>
    public static readonly string[] VcRedist =
    {
        "https://aka.ms/vs/17/release/vc_redist.x64.exe",
        "https://download.visualstudio.microsoft.com/download/pr/990c720a-46ce-4dc7-87b9-c2eb615d34b9/CC0FF0EB1DC3F5188AE6300FAEF32BF5BEEBA4BDD6E8E445A9184072096B713B/VC_redist.x64.exe",
    };
}
