# Third-party notices

3DFastCraft depends on the packages below. Each is covered by its own licence, which governs
that component regardless of the terms in [LICENSE](LICENSE).

None of these are redistributed in source form here; they are restored from NuGet at build time
and bundled into the standalone executable produced by `build.bat`.

| Package | Licence | Project |
|---|---|---|
| HelixToolkit | MIT | https://github.com/helix-toolkit/helix-toolkit |
| HelixToolkit.SharpDX.Core | MIT | https://github.com/helix-toolkit/helix-toolkit |
| HelixToolkit.SharpDX.Core.Wpf | MIT | https://github.com/helix-toolkit/helix-toolkit |
| SharpDX and its SharpDX.* packages | MIT | https://github.com/sharpdx/SharpDX |
| Cyotek.Drawing.BitmapFont | MIT | https://github.com/cyotek/Cyotek.Drawing.BitmapFont |
| Microsoft.Extensions.Logging.Abstractions | MIT | https://github.com/dotnet/runtime |
| .NET runtime and `System.*` libraries | MIT | https://github.com/dotnet/runtime |

The test project additionally uses:

| Package | Licence | Project |
|---|---|---|
| xUnit.net | Apache-2.0 | https://github.com/xunit/xunit |
| Microsoft.NET.Test.Sdk | MIT | https://github.com/microsoft/vstest |

## A note on SharpDX

SharpDX has been unmaintained since 2019 and its repository is archived. It is used here through
HelixToolkit, which still depends on it. It works on Windows 11 and is fully managed — it calls
the system Direct3D libraries by P/Invoke rather than shipping native binaries — but it is the
project's main long-term dependency risk. The modelling core deliberately contains no renderer
types, so the viewport can be replaced without touching the geometry, model or file-format code.
