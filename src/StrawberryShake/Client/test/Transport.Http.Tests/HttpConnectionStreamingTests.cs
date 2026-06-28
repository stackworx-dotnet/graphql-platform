using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text;
using System.Text.Json;

namespace StrawberryShake.Transport.Http;

public class HttpConnectionStreamingTests
{
    private static readonly HttpRequestOptionsKey<bool> s_streamingOption =
        new("WebAssemblyEnableStreamingResponse");

    [Fact]
    public async Task CreateHttpRequest_Should_EnableResponseStreaming_When_OperationIsSubscription()
    {
        // arrange
        var handler = new CapturingHandler(
            "text/event-stream",
            "event: next\ndata: {\"data\":{}}\n\nevent: complete\n\n");
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/graphql") };

        var document = new TestDocument(OperationKind.Subscription, "subscription Test { __typename }");
        var request = new OperationRequest("Test", document);

        // act
        var connection = new HttpConnection(() => client);
        await foreach (var _ in connection.ExecuteAsync(request))
        {
        }

        // assert
        Assert.NotNull(handler.CapturedRequest);
        var found = handler.CapturedRequest!.Options.TryGetValue(s_streamingOption, out var enabled);
        Assert.True(found);
        Assert.True(enabled);
    }

    [Fact]
    public async Task CreateHttpRequest_Should_NotEnableResponseStreaming_When_OperationIsQuery()
    {
        // arrange
        var handler = new CapturingHandler(
            "application/graphql-response+json",
            "{\"data\":{}}");
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/graphql") };

        var document = new TestDocument(OperationKind.Query, "query Test { __typename }");
        var request = new OperationRequest("Test", document);

        // act
        var connection = new HttpConnection(() => client);
        await foreach (var _ in connection.ExecuteAsync(request))
        {
        }

        // assert
        Assert.NotNull(handler.CapturedRequest);
        var found = handler.CapturedRequest!.Options.TryGetValue(s_streamingOption, out _);
        Assert.False(found);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly string _contentType;
        private readonly string _body;

        public CapturingHandler(string contentType, string body)
        {
            _contentType = contentType;
            _body = body;
        }

        public HttpRequestMessage? CapturedRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CapturedRequest = request;

            var content = new ByteArrayContent(Encoding.UTF8.GetBytes(_body));
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(_contentType);

            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
            return Task.FromResult(response);
        }
    }

    private sealed class TestDocument : IDocument
    {
        private readonly byte[] _query;

        public TestDocument(OperationKind kind, [StringSyntax("graphql")] string query)
        {
            Kind = kind;
            _query = Encoding.UTF8.GetBytes(query);
        }

        public OperationKind Kind { get; }

        public ReadOnlySpan<byte> Body => _query;

        public DocumentHash Hash { get; } = new("MD5", "ABC");
    }
}
