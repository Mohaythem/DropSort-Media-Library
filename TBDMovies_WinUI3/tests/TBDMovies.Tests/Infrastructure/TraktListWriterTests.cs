using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TBDMovies.Infrastructure.Trakt;
using Xunit;

namespace TBDMovies.Tests.Infrastructure;

public sealed class TraktListWriterTests
{
    [Fact]
    public async Task AddMovieToList_UsesAllowlistedEndpointAndSingleMovieBody()
    {
        var handler = new CaptureHandler(_ => new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent("{\"added\":{\"movies\":[{}]},\"existing\":{\"movies\":[]},\"not_found\":{\"movies\":[]}}")
        });
        using var client = new HttpClient(handler);
        using var trakt = new TraktDataClient(client);

        await trakt.AddMovieToListAsync(42, 123, "client", "token");

        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://api.trakt.tv/users/me/lists/42/items", request.RequestUri!.ToString());
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("token", request.Headers.Authorization.Parameter);
        Assert.Equal("{\"movies\":[{\"ids\":{\"tmdb\":123}}]}", Assert.Single(handler.Bodies));
    }

    [Fact]
    public async Task AddMovieToList_DoesNotRetryMutationOnServerError()
    {
        var handler = new CaptureHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("server detail that must not leak")
        });
        using var client = new HttpClient(handler);
        using var trakt = new TraktDataClient(client);

        var failure = await Assert.ThrowsAsync<TBDMovies.Application.Trakt.TraktClientException>(() =>
            trakt.AddMovieToListAsync(null, 123, "client", "token"));

        Assert.Equal(TBDMovies.Application.Trakt.TraktFailureKind.HttpError, failure.FailureKind);
        Assert.Single(handler.Requests);
        Assert.DoesNotContain("server detail", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Redirect)]
    public async Task AddMovieToList_DoesNotRetryAnyFailure(HttpStatusCode code)
    {
        var handler = new CaptureHandler(_ => new HttpResponseMessage(code));
        using var client = new HttpClient(handler);
        using var trakt = new TraktDataClient(client);
        await Assert.ThrowsAsync<TBDMovies.Application.Trakt.TraktClientException>(() =>
            trakt.AddMovieToListAsync(null, 123, "client", "token"));
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(0L, 123L)]
    [InlineData(-1L, 123L)]
    [InlineData(1L, 0L)]
    public async Task AddMovieToList_RejectsInvalidIdsBeforeSend(long listId, long tmdbId)
    {
        var handler = new CaptureHandler(_ => new HttpResponseMessage(HttpStatusCode.Created));
        using var client = new HttpClient(handler);
        using var trakt = new TraktDataClient(client);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => trakt.AddMovieToListAsync(listId, tmdbId, "client", "token"));
        Assert.Empty(handler.Requests);
    }

    private sealed class CaptureHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = new();
        public List<string> Bodies { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (request.Content != null) Bodies.Add(request.Content.ReadAsStringAsync().GetAwaiter().GetResult());
            return Task.FromResult(responseFactory(request));
        }
    }
}
