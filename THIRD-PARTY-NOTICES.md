# Third-party notices

PrintBridge itself is MIT-licensed. It ships with the following third-party components,
which remain under their own licenses.

## PDFtoImage

- Package: `PDFtoImage`
- Copyright © David Sungaila
- License: **MIT**
- Source: <https://github.com/sungaila/PDFtoImage>

PDFtoImage renders PDF pages to bitmaps, which PrintBridge then hands to the Windows
print spooler. It brings in the two components below.

## PDFium

- Packages: `bblanchon.PDFium.Win32` (native `pdfium.dll`), pulled in by PDFtoImage
- Copyright © The PDFium Authors, Google Inc.
- License: **BSD 3-Clause**
- Source: <https://pdfium.googlesource.com/pdfium/>
- Packaging source: <https://github.com/bblanchon/pdfium-binaries>

## SkiaSharp

- Packages: `SkiaSharp`, `SkiaSharp.NativeAssets.Win32`, pulled in by PDFtoImage
- Copyright © Microsoft Corporation; Skia is copyright © Google Inc.
- License: **MIT** (SkiaSharp), **BSD 3-Clause** (Skia)
- Source: <https://github.com/mono/SkiaSharp>

## Serilog

- Packages: `Serilog`, `Serilog.Extensions.Logging`, `Serilog.Sinks.File`
- License: Apache-2.0
- Source: <https://github.com/serilog/serilog>

## ASP.NET Core / .NET runtime

- The self-contained publish includes the .NET, ASP.NET Core and Windows Desktop
  runtimes (Windows Forms and `System.Drawing.Printing`)
- License: MIT
- Source: <https://github.com/dotnet/aspnetcore>

No component of PrintBridge or its dependencies is licensed under the GPL or LGPL.
