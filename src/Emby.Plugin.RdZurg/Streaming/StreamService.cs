using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace Emby.Plugin.RdZurg.Streaming;

/// <summary>The playback route every <c>.strm</c> file points at.</summary>
[Route("/RdZurg/Stream/{Key}/{FileName}", "GET,HEAD", IsHidden = true, Summary = "Serves one signed Real-Debrid file")]
[Unauthenticated] // Emby's ffmpeg has no session; the mandatory signature is the credential.
public class GetRdZurgStream : IReturnVoid
{
    /// <summary>Gets or sets the content key.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Gets or sets the file name, which only tells ffmpeg the container.</summary>
    public string FileName { get; set; } = string.Empty;
}

/// <summary>Adapts Emby's request pipeline to the host-free responder.</summary>
public class StreamService : IService, IRequiresRequest
{
    /// <inheritdoc />
    public IRequest Request { get; set; } = null!;

    /// <summary>Serves bytes.</summary>
    /// <param name="request">The route values.</param>
    /// <returns>A writer that answers once Emby asks for the body.</returns>
    public object Get(GetRdZurgStream request) => new Writer(request.Key, Request);

    /// <summary>Serves headers only.</summary>
    /// <param name="request">The route values.</param>
    /// <returns>A writer that answers once Emby asks for the body.</returns>
    public object Head(GetRdZurgStream request) => new Writer(request.Key, Request);

    /// <summary>
    /// Does all of its work in <see cref="WriteToAsync"/>, where the status can still be chosen: Emby sends
    /// nothing until the first write, so every refusal becomes its own status code and nothing is left open if
    /// Emby never asks for the body.
    /// </summary>
    private sealed class Writer : IAsyncStreamWriter, IHasHeaders
    {
        private readonly string _key;
        private readonly IRequest _request;

        public Writer(string key, IRequest request)
        {
            _key = key;
            _request = request;
        }

        public IDictionary<string, string> Headers { get; } = new Dictionary<string, string>();

        public Task WriteToAsync(IResponse response, CancellationToken cancellationToken)
        {
            var exchange = new Exchange(_request, response);
            var responder = Plugin.Instance?.Responder;
            if (responder is null)
            {
                exchange.SetStatus(503);
                exchange.SetContentLength(0);
                return Task.CompletedTask;
            }

            return responder.RespondAsync(_key, exchange, cancellationToken);
        }
    }

    private sealed class Exchange : IStreamExchange
    {
        private readonly IRequest _request;
        private readonly IResponse _response;

        public Exchange(IRequest request, IResponse response)
        {
            _request = request;
            _response = response;
        }

        public string Method => _request.Verb ?? _request.HttpMethod ?? "GET";

        public string? Range => _request.Headers.Get("Range");

        public bool HasIfRange => !string.IsNullOrEmpty(_request.Headers.Get("If-Range"));

        public string? Signature => _request.QueryString.Get("signature");

        public bool HeadersSent => _response.SentHeaders;

        public void SetStatus(int status) => _response.StatusCode = status;

        public void SetHeader(string name, string value)
        {
            if (string.Equals(name, "Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                _response.ContentType = value;
                return;
            }

            _response.Headers.Remove(name);
            _response.AddHeader(name, value);
        }

        public void RemoveHeader(string name) => _response.Headers.Remove(name);

        public void SetContentLength(long length) => _response.SetContentLength(length);

        public async Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
            => await _response.OutputWriter.WriteAsync(data, cancellationToken).ConfigureAwait(false);
    }
}
