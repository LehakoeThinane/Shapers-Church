using System.Text.RegularExpressions;
using Shapers.Media.Contracts;

namespace Shapers.Media.Application;

/// <summary>Stores images other modules bring in, under "imported/", each in its own folder so names never clash.</summary>
public sealed partial class PublicImageStore(IFileStorage storage) : IPublicImageStore
{
    /// <summary>Photos only. Not SVG: it can carry scripts.</summary>
    private static readonly HashSet<string> ImageTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/gif", "image/webp",
    };

    public async Task<string> SaveAsync(string fileName, Stream content, string contentType, CancellationToken cancellationToken)
    {
        var type = contentType.Split(';')[0].Trim();
        if (!ImageTypes.Contains(type))
        {
            throw new DomainRuleException("media.not_an_image", "Only images can be stored this way.");
        }

        var key = $"imported/{Guid.CreateVersion7():N}/{SafeName(fileName)}";
        await storage.SaveAsync(key, content, type, cancellationToken);
        return storage.PublicUrl(key);
    }

    /// <summary>Letters, digits, dots, dashes and underscores only, so the name is safe in a URL and a path.</summary>
    private static string SafeName(string fileName)
    {
        var name = Unsafe().Replace(Path.GetFileName(fileName), "-").Trim('-', '.');
        return name.Length is > 0 and <= 100 ? name : "image";
    }

    [GeneratedRegex(@"[^A-Za-z0-9._-]+")]
    private static partial Regex Unsafe();
}
