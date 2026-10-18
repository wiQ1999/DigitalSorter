using System.Security.Cryptography;
using Microsoft.Extensions.FileProviders;

namespace DigitalSorter.Auth;

public static class WebAssets
{
    public static string Revision(IFileProvider files)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var path in new[] { "css/app.css", "js/media.js", "DigitalSorter.Web.styles.css" })
        {
            var file = files.GetFileInfo(path);
            if (!file.Exists) continue;
            using var stream = file.CreateReadStream();
            hash.AppendData(SHA256.HashData(stream));
        }
        return Convert.ToHexString(hash.GetHashAndReset())[..16];
    }
}
