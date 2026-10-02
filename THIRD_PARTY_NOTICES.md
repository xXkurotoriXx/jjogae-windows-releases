# Third-party notices

Windows 0.4.15 self-contained win-x64 distribution. Component licenses remain with their respective holders; this notice does not grant a new license to the application source or branding.

| Component | Version | License / notices |
| --- | --- | --- |
| .NET Runtime | 10.0.12 | [MIT license](https://github.com/xXkurotoriXx/jjogae-windows-releases/blob/main/licenses/NET-Runtime-LICENSE.txt), [upstream third-party notices](https://github.com/xXkurotoriXx/jjogae-windows-releases/blob/main/licenses/NET-Runtime-NOTICES.txt) |
| .NET Windows Desktop Runtime (WPF/Windows Forms) | 10.0.12 | [license](https://github.com/xXkurotoriXx/jjogae-windows-releases/blob/main/licenses/WindowsDesktop-LICENSE.txt); additional notices in the .NET Runtime notice file |
| Microsoft.Toolkit.Uwp.Notifications | 7.1.3 | Copyright .NET Foundation and Contributors; [complete MIT license](https://github.com/xXkurotoriXx/jjogae-windows-releases/blob/main/licenses/WindowsCommunityToolkit-LICENSE.md) |
| Microsoft.Web.WebView2 SDK / loader | 1.0.4191.47 | [Microsoft license](https://github.com/xXkurotoriXx/jjogae-windows-releases/blob/main/licenses/WebView2-LICENSE.txt), [notices](https://github.com/xXkurotoriXx/jjogae-windows-releases/blob/main/licenses/WebView2-NOTICE.txt) |
| Emoji.Wpf | 0.3.4 | Copyright 2017–2021 Sam Hocevar; [WTFPL version 2](https://github.com/xXkurotoriXx/jjogae-windows-releases/blob/main/licenses/Emoji-Wpf-LICENSE.txt) |
| Stfu | 0.1.1 | Copyright 2017–2021 Sam Hocevar; [WTFPL version 2](https://github.com/xXkurotoriXx/jjogae-windows-releases/blob/main/licenses/Stfu-LICENSE.txt) |
| Typography.OpenFont / Typography.GlyphLayout, included by Emoji.Wpf | upstream bundled source | [complete upstream MIT license and copyright](https://github.com/xXkurotoriXx/jjogae-windows-releases/blob/main/licenses/Typography-LICENSE.txt) |

The Emoji.Wpf and Stfu package copyright notices above are retained from their 0.3.4 / 0.1.1 NuGet metadata. Full upstream license texts are reproduced unchanged in the linked files. JeremyAnsel.HLSL.Targets 1.0.13 and Microsoft.NET.ILLink.Tasks are build dependencies, not application runtime libraries; no separate build tool is installed by this executable.

Microsoft Edge WebView2 Runtime is a separately installed Microsoft product and is governed by its own terms. The SDK license and notices above concern the components shipped in this app. Windows toast registration is local. Emoji rendering uses installed fonts; their licenses remain with the font providers. Third-party service names and logos identify their respective services and do not imply endorsement.

Package versions were checked against the locked NuGet dependencies and the runtime packs used for the official release. The complete license files are also available from this repository so users need not access the private source repository.
