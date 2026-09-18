# Vendored krabsetw

This folder contains a prebuilt **Microsoft.O365.Security.Native.ETW** assembly that includes a
fix not yet available in the published NuGet package.

## Why

The published 4.4.6 package mis-decodes TraceLogging payloads. TraceLogging events are
identified by name rather than event id (the id is always `0`), and a provider may emit the same
event name from several call sites with different fields. krabsetw's schema cache could not tell
those variants apart, so the first schema decoded was reused for all of them and every field
came out shifted.

In ETWSpy this showed up as `Microsoft.Windows.AppLifeCycle.UI` reporting
`entryPoint=50331648` — actually the `PartA_PrivTags` value — with `appId` empty.

Upstream issue: <https://github.com/microsoft/krabsetw/issues/193>

## Provenance

| | |
| --- | --- |
| Source | <https://github.com/chrdavis/krabsetw> |
| Branch | `fix/193-tracelogging-schema-key` |
| Commit | `f4a75e40cfbb6a9fb37a7847908b6316c252b767` |
| Based on upstream | `634b411` (`master`) |
| Configuration | `Release` / `x64` / `net8.0` |

## Rebuilding

Build the .NET Core wrapper, then copy three files here. Use the **Enterprise/Community**
MSBuild — the Build Tools copy lacks the .NET SDK resolver and fails with `MSB4236`.

```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Enterprise\MSBuild\Current\Bin\MSBuild.exe" `
	<krabsetw>\Microsoft.O365.Security.Native.ETW.NetCore\Microsoft.O365.Security.Native.ETW.NetCore.vcxproj `
	/p:Configuration=Release /p:Platform=x64

$src = "<krabsetw>\Microsoft.O365.Security.Native.ETW.NetCore\x64\Release\net8.0"
Copy-Item "$src\Microsoft.O365.Security.Native.ETW.dll" .
Copy-Item "$src\Microsoft.O365.Security.Native.ETW.xml" .
Copy-Item "$src\Ijwhost.dll" .
```

`Ijwhost.dll` is required to host a mixed-mode (C++/CLI) assembly on .NET Core.

Remember to update the commit hash above.

## Removing this once the fix ships upstream

1. Set `KrabsEtwSource` to `Package` in `Directory.Build.props`, and bump the
   `Microsoft.O365.Security.Native.ETW` version in the project files.
2. Delete this folder and the `Vendored` branch in `eng\KrabsEtw.targets`.
3. Capture `Microsoft.Windows.AppLifeCycle.UI` and confirm no `(schema warning)` rows appear.
