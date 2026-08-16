using System.Net;
using System.Text;

namespace ACModHub.Tests.TestSupport;

/// <summary>
/// Deterministic HttpMessageHandler for catalog/update client tests: a queue of responses
/// and a record of requests (URLs, headers) received.
/// </summary>
public sealed class FakeCatalogHandler : HttpMessageHandler
{
    private readonly Queue<(HttpStatusCode Status, string? Body, Dictionary<string, string>? Headers)> _responses;
    private readonly bool _repeatLast;

    public FakeCatalogHandler(bool repeatLast = false)
    {
        _repeatLast = repeatLast;
        _responses = new Queue<(HttpStatusCode, string?, Dictionary<string, string>?)>();
    }

    public List<Uri> RequestedUris { get; } = [];
    public List<string> RequestedIfNoneMatch { get; } = [];

    public FakeCatalogHandler Enqueue(HttpStatusCode status, string? body = null, Dictionary<string, string>? headers = null)
    {
        _responses.Enqueue((status, body, headers));
        return this;
    }

    public FakeCatalogHandler EnqueueJson(string json, string? etag = null)
    {
        var headers = new Dictionary<string, string> { ["Content-Type"] = "application/json" };
        if (etag is not null) headers["ETag"] = etag;
        return Enqueue(HttpStatusCode.OK, json, headers);
    }

    public FakeCatalogHandler EnqueueRedirect(string location)
    {
        return Enqueue(HttpStatusCode.Redirect, null, new Dictionary<string, string> { ["Location"] = location });
    }

    public FakeCatalogHandler EnqueueStreaming(string content, int chunkSize)
    {
        // Response with chunked encoding (no content-length) to exercise streaming guards.
        var handler = new ChunkedHandler(content, chunkSize);
        _responses.Enqueue((HttpStatusCode.OK, null, null));
        StreamingHandlers.Enqueue(handler);
        return this;
    }

    private Queue<ChunkedHandler> StreamingHandlers { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestedUris.Add(request.RequestUri!);
        RequestedIfNoneMatch.Add(request.Headers.IfNoneMatch.Count > 0 ? request.Headers.IfNoneMatch.First().Tag : string.Empty);

        (HttpStatusCode Status, string? Body, Dictionary<string, string>? Headers) response;
        if (_responses.Count == 0)
        {
            // Out of queued responses behaves like a 404 endpoint (a common production case).
            response = (HttpStatusCode.NotFound, null, null);
        }
        else if (_responses.Count == 1 && _repeatLast)
        {
            response = _responses.Peek();
        }
        else
        {
            response = _responses.Dequeue();
        }

        var message = new HttpResponseMessage(response.Status);
        if (response.Headers is not null)
        {
            foreach (var pair in response.Headers) message.Headers.TryAddWithoutValidation(pair.Key, pair.Value);
        }
        if (response.Body is not null)
        {
            message.Content = new StringContent(response.Body, Encoding.UTF8, "application/json");
        }
        else if (StreamingHandlers.Count > 0 && response.Status == HttpStatusCode.OK)
        {
            message.Content = new StreamContent(StreamingHandlers.Dequeue());
        }
        message.RequestMessage = request;
        return Task.FromResult(message);
    }

    private sealed class ChunkedHandler : Stream
    {
        private readonly byte[] _data;
        private readonly int _chunkSize;
        private int _offset;

        public ChunkedHandler(string content, int chunkSize)
        {
            _data = Encoding.UTF8.GetBytes(content);
            _chunkSize = chunkSize;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_offset >= _data.Length) return 0;
            var toRead = Math.Min(_chunkSize, Math.Min(count, _data.Length - _offset));
            Array.Copy(_data, _offset, buffer, offset, toRead);
            _offset += toRead;
            return toRead;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _data.Length;
        public override long Position { get => _offset; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            Task.FromResult(Read(buffer, offset, count));
    }
}
