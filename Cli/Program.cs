using System.Diagnostics;
using GoImage.Image;
using GoImage.Bmp;
using GoImage.Jpeg;
using GoImage.Png;
using GoImage.Gif;

namespace GoImage.Cli;

class Program
{
    static void Main(string[] args)
    {
        // 注册格式
        BmpReader.Register();
        PngReader.Register();
        GifReader.Register();
        // Jpeg 在静态构造函数中注册，确保它被加载
        System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(GoImage.Jpeg.Decoder).TypeHandle);

        if (args.Length < 2)
        {
            Console.WriteLine("用法: GoImage.Cli <输入文件> <输出文件>");
            Console.WriteLine("支持的格式: BMP, JPEG (解码支持取决于注册的格式)");
            return;
        }

        string inputPath = args[0];
        string outputPath = args[1];

        if (!File.Exists(inputPath))
        {
            Console.WriteLine($"错误: 找不到输入文件 '{inputPath}'");
            return;
        }

        try
        {
            // 1. 解码
            Console.WriteLine($"正在从 '{inputPath}' 解码...");
            Stopwatch sw = Stopwatch.StartNew();
            
            IImage? img;
            string format;
            
            using (var fsIn = File.OpenRead(inputPath))
            {
                (img, format) = ImageRegistry.Decode(fsIn);
            }
            
            sw.Stop();
            if (img == null)
            {
                Console.WriteLine("错误: 解码失败，返回图像为空。");
                return;
            }
            Console.WriteLine($"成功解码 {format} 格式 (用时: {sw.ElapsedMilliseconds}ms, 分辨率: {img.Bounds().Dx()}x{img.Bounds().Dy()})");

            // 2. 编码
            string ext = Path.GetExtension(outputPath).ToLower();
            Console.WriteLine($"正在编码到 '{outputPath}' ({ext})...");
            
            sw.Restart();
            using (var fsOut = File.Create(outputPath))
            {
                switch (ext)
                {
                    case ".bmp":
                        BmpWriter.Encode(fsOut, img);
                        break;
                    case ".jpg":
                    case ".jpeg":
                        GoImage.Jpeg.Encoder.Encode(fsOut, img, new GoImage.Jpeg.Options { Quality = 90 });
                        break;
                    case ".png":
                        PngWriter.Encode(fsOut, img);
                        break;
                    case ".gif":
                        GifWriter.Encode(fsOut, img);
                        break;
                    default:
                        throw new NotSupportedException($"不支持的输出格式: {ext}");
                }
            }
            sw.Stop();
            Console.WriteLine($"编码完成 (用时: {sw.ElapsedMilliseconds}ms)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"发生错误: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"内部错误: {ex.InnerException.Message}");
            }
        }
    }
}
