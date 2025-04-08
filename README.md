# DistanceTelemetryPlugin

Forked from https://github.com/Distance-Modding/Mod.DistanceTelemetryPlugin

## Updates made
- Fixed up code to support later version of game (last tested against v)
- Created an Accesors.cs class that helps to access private fields (patches to have public getters)
- Make real-time (rather than write at end)

## Last confirmed working configuration 
- Distance game versionL: v.7067
- JsonFx version: v2.0.1209.2802
- .Net Framework version: v3.5
- BepInEx version: v5.4.23.2
 
## Development notes
- Use dnSpy to decompile/understand game Dlls
    - https://github.com/dnSpy/dnSpy
    - The game's main library is always `Distance\Distance_Data\Managed\Assembly-CSharp.dll`. 
    - Also can use for other dlls, such as UnityEngine, BepInEx, Harmony etc.
- Use BepInEx_win_x64_5.4.23.2
- Need .Net Framework 3.5 SP1 
    - https://www.microsoft.com/en-gb/download/details.aspx?id=22
    - Compile mod like that (not 4.7.2, as the version of Unity Distance uses needs .Net 3.5)
- Also need JsonFx.dll (v2.0.1209.2802)
    - https://www.nuget.org/packages/JsonFx/2.0.1209.2802
    - N.B. the .nupkg file can just be unzipped (like a zip file) and can get .dll from there
- Need Build Tools for Visual Studio 2022
    - https://visualstudio.microsoft.com/downloads/?cid=learn-onpage-download-cta
    - For building C# code into .dll
    - Comes with Visual Studio, but to build from VS Code command line just need the build tools installed + path added
- Check the .csproj params
    - References to paths to .dlls - check correct on your machine

## Building code into .dll
    - To build, from folder with .csproj run `msbuild DistanceTelemetryPlugin.csproj /t:Clean,Build /p:Configuration=Release`

## Installing mod
    - Create folder in `<Path to Steam>\Steam\steamapps\common\Distance\BepInEx\plugins\` called `DistanceTelemetryPlugin`
    - Paste in the `DistanceTelemetryPlugin.dll` and `JsonFx.dll` from `Mod.DistanceTelemetryPlugin\DistanceTelemetryPlugin\bin\Release`