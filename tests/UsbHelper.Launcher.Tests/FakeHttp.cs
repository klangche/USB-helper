using System.Net;

namespace UsbHelper.Launcher.Tests;

/// <summary>An HTTP client whose answers the test gives, per URL.</summary>
internal sealed class FakeHttp(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer) : HttpMessageHandler
{
    public List<Uri> Requests { get; } = [];

    public HttpClient Client() => new(this);

    public static FakeHttp Serving(IReadOnlyDictionary<string, byte[]> files) => new((request, _) =>
    {
        var found = files.TryGetValue(request.RequestUri!.ToString(), out var body);
        return Task.FromResult(found
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body!) }
            : new HttpResponseMessage(HttpStatusCode.NotFound));
    });

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!);
        return answer(request, cancellationToken);
    }
}
