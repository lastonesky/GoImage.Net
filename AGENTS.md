# GoImage

High-performance C# .NET port of Go's standard library `image` packages — BMP/JPEG/PNG/GIF codec + draw engine.

## Project

- Stack: .NET 8 class library (`GoImage.csproj`) + .NET 10 CLI tool (`Cli/GoImage.Cli.csproj`)
- Root namespace: `GoImage`
- Entry point: `Cli/Program.cs` (console image converter)
- No solution file; no test project

## Commands

```bash
dotnet build GoImage.csproj                    # Build the library
dotnet build Cli/GoImage.Cli.csproj            # Build the CLI
dotnet run --project Cli -- <input> <output>   # Convert an image (registers BMP/PNG/GIF/JPEG codecs)
dotnet pack GoImage.csproj -c Release          # Produce NuGet package (nupkg/ output)
./scripts/publish.sh 0.1.0                     # Pack + push to nuget.org (needs NUGET_API_KEY)
```

No `dotnet test` — no test project exists.

## Architecture

| Module | Role |
|---|---|
| `Image/` | Core interfaces (`IImage`, `IImage<T>`, `IDrawImage`, `IImage64`), geometry (`Point`, `Rectangle`), concrete types (`RGBA`, `Gray`, `Paletted`, `YCbCr`), format registry (`ImageRegistry`) |
| `Color/` | Color models (`IColor`, `IModel`) and types: `RGBA`, `NRGBA`, `Gray`, `RGBA64`, `YCbCr` + `ColorModels` conversion hub |
| `Draw/` | Compositing engine (`Drawer.Draw` / `DrawMask`) with fast paths for RGBA-to-RGBA copy/fill/over and paletted targets |
| `Bmp/` | BMP codec (`Reader.cs` / `Writer.cs`) — 1/4/8/24/32-bit |
| `Jpeg/` | JPEG codec — `Reader.cs` (JFIF/Adobe decode), `Writer.cs` (DCT + Huffman), `Dct.cs`, `Huffman.cs`, `Scan.cs` |
| `Png/` | PNG codec — stream-based IDAT writer with `ZLibStream` |
| `Gif/` | GIF codec + `WuQuantizer` for palette generation + high-perf LZW |
| `Internal/` | `ImageUtil.DrawYCbCr` — YCbCr→RGBA subsampled conversion |
| `Cli/` | Console app: register all codecs, decode→encode pipeline with timing |

All codecs register via `ImageRegistry.RegisterFormat(name, magic, decode, decodeConfig)` — the CLI calls each `*Reader.Register()` at startup.

## Conventions

- **File-scoped namespaces**: `namespace GoImage.X;`
- **Go-style naming**: `IImage`, `Pt.New()`, `Rect.New()`, `Drawer.Draw()` — mirrors Go API surface
- **Port attribution**: files derived from Go source carry a `// Port of Go's image/...` header comment
- **XML doc comments** on all public types and key methods
- **Performance-first**: `Span<T>`, `ReadOnlySpan<T>`, `ArrayPool<byte>`, `MemoryMarshal.Cast`, `AllowUnsafeBlocks`, `MethodImpl(AggressiveInlining)` — no gratuitous allocations
- **Static classes for codecs**: `PngReader`/`PngWriter` etc. are `static`; format state is instance-based
- **Nullable enabled** project-wide
- **Chinese comments** in `Cli/Program.cs` (user-facing CLI); English elsewhere

## Notes

<!-- Quick-adds for future sessions -->
