# GoImage

GoImage 是 Go 语言标准库 `image` 及其相关图像处理包在 .NET 平台上的高性能移植版本。它不仅保留了 Go 语言图像处理的简洁设计哲学（如基于接口的图像模型、统一的绘图逻辑），还充分利用了现代 C# 的高性能特性进行深度优化。

## 核心特性

- **高性能实现**：
  - 广泛使用 `Span<T>` 和 `ReadOnlySpan<T>` 减少内存拷贝。
  - 引入 `ArrayPool<byte>` 降低大尺寸图片处理时的 GC 压力。
  - 针对 GIF 编码引入了 **15-bit RGB 查找表 (LUT)**，将颜色匹配性能提升了约 50 倍。
  - 实现流式 IDAT 块写入和 LZW 压缩，避免大内存分配（MemoryStream.ToArray）。
- **完全流式支持**：支持从 `System.IO.Stream` 进行流式解码与编码，适合处理网络流或大文件。
- **Go 风格 API**：保留了 `IImage`、`Rectangle`、`Point` 以及 `Draw` 等经典概念，对于熟悉 Go 语言的开发者极易上手。
- **多格式支持**：内置 BMP、JPEG、PNG 和 GIF 的完整解码与编码支持。

## 支持格式

| 格式 | 解码 (Decode) | 编码 (Encode) | 特性 |
| :--- | :---: | :---: | :--- |
| **PNG** | ✅ | ✅ | 支持 RGBA/RGB/Gray，流式写入 |
| **JPEG** | ✅ | ✅ | 支持 JFIF、Adobe 扩展、YCbCr |
| **GIF** | ✅ | ✅ | 包含高性能 LZW 压缩，调色板加速 |
| **BMP** | ✅ | ✅ | 支持 1/4/8/24/32 位色深 |

## 性能表现

在处理超大图像（如 10650x13426，约 1.4 亿像素）时：
- **GIF 编码**：从原生移植的 62 秒大幅提升至 **2 秒** 以内。
- **PNG 编码**：利用内存布局直接映射（MemoryMarshal.Cast），编码速度接近系统原生性能。

## 快速上手

### 1. 注册与解码图片

```csharp
using GoImage.Image;
using GoImage.Jpeg;
using GoImage.Png;

// 注册所需格式（或使用预定义的全局注册）
PngReader.Register();
GifReader.Register();

using (var fs = File.OpenRead("input.jpg"))
{
    // 自动检测格式并解码
    var (img, format) = ImageRegistry.Decode(fs);
    Console.WriteLine($"格式: {format}, 尺寸: {img.Bounds().Dx()}x{img.Bounds().Dy()}");
}
```

### 2. 绘图与合成

```csharp
using GoImage.Draw;
using GoImage.Color;

var dst = RGBA.NewRGBA(Rect.New(0, 0, 100, 100));
var src = new Uniform(new Color.RGBA(255, 0, 0, 255)); // 红色

// 将红色填充到目标图像
Drawer.Draw(dst, dst.Bounds(), src, Point.Zero, Op.Src);
```

### 3. 编码保存

```csharp
using (var fs = File.Create("output.gif"))
{
    GoImage.Gif.GifWriter.Encode(fs, img);
}
```

## 技术架构

GoImage 遵循了 Go 语言 `image` 包的精髓：
- **颜色模型 (Color Models)**：通过 `IColor` 接口支持 RGBA、NRGBA、Gray、YCbCr 等多种颜色空间。
- **图像接口 (IImage)**：所有图像类型均实现统一接口，绘图引擎 `Drawer` 可在不同类型的图像间进行透明操作。
- **零拷贝思想**：在 PNG 和 BMP 的某些路径中，通过 `MemoryMarshal` 直接将图像数组转换为像素 Span，消除了逐像素转换的开销。

## 开源协议

本项目基于 BSD-style 协议（源自 Go 语言项目）。具体参见代码文件头部的声明。
