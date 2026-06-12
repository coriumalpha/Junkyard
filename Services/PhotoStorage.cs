using Inventario.Data;
using Inventario.Models;

namespace Inventario.Services;

public class PhotoStorage(IWebHostEnvironment env, IConfiguration config)
{
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

        await using var stream = File.Create(fullPath);
        await file.CopyToAsync(stream, cancellationToken);

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

        await using var stream = File.Create(fullPath);
        await file.CopyToAsync(stream, cancellationToken);

        return new PhotoInbox
        {
            Filename = $"inbox/{filename}",
            OriginalFilename = file.FileName,
            UpdatedAt = DateTime.UtcNow,
            SourceBoxId = sourceBoxId
        };
    }

    public static string PublicUrl(string filename) => $"/uploads/{filename.Replace("\\", "/")}";

    public static string RotationStyle(int rotationDegrees) => $"--rotation:{NormalizeRotation(rotationDegrees)}deg";

    public static int NormalizeRotation(int rotationDegrees)
    {
        var value = rotationDegrees % 360;
        return value < 0 ? value + 360 : value;
    }
}
