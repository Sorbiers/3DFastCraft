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
| ManifoldRust (a Rust port of the Manifold geometry library, with its native `manifold_rs.dll`) | Apache-2.0 | https://github.com/larsbrubaker/manifold-rust |
| Microsoft Visual C++ Runtime (`vcruntime140.dll`, needed by `manifold_rs.dll`) | Microsoft Visual C++ Redistributable licence | https://learn.microsoft.com/cpp/windows/latest-supported-vc-redist |
| .NET runtime and `System.*` libraries | MIT | https://github.com/dotnet/runtime |

The test project additionally uses:

| Package | Licence | Project |
|---|---|---|
| xUnit.net | Apache-2.0 | https://github.com/xunit/xunit |
| Microsoft.NET.Test.Sdk | MIT | https://github.com/microsoft/vstest |

## Code that follows another project

The boolean fallback in `src/FastCraft3D.Geometry/Csg` - the BSP tree, plane and polygon classes -
is written to follow the algorithm and structure of **csg.js** by Evan Wallace
(https://github.com/evanw/csg.js), which is licensed as below. Nothing is restored from a package
for it; it is part of this source, so its notice goes with it.

> Copyright (c) 2011 Evan Wallace (http://madebyevan.com/)
>
> Permission is hereby granted, free of charge, to any person obtaining a copy of this software and
> associated documentation files (the "Software"), to deal in the Software without restriction,
> including without limitation the rights to use, copy, modify, merge, publish, distribute,
> sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is
> furnished to do so, subject to the following conditions:
>
> The above copyright notice and this permission notice shall be included in all copies or
> substantial portions of the Software.
>
> THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT
> NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
> NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM,
> DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT
> OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

## A note on SharpDX

SharpDX has been unmaintained since 2019 and its repository is archived. It is used here through
HelixToolkit, which still depends on it. It works on Windows 11 and is fully managed — it calls
the system Direct3D libraries by P/Invoke rather than shipping native binaries — but it is the
project's main long-term dependency risk. The modelling core deliberately contains no renderer
types, so the viewport can be replaced without touching the geometry, model or file-format code.
