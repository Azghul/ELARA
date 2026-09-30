# Third-party notices

ELARA ships with the following third-party components. They are used as published
NuGet packages / bundled binaries and were not modified.

## NAudio.Core (2.1.0)

- Used for: stream types used by the MP3 encoder writer
- License: MIT — full text shipped in this distribution as
  [LICENSE-NAudio.Core.txt](LICENSE-NAudio.Core.txt)
- Copyright (as documented in the package): © Mark Heath 2022
- Source: https://github.com/naudio/NAudio
- Package: https://www.nuget.org/packages/NAudio.Core/
- Pulled in transitively by NAudio.Lame.

## NAudio.Lame (2.1.0)

- Used for: MP3 encoding (LAME wrapper)
- License: MIT — full text shipped in this distribution as
  [LICENSE-NAudio.Lame.txt](LICENSE-NAudio.Lame.txt) (identical to the
  LICENSE.txt shipped inside the NuGet package)
- Copyright (as documented in the package): Copyright (c) 2013-2019 Corey Murtagh
- Source: https://github.com/Corey-M/NAudio.Lame
- Package: https://www.nuget.org/packages/NAudio.Lame/

## LAME — native encoder library

- Files: `libmp3lame.64.dll` (win-x64), bundled unmodified inside the NAudio.Lame
  NuGet package and copied to the application folder by that package.
- LAME version: **3.100** (identifiable from the binary itself; build date 2017-10-22)
- License: **GNU Library General Public License, Version 2, June 1991** — the exact
  license text of the LAME 3.100 release (`COPYING`), shipped unmodified in this
  distribution as [LICENSE-LAME.txt](LICENSE-LAME.txt).
- This distribution ships the library **unmodified and as a separate, dynamically
  loaded DLL** (`libmp3lame.64.dll`), which is the usage model explicitly
  recommended by the LAME project's LICENSE file.
- Project / source for exactly this version (LAME 3.100):
  https://sourceforge.net/projects/lame/files/lame/3.100/

## .NET 10 Runtime (self-contained deployment — Windows)

The self-contained win-x64 deployment includes the .NET runtime from
**Microsoft.NETCore.App.Runtime.win-x64 (10.0.12)** and
**Microsoft.WindowsDesktop.App.Runtime.win-x64 (10.0.12)**. Per Microsoft's
official Windows license information
([license-information-windows.md](https://github.com/dotnet/core/blob/main/license-information-windows.md)),
this Windows distribution contains components under **multiple Microsoft license terms**:

- **MIT** — most runtime components and files.
  Full text shipped in this distribution as
  [LICENSE-NETRuntime.txt](LICENSE-NETRuntime.txt)
  (Copyright (c) .NET Foundation and Contributors). The complete third-party
  notices of the .NET runtime are shipped as
  [LICENSE-NETRuntime-ThirdPartyNotices.txt](LICENSE-NETRuntime-ThirdPartyNotices.txt).
- **Microsoft .NET Library License** — the following binaries included in this
  distribution: `coreclr.dll` and `Microsoft.DiaSymReader.Native.amd64.dll`.
  Full official license text shipped in this distribution as
  [LICENSE-DOTNET-LIBRARY.txt](LICENSE-DOTNET-LIBRARY.txt)
  (verbatim from https://dotnet.microsoft.com/dotnet_library_license.htm).

WPF-related binaries covered by the .NET Library License or the Windows SDK
License (`PresentationNative_cor3.dll`, `vcruntime140_cor3.dll`, `wpfgfx_cor3.dll`,
`D3DCompiler_47_cor3.dll`) are **not** part of this distribution.

- Runtime source: https://github.com/dotnet/runtime

## ELARA

- License: MIT (see LICENSE, based on the original SimpleAudioRecorder by
  SickPuppyCoding — https://github.com/SickPuppyCoding/SimpleAudioRecorder)
