using System.Windows.Media.Imaging;

namespace Jjogae.Windows;

internal sealed class BackgroundImageStore(string directory)
{
    internal const long MaximumBytes = 20 * 1024 * 1024;
    internal string ImagePath { get; } = Path.Combine(Path.GetFullPath(directory), "background.png");
    internal bool Exists => File.Exists(ImagePath);

    internal Task<BitmapImage?> Load() => Task.Run(() => Exists ? Decode(ImagePath) : null);

    internal Task<BitmapImage> Import(string selectedPath) => Task.Run(() =>
    {
        var image = Decode(selectedPath);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        Directory.CreateDirectory(Path.GetDirectoryName(ImagePath)!);
        var temporary = ImagePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                encoder.Save(output);
                if (output.Length > MaximumBytes) throw new InvalidDataException("이미지 크기를 줄인 뒤 다시 선택해 주세요.");
            }
            File.Move(temporary, ImagePath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return image;
    });

    internal void Clear() { if (Exists) File.Delete(ImagePath); }

    private static BitmapImage Decode(string path)
    {
        if (!new[] { ".png", ".jpg", ".jpeg", ".bmp" }.Contains(Path.GetExtension(path).ToLowerInvariant()))
            throw new InvalidDataException("PNG, JPG, BMP 이미지를 선택해 주세요.");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length == 0 || stream.Length > MaximumBytes) throw new InvalidDataException("20MB 이하의 이미지 파일을 선택해 주세요.");
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.OnDemand);
        if (decoder is not PngBitmapDecoder and not JpegBitmapDecoder and not BmpBitmapDecoder || decoder.Frames.Count != 1)
            throw new InvalidDataException("PNG, JPG, BMP 정지 이미지를 선택해 주세요.");
        var frame = decoder.Frames[0];
        var width = frame.PixelWidth; var height = frame.PixelHeight;
        if (width < 1 || height < 1 || width > 16384 || height > 16384 || (long)width * height > 40_000_000)
            throw new InvalidDataException("이미지가 너무 큽니다. 4천만 화소 이하의 이미지를 선택해 주세요.");
        stream.Position = 0;
        var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
        if (width >= height) image.DecodePixelWidth = Math.Min(width, 2560); else image.DecodePixelHeight = Math.Min(height, 2560);
        image.StreamSource = stream; image.EndInit(); image.Freeze();
        return image;
    }
}
