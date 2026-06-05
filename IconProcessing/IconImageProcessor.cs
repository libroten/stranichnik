using System;
using System.IO;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Stranichnik.Diagnostics;

namespace Stranichnik.Icons;

public sealed class IconImageProcessor : IIconImageProcessor
{
    private const string PngMimeType = "image/png";
    private static readonly Vector Dpi = new(96, 96);

    private readonly IconProcessingOptions _options;

    public IconImageProcessor(IconProcessingOptions? options = null)
    {
        _options = options ?? new IconProcessingOptions();
    }

    public ProcessedIconImage Process(ReadOnlyMemory<byte> originalBytes)
    {
        ValidateInputSize(originalBytes);

        using var input = new MemoryStream(originalBytes.ToArray(), writable: false);
        using var source = new Bitmap(input);

        if (source.PixelSize.Width <= 0 || source.PixelSize.Height <= 0)
        {
            Logs.Print("Icon image processing failed: decoded image has invalid dimensions.");
            throw new InvalidOperationException("Icon image has invalid dimensions.");
        }

        Logs.Print(
            $"Icon image decoded. SourceWidth={source.PixelSize.Width}, SourceHeight={source.PixelSize.Height}.");

        var outputSize = _options.OutputSize;
        using var target = new RenderTargetBitmap(new PixelSize(outputSize, outputSize), Dpi);

        using (var context = target.CreateDrawingContext(clear: true))
        {
            context.DrawImage(
                source,
                new Rect(source.Size),
                CalculateDestinationRect(source.PixelSize, outputSize));
        }

        using var output = new MemoryStream();
        target.Save(output);

        return new ProcessedIconImage(
            PngMimeType,
            outputSize,
            outputSize,
            output.ToArray());
    }

    private void ValidateInputSize(ReadOnlyMemory<byte> originalBytes)
    {
        if (originalBytes.Length == 0)
        {
            Logs.Print("Icon image processing rejected: empty input.");
            throw new ArgumentException("Icon image bytes cannot be empty.", nameof(originalBytes));
        }

        if (originalBytes.Length > _options.MaxInputBytes)
        {
            Logs.Print(
                $"Icon image processing rejected: input is too large. Bytes={originalBytes.Length}, MaxBytes={_options.MaxInputBytes}.");
            throw new InvalidOperationException("Icon image is too large.");
        }
    }

    private static Rect CalculateDestinationRect(PixelSize sourceSize, int outputSize)
    {
        var scale = Math.Min(
            outputSize / (double)sourceSize.Width,
            outputSize / (double)sourceSize.Height);

        var width = sourceSize.Width * scale;
        var height = sourceSize.Height * scale;

        return new Rect(
            (outputSize - width) / 2,
            (outputSize - height) / 2,
            width,
            height);
    }
}
