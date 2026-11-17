using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;

namespace BajkoweCuda.Api.Hosting;

/// <summary>
/// The built site in <c>wwwroot</c>, with the build-time origin placeholder replaced by this environment's
/// public origin in the text files that carry absolute URLs (HTML, <c>robots.txt</c>, <c>sitemap.xml</c>).
/// Everything else is passed through untouched. Rewritten files are kept in memory: there are a few dozen of them.
/// </summary>
internal sealed class PublicSiteFiles(IWebHostEnvironment environment, PublicSiteOptions options) : IFileProvider
{
    private static readonly string[] RewrittenExtensions = [".html", ".xml", ".txt"];

    private readonly IFileProvider _inner = environment.WebRootFileProvider;
    private readonly ConcurrentDictionary<(string Path, DateTimeOffset LastModified), RewrittenFile> _cache = new();

    public IFileInfo GetFileInfo(string subpath)
    {
        var file = _inner.GetFileInfo(subpath);

        if (!file.Exists || file.IsDirectory || !RewrittenExtensions.Contains(Path.GetExtension(subpath), StringComparer.OrdinalIgnoreCase))
        {
            return file;
        }

        return _cache.GetOrAdd((subpath, file.LastModified), static (_, state) => state.Self.Rewrite(state.File), (Self: this, File: file));
    }

    public IDirectoryContents GetDirectoryContents(string subpath) => _inner.GetDirectoryContents(subpath);

    public IChangeToken Watch(string filter) => _inner.Watch(filter);

    private RewrittenFile Rewrite(IFileInfo file)
    {
        using var stream = file.CreateReadStream();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var content = reader.ReadToEnd().Replace(PublicSiteOptions.OriginPlaceholder, options.Origin, StringComparison.Ordinal);

        return new RewrittenFile(file, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(content));
    }

    private sealed class RewrittenFile(IFileInfo original, byte[] content) : IFileInfo
    {
        public bool Exists => true;

        public bool IsDirectory => false;

        public long Length => content.Length;

        public string Name => original.Name;

        public DateTimeOffset LastModified => original.LastModified;

        /// <summary>No physical path: the static file middleware must stream <see cref="CreateReadStream"/>, not the file on disk.</summary>
        public string? PhysicalPath => null;

        public Stream CreateReadStream() => new MemoryStream(content, writable: false);
    }
}
