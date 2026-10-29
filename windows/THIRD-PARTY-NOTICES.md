# Third-party components

MagerCam uses unmodified, dynamically linked components:

- **LibVLCSharp / LibVLCSharp.WinForms 3.10.2** — VideoLAN, LGPL-2.1-or-later. Source: https://github.com/videolan/libvlcsharp (tag 3.10.2); packages: https://www.nuget.org/packages/LibVLCSharp.WinForms/3.10.2
- **LibVLC Windows 3.0.24** — VideoLAN and contributors. Source and component licenses: https://www.videolan.org/vlc/download-sources.html ; https://download.videolan.org/pub/videolan/vlc/3.0.24/ ; package: https://www.nuget.org/packages/VideoLAN.LibVLC.Windows/3.0.24 . Preserve the accompanying license files and notices, including licenses for plugins and dependencies.
- **.NET 10 Windows Desktop runtime** — Microsoft and contributors, MIT and third-party notices distributed with the runtime. Source: https://github.com/dotnet/runtime ; https://github.com/dotnet/winforms

The application does not modify these libraries. They remain separate replaceable files in the portable application directory. License texts are distributed in `licenses/`. FFmpeg used to generate the test fixture is a development tool and is not included in the application.
