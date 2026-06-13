using Inventario.Data;
using Inventario.Models;
using Microsoft.EntityFrameworkCore;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace Inventario.Services;

public class PhotoStorage(IWebHostEnvironment env, IConfiguration config)
{
    private const string DerivedFolder = "_derived";
    private const string ThumbVariant = "thumb";
    private const string PreviewVariant = "preview";
    private const int ThumbMaxSize = 360;
    private const int PreviewMaxSize = 1400;
    private static readonly JpegEncoder DerivativeEncoder = new() { Quality = 78 };
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".gif"
    };

    public string Root { get; } = DataPaths.UploadRoot(env, config);

    public async Task<Photo?> SaveAsync(IFormFile? file, PhotoEntityType type, int entityId, string? caption, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return null;
        }

        var extension = Path.GetExtension(file.FileName);
        if (!AllowedExtensions.Contains(extension))
        {
            throw new InvalidOperationException("Formato de imagen no permitido. Usa jpg, png, webp o gif.");
        }

        Directory.CreateDirectory(Root);
        var folder = Path.Combine(Root, type.ToString().ToLowerInvariant(), entityId.ToString());
        Directory.CreateDirectory(folder);
        var filename = $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        var fullPath = Path.Combine(folder, filename);

        await using (var stream = File.Create(fullPath))
        {
            await file.CopyToAsync(stream, cancellationToken);
        }

        await NormalizeImageAsync(fullPath, cancellationToken);
        await EnsureDerivativesAsync($"{type.ToString().ToLowerInvariant()}/{entityId}/{filename}", cancellationToken);

        return new Photo
        {
            EntityType = type,
            EntityId = entityId,
            Filename = $"{type.ToString().ToLowerInvariant()}/{entityId}/{filename}",
            Caption = caption
        };
    }

    public async Task<PhotoInbox?> SaveInboxAsync(IFormFile? file, int? sourceBoxId, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return null;
        }

        var extension = Path.GetExtension(file.FileName);
        if (!AllowedExtensions.Contains(extension))
        {
            throw new InvalidOperationException("Formato de imagen no permitido. Usa jpg, png, webp o gif.");
        }

        Directory.CreateDirectory(Root);
        var folder = Path.Combine(Root, "inbox");
        Directory.CreateDirectory(folder);
        var filename = $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        var fullPath = Path.Combine(folder, filename);

        await using (var stream = File.Create(fullPath))
        {
            await file.CopyToAsync(stream, cancellationToken);
        }

        await NormalizeImageAsync(fullPath, cancellationToken);
        await EnsureDerivativesAsync($"inbox/{filename}", cancellationToken);

        return new PhotoInbox
        {
            Filename = $"inbox/{filename}",
            OriginalFilename = file.FileName,
            UpdatedAt = DateTime.UtcNow,
            SourceBoxId = sourceBoxId
        };
    }

    public static string PublicUrl(string filename) => $"/uploads/{filename.Replace("\\", "/")}";
    public static string ThumbUrl(string filename) => DerivativeUrl(ThumbVariant, filename);
    public static string PreviewUrl(string filename) => DerivativeUrl(PreviewVariant, filename);

    public static string DerivativeUrl(string variant, string filename)
        => $"/photo-derivatives/{variant}/{filename.Replace("\\", "/")}";

    public static string RotationStyle(int rotationDegrees)
    {
        var normalized = NormalizeRotation(rotationDegrees);
        return $"--rotation:{normalized}deg";
    }

    public static int NormalizeRotation(int rotationDegrees)
    {
        var value = rotationDegrees % 360;
        return value < 0 ? value + 360 : value;
    }

    public async Task RotateStoredPhotoAsync(string filename, int deltaDegrees, CancellationToken cancellationToken)
    {
        var normalized = NormalizeRotation(deltaDegrees);
        if (normalized == 0)
        {
            return;
        }

        var fullPath = FullPath(filename);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("No se encontró el archivo de fotografía.", fullPath);
        }

        using var image = await Image.LoadAsync(fullPath, cancellationToken);
        image.Mutate(context =>
        {
            switch (normalized)
            {
                case 90:
                    context.Rotate(RotateMode.Rotate90);
                    break;
                case 180:
                    context.Rotate(RotateMode.Rotate180);
                    break;
                case 270:
                    context.Rotate(RotateMode.Rotate270);
                    break;
            }
        });
        image.Metadata.ExifProfile = null;
        await ReplaceImageAsync(image, fullPath, cancellationToken);
        DeleteDerivatives(filename);
        await EnsureDerivativesAsync(filename, cancellationToken);
    }

    public async Task EnsureDerivativesAsync(string filename, CancellationToken cancellationToken)
    {
        await EnsureDerivativeAsync(filename, ThumbVariant, ThumbMaxSize, cancellationToken);
        await EnsureDerivativeAsync(filename, PreviewVariant, PreviewMaxSize, cancellationToken);
    }

    public async Task<string> GetOrCreateDerivativeAsync(string variant, string filename, CancellationToken cancellationToken)
    {
        var maxSize = variant switch
        {
            ThumbVariant => ThumbMaxSize,
            PreviewVariant => PreviewMaxSize,
            _ => throw new InvalidOperationException("Variante de fotografía no válida.")
        };

        return await EnsureDerivativeAsync(filename, variant, maxSize, cancellationToken);
    }

    public static async Task ResetRotationMetadataAsync(InventoryDbContext db, string filename, CancellationToken cancellationToken)
    {
        var photos = await db.Photos
            .IgnoreQueryFilters()
            .Where(photo => photo.Filename == filename)
            .ToListAsync(cancellationToken);
        foreach (var photo in photos)
        {
            photo.RotationDegrees = 0;
        }

        var inboxPhotos = await db.PhotoInboxes
            .Where(photo => photo.Filename == filename)
            .ToListAsync(cancellationToken);
        foreach (var photo in inboxPhotos)
        {
            photo.RotationDegrees = 0;
        }
    }

    private async Task NormalizeImageAsync(string fullPath, CancellationToken cancellationToken)
    {
        try
        {
            using var image = await Image.LoadAsync(fullPath, cancellationToken);
            image.Mutate(context => context.AutoOrient());
            image.Metadata.ExifProfile = null;
            await ReplaceImageAsync(image, fullPath, cancellationToken);
        }
        catch (UnknownImageFormatException)
        {
            // The extension was accepted, but the file is not decodable. Leave validation to the browser/UI.
        }
    }

    private string FullPath(string filename)
    {
        var normalized = filename.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        if (normalized.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() == DerivedFolder)
        {
            throw new InvalidOperationException("Ruta de fotografía no válida.");
        }

        var fullPath = Path.GetFullPath(Path.Combine(Root, normalized));
        var root = Path.GetFullPath(Root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(root, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Ruta de fotografía no válida.");
        }

        return fullPath;
    }

    private async Task<string> EnsureDerivativeAsync(string filename, string variant, int maxSize, CancellationToken cancellationToken)
    {
        var sourcePath = FullPath(filename);
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("No se encontró el archivo de fotografía.", sourcePath);
        }

        var targetPath = DerivativeFullPath(variant, filename);
        if (File.Exists(targetPath) && File.GetLastWriteTimeUtc(targetPath) >= File.GetLastWriteTimeUtc(sourcePath))
        {
            return targetPath;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath) ?? Root);
        using var image = await Image.LoadAsync(sourcePath, cancellationToken);
        image.Mutate(context =>
        {
            context.AutoOrient();
            if (image.Width > maxSize || image.Height > maxSize)
            {
                context.Resize(new ResizeOptions
                {
                    Mode = ResizeMode.Max,
                    Size = new Size(maxSize, maxSize)
                });
            }
        });
        image.Metadata.ExifProfile = null;
        await image.SaveAsJpegAsync(targetPath, DerivativeEncoder, cancellationToken);
        return targetPath;
    }

    private string DerivativeFullPath(string variant, string filename)
    {
        var normalized = filename.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(Root, DerivedFolder, variant, normalized + ".jpg"));
        var derivedRoot = Path.GetFullPath(Path.Combine(Root, DerivedFolder, variant)).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(derivedRoot, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Ruta de miniatura no válida.");
        }

        return fullPath;
    }

    private void DeleteDerivatives(string filename)
    {
        foreach (var variant in new[] { ThumbVariant, PreviewVariant })
        {
            var path = DerivativeFullPath(variant, filename);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static async Task ReplaceImageAsync(Image image, string fullPath, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(fullPath) ?? "";
        var extension = Path.GetExtension(fullPath);
        var tempPath = Path.Combine(directory, $".{Path.GetFileNameWithoutExtension(fullPath)}.tmp-{Guid.NewGuid():N}{extension}");
        await image.SaveAsync(tempPath, cancellationToken);
        File.Move(tempPath, fullPath, overwrite: true);
    }
}
