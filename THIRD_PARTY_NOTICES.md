# Third-party notices

ELARA ships with the following third-party components. They are used as published
NuGet packages / bundled binaries and were not modified.

## NAudio.Core (2.1.0)

- Used for: stream types used by the MP3 encoder writer
- License: MIT
- Copyright: Copyright © Mark Heath and NAudio contributors
- Source: https://github.com/naudio/NAudio
- Package: https://www.nuget.org/packages/NAudio.Core/
- Pulled in transitively by NAudio.Lame.

## NAudio.Lame (2.1.0)

- Used for: MP3 encoding (LAME wrapper)
- License: MIT (LICENSE.txt as shipped inside the NuGet package)
- Copyright: Copyright (c) 2013-2019 Corey Murtagh
- Source: https://github.com/Corey-M/NAudio.Lame
- Package: https://www.nuget.org/packages/NAudio.Lame/

## LAME — native encoder library

- Files: `libmp3lame.64.dll` (win-x64), bundled unmodified inside the NAudio.Lame
  NuGet package and copied to the application folder by that package.
- License: GNU Lesser General Public License (LGPL), as published by the LAME project.
  The exact license text and version are defined by the LAME project; see the links below.
- Project / source: https://lame.sourceforge.io/
- LGPL license text: https://www.gnu.org/licenses/lgpl.html
- The library is distributed unmodified and linked at runtime (dynamic linking);
  no LAME source code is part of this repository.

## ELARA

- License: MIT (see LICENSE, based on the original SimpleAudioRecorder by
  SickPuppyCoding — https://github.com/SickPuppyCoding/SimpleAudioRecorder)
