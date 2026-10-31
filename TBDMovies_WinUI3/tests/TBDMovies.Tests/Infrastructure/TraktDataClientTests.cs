using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TBDMovies.Application.Trakt;
using TBDMovies.Infrastructure.Trakt;
using Xunit;

namespace TBDMovies.Tests.Infrastructure;

public sealed class TraktDataClientTests
{
    [Theory]
    [InlineData(60, false)]
    [InlineData(60, true)]
    [InlineData(61, false)]
    [InlineData(61, true)]
    public async Task RetryAfter_AtCapRetries_AboveCapFailsWithoutWaiting(int seconds, bool useDate)
    {
        var handler = new FakeHttpMessageHandler();
        var clock = new FakeTimeProvider();
        var delays = new List<TimeSpan>();
        handler.Enqueue(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = useDate
                ? new RetryConditionHeaderValue(clock.GetUtcNow().AddSeconds(seconds))
                : new RetryConditionHeaderValue(TimeSpan.FromSeconds(seconds));
            return response;
        });
        handler.Enqueue(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") };
            response.Headers.Add("X-Pagination-Page", "1");
            response.Headers.Add("X-Pagination-Limit", "250");
            response.Headers.Add("X-Pagination-Page-Count", "1");
            response.Headers.Add("X-Pagination-Item-Count", "0");
            return response;
        });
        using var http = new HttpClient(handler);
        using var client = new TraktDataClient(http, clock,
            delayAsync: (delay, _) => { delays.Add(delay); return Task.CompletedTask; });
        if (seconds == 60)
        {
            await client.GetResourceAsync(TraktResourceSpec.Watchlist(), TestClientId, TestAccessToken);
            Assert.Equal(TimeSpan.FromSeconds(60), Assert.Single(delays));
            Assert.Equal(2, handler.CapturedRequests.Count);
        }
        else
        {
            var failure = await Assert.ThrowsAsync<TraktClientException>(() =>
                client.GetResourceAsync(TraktResourceSpec.Watchlist(), TestClientId, TestAccessToken));
            Assert.Equal(TraktFailureKind.RateLimited, failure.FailureKind);
            Assert.Empty(delays);
            Assert.Single(handler.CapturedRequests);
        }
    }

    [Fact]
    public async Task Watched_shows_requests_progress_on_every_page_and_projects_nested_episodes()
    {
        var handler = new FakeHttpMessageHandler();
        for (var page = 1; page <= 2; page++)
        {
            var currentPage = page;
            handler.Enqueue(request =>
            {
                var product = Assert.Single(request.Headers.UserAgent).Product;
                Assert.NotNull(product);
                Assert.Equal("TBDMovies", product.Name);
                Assert.True(Version.TryParse(product.Version, out _));
                Assert.Contains("extended=progress", request.RequestUri!.Query);
                Assert.Contains($"page={currentPage}", request.RequestUri.Query);
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""[{"plays":1,"show":{"ids":{"trakt":20}},"seasons":[{"number":1,"episodes":[{"number":1,"plays":1}]}]}]""")
                };
                response.Headers.Add("X-Pagination-Page", currentPage.ToString());
                response.Headers.Add("X-Pagination-Limit", "1");
                response.Headers.Add("X-Pagination-Page-Count", "2");
                response.Headers.Add("X-Pagination-Item-Count", "2");
                return response;
            });
        }
        using var http = new HttpClient(handler);
        using var client = new TraktDataClient(http);
        var raw = await client.GetResourceAsync(TraktResourceSpec.WatchedShows(), TestClientId, TestAccessToken);
        var parsed = TraktDataProjections.ParseWatchedShows(raw);
        var projected = new TraktProjectionService().ProjectWatchedShows(parsed, new TraktLocalSnapshot([], DateTimeOffset.UtcNow));
        Assert.Equal(2, projected.Count);
        Assert.All(projected, row => Assert.Equal(1, row.WatchedEpisodeCount));
        Assert.Equal(2, handler.CapturedRequests.Count);
    }

    [Theory]
    [InlineData("2", "1", "1", "1")]
    [InlineData("1", "0", "1", "1")]
    [InlineData("1", "250", "-1", "0")]
    [InlineData("1", "250", "0", "1")]
    public async Task Invalid_first_page_headers_cannot_publish_a_snapshot(string page, string limit, string pages, string items)
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") };
            response.Headers.Add("X-Pagination-Page", page);
            response.Headers.Add("X-Pagination-Limit", limit);
            response.Headers.Add("X-Pagination-Page-Count", pages);
            response.Headers.Add("X-Pagination-Item-Count", items);
            return response;
        });
        using var http = new HttpClient(handler);
        using var client = new TraktDataClient(http);
        var failure = await Assert.ThrowsAsync<TraktClientException>(() =>
            client.GetResourceAsync(TraktResourceSpec.History(), "synthetic-client", "synthetic-token"));
        Assert.Equal(TraktFailureKind.InvalidResponse, failure.FailureKind);
        Assert.Single(handler.CapturedRequests);
    }

    private const string TestClientId = "test_client_id_123";
    private const string TestAccessToken = "test_access_token_abc";

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();

        public List<HttpRequestMessage> CapturedRequests { get; } = new();

        public void Enqueue(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responses.Enqueue(responseFactory);
        }

        public void Enqueue(HttpResponseMessage response)
        {
            _responses.Enqueue(_ => response);
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CapturedRequests.Add(request);
            if (_responses.Count == 0)
            {
                throw new InvalidOperationException("No queued HTTP response for test request.");
            }

            var factory = _responses.Dequeue();
            return Task.FromResult(factory(request));
        }
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow = new(2026, 10, 21, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan delta) => _utcNow += delta;
    }

    [Fact]
    public async Task GetAccountAsync_ValidPayload_ReturnsClonedSnapshot_AndExtractsUuid()
    {
        var handler = new FakeHttpMessageHandler();
        const string payload = """
        {
            "user": {
                "username": "tester",
                "name": "Test User",
                "private": false,
                "vip": true,
                "ids": {
                    "slug": "tester",
                    "uuid": "4f5e6d7c-8b9a-0123-4567-89abcdef0123"
                }
            }
        }
        """;

        handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        });

        using var httpClient = new HttpClient(handler);
        using var client = new TraktDataClient(httpClient);

        var snapshot = await client.GetAccountAsync(TestClientId, TestAccessToken);

        Assert.Equal(JsonValueKind.Object, snapshot.ValueKind);
        var profile = TraktDataProjections.ParseAccountProfile(snapshot);
        Assert.Equal("4f5e6d7c-8b9a-0123-4567-89abcdef0123", profile.Uuid);
        Assert.Equal("tester", profile.Username);
        Assert.True(profile.IsVip);

        // Verify request headers
        var captured = handler.CapturedRequests[0];
        Assert.Equal("https://api.trakt.tv/users/settings", captured.RequestUri!.ToString());
        Assert.Equal("Bearer", captured.Headers.Authorization!.Scheme);
        Assert.Equal(TestAccessToken, captured.Headers.Authorization!.Parameter);
        Assert.Equal(TestClientId, captured.Headers.GetValues("trakt-api-key").First());
        Assert.Equal("2", captured.Headers.GetValues("trakt-api-version").First());
    }

    [Fact]
    public async Task GetAccountAsync_MissingUuid_ThrowsInvalidResponse()
    {
        var handler = new FakeHttpMessageHandler();
        const string payload = """
        {
            "user": {
                "username": "tester",
                "ids": {
                    "slug": "tester"
                }
            }
        }
        """;

        handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        });

        using var httpClient = new HttpClient(handler);
        using var client = new TraktDataClient(httpClient);

        var ex = await Assert.ThrowsAsync<TraktClientException>(
            () => client.GetAccountAsync(TestClientId, TestAccessToken));

        Assert.Equal(TraktFailureKind.InvalidResponse, ex.FailureKind);
    }

    [Fact]
    public async Task GetAccountAsync_Unauthorized401_ThrowsImmediatelyWithoutRetry()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        using var httpClient = new HttpClient(handler);
        using var client = new TraktDataClient(httpClient);

        var ex = await Assert.ThrowsAsync<TraktClientException>(
            () => client.GetAccountAsync(TestClientId, TestAccessToken));

        Assert.Equal(TraktFailureKind.Unauthorized, ex.FailureKind);
        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
        Assert.Single(handler.CapturedRequests);
    }

    [Fact]
    public void TraktResourceSpec_ListItems_ZeroOrNegativeId_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TraktResourceSpec.ListItems(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => TraktResourceSpec.ListItems(-5));

        var spec = TraktResourceSpec.ListItems(90001);
        Assert.Equal("users/me/lists/90001/items", spec.GetRelativePath());
    }

    [Fact]
    public async Task GetResourceAsync_ListItems_Success_PathContainsListId()
    {
        var handler = new FakeHttpMessageHandler();
        const string payload = """
        [
            { "id": 501, "rank": 1, "type": "movie", "movie": { "title": "Listed Film", "year": 2024, "ids": { "trakt": 10 } } }
        ]
        """;

        handler.Enqueue(_ =>
        {
            var res = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            res.Headers.Add("X-Pagination-Page", "1");
            res.Headers.Add("X-Pagination-Limit", "250");
            res.Headers.Add("X-Pagination-Page-Count", "1");
            res.Headers.Add("X-Pagination-Item-Count", "1");
            return res;
        });

        using var httpClient = new HttpClient(handler);
        using var client = new TraktDataClient(httpClient);

        var snapshot = await client.GetResourceAsync(TraktResourceSpec.ListItems(777), TestClientId, TestAccessToken);

        Assert.Equal(JsonValueKind.Array, snapshot.ValueKind);
        Assert.Contains("users/me/lists/777/items", handler.CapturedRequests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task GetResourceAsync_Watchlist_MultiPage_AggregatesAndVerifiesPagination()
    {
        var handler = new FakeHttpMessageHandler();

        const string page1Payload = """
        [
            { "id": 101, "rank": 1, "type": "movie", "movie": { "title": "Film A", "year": 2024, "ids": { "trakt": 1 } } }
        ]
        """;

        const string page2Payload = """
        [
            { "id": 102, "rank": 2, "type": "movie", "movie": { "title": "Film B", "year": 2023, "ids": { "trakt": 2 } } }
        ]
        """;

        handler.Enqueue(_ =>
        {
            var res = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(page1Payload, Encoding.UTF8, "application/json")
            };
            res.Headers.Add("X-Pagination-Page", "1");
            res.Headers.Add("X-Pagination-Limit", "250");
            res.Headers.Add("X-Pagination-Page-Count", "2");
            res.Headers.Add("X-Pagination-Item-Count", "2");
            return res;
        });

        handler.Enqueue(_ =>
        {
            var res = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(page2Payload, Encoding.UTF8, "application/json")
            };
            res.Headers.Add("X-Pagination-Page", "2");
            res.Headers.Add("X-Pagination-Limit", "250");
            res.Headers.Add("X-Pagination-Page-Count", "2");
            res.Headers.Add("X-Pagination-Item-Count", "2");
            return res;
        });

        using var httpClient = new HttpClient(handler);
        using var client = new TraktDataClient(httpClient);

        var snapshot = await client.GetResourceAsync(TraktResourceSpec.Watchlist(), TestClientId, TestAccessToken);

        Assert.Equal(JsonValueKind.Array, snapshot.ValueKind);
        var watchlist = TraktDataProjections.ParseWatchlist(snapshot);
        Assert.Equal(2, watchlist.Count);
        Assert.Equal(101, watchlist[0].Id);
        Assert.Equal(102, watchlist[1].Id);
        Assert.Equal(2, handler.CapturedRequests.Count);
    }

    [Fact]
    public async Task GetResourceAsync_MissingPaginationHeaders_ThrowsInvalidResponse()
    {
        var handler = new FakeHttpMessageHandler();
        const string payload = "[ { \"id\": 1 } ]";

        handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        });

        using var httpClient = new HttpClient(handler);
        using var client = new TraktDataClient(httpClient);

        var ex = await Assert.ThrowsAsync<TraktClientException>(
            () => client.GetResourceAsync(TraktResourceSpec.Watchlist(), TestClientId, TestAccessToken));

        Assert.Equal(TraktFailureKind.InvalidResponse, ex.FailureKind);
    }

    [Fact]
    public async Task GetResourceAsync_PrematureEmptyPage_ThrowsInvalidResponse_NeverStopsEarly()
    {
        var handler = new FakeHttpMessageHandler();

        handler.Enqueue(_ =>
        {
            var res = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json")
            };
            res.Headers.Add("X-Pagination-Page", "1");
            res.Headers.Add("X-Pagination-Limit", "250");
            res.Headers.Add("X-Pagination-Page-Count", "2");
            res.Headers.Add("X-Pagination-Item-Count", "2");
            return res;
        });

        using var httpClient = new HttpClient(handler);
        using var client = new TraktDataClient(httpClient);

        var ex = await Assert.ThrowsAsync<TraktClientException>(
            () => client.GetResourceAsync(TraktResourceSpec.WatchedMovies(), TestClientId, TestAccessToken));

        Assert.Equal(TraktFailureKind.InvalidResponse, ex.FailureKind);
        Assert.Contains("before advertised", ex.Message);
    }

    [Fact]
    public async Task GetResourceAsync_CrossPageHeaderMismatch_ThrowsInvalidResponse()
    {
        var handler = new FakeHttpMessageHandler();

        handler.Enqueue(_ =>
        {
            var res = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[ { \"id\": 1 } ]", Encoding.UTF8, "application/json")
            };
            res.Headers.Add("X-Pagination-Page", "1");
            res.Headers.Add("X-Pagination-Limit", "250");
            res.Headers.Add("X-Pagination-Page-Count", "2");
            res.Headers.Add("X-Pagination-Item-Count", "2");
            return res;
        });

        handler.Enqueue(_ =>
        {
            var res = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[ { \"id\": 2 } ]", Encoding.UTF8, "application/json")
            };
            // Header inconsistency: Page-Count changed mid-stream to 3
            res.Headers.Add("X-Pagination-Page", "2");
            res.Headers.Add("X-Pagination-Limit", "250");
            res.Headers.Add("X-Pagination-Page-Count", "3");
            res.Headers.Add("X-Pagination-Item-Count", "2");
            return res;
        });

        using var httpClient = new HttpClient(handler);
        using var client = new TraktDataClient(httpClient);

        var ex = await Assert.ThrowsAsync<TraktClientException>(
            () => client.GetResourceAsync(TraktResourceSpec.WatchedMovies(), TestClientId, TestAccessToken));

        Assert.Equal(TraktFailureKind.InvalidResponse, ex.FailureKind);
        Assert.Contains("inconsistent", ex.Message);
    }

    [Fact]
    public async Task GetResourceAsync_ItemCountMismatchAtEnd_ThrowsInvalidResponse()
    {
        var handler = new FakeHttpMessageHandler();

        handler.Enqueue(_ =>
        {
            var res = new HttpResponseMessage(HttpStatusCode.OK)
            {
                // Returns 1 item while advertised 2
                Content = new StringContent("[ { \"id\": 1 } ]", Encoding.UTF8, "application/json")
            };
            res.Headers.Add("X-Pagination-Page", "1");
            res.Headers.Add("X-Pagination-Limit", "250");
            res.Headers.Add("X-Pagination-Page-Count", "1");
            res.Headers.Add("X-Pagination-Item-Count", "2");
            return res;
        });

        using var httpClient = new HttpClient(handler);
        using var client = new TraktDataClient(httpClient);

        var ex = await Assert.ThrowsAsync<TraktClientException>(
            () => client.GetResourceAsync(TraktResourceSpec.History(), TestClientId, TestAccessToken));

        Assert.Equal(TraktFailureKind.InvalidResponse, ex.FailureKind);
        Assert.Contains("does not match advertised", ex.Message);
    }

    [Fact]
    public async Task GetResourceAsync_ValidatedEmptyDataset_ReturnsEmptyArraySnapshot()
    {
        var handler = new FakeHttpMessageHandler();

        handler.Enqueue(_ =>
        {
            var res = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json")
            };
            res.Headers.Add("X-Pagination-Page", "1");
            res.Headers.Add("X-Pagination-Limit", "250");
            res.Headers.Add("X-Pagination-Page-Count", "0");
            res.Headers.Add("X-Pagination-Item-Count", "0");
            return res;
        });

        using var httpClient = new HttpClient(handler);
        using var client = new TraktDataClient(httpClient);

        var snapshot = await client.GetResourceAsync(TraktResourceSpec.Watchlist(), TestClientId, TestAccessToken);

        Assert.Equal(JsonValueKind.Array, snapshot.ValueKind);
        Assert.Equal(0, snapshot.GetArrayLength());
    }

    [Fact]
    public async Task GetResourceAsync_UserLists_UnpaginatedContract_Succeeds()
    {
        var handler = new FakeHttpMessageHandler();
        const string payload = """
        [
            {
                "name": "Custom 1",
                "item_count": 5,
                "ids": { "trakt": 12345, "slug": "custom-1" }
            }
        ]
        """;

        handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        });

        using var httpClient = new HttpClient(handler);
        using var client = new TraktDataClient(httpClient);

        var snapshot = await client.GetResourceAsync(TraktResourceSpec.UserLists(), TestClientId, TestAccessToken);

        Assert.Equal(JsonValueKind.Array, snapshot.ValueKind);
        var lists = TraktDataProjections.ParseLists(snapshot);
        Assert.Single(lists);
        Assert.Equal(12345, lists[0].TraktId);
        Assert.Equal("Custom 1", lists[0].Name);
    }

    [Fact]
    public async Task GetResourceAsync_UserLists_Paginated_FollowsPagination()
    {
        var handler = new FakeHttpMessageHandler();
        const string payload = """
        [
            {
                "name": "Custom Paginated",
                "item_count": 10,
                "ids": { "trakt": 99999, "slug": "custom-paginated" }
            }
        ]
        """;

        handler.Enqueue(_ =>
        {
            var res = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            res.Headers.Add("X-Pagination-Page", "1");
            res.Headers.Add("X-Pagination-Limit", "250");
            res.Headers.Add("X-Pagination-Page-Count", "1");
            res.Headers.Add("X-Pagination-Item-Count", "1");
            return res;
        });

        using var httpClient = new HttpClient(handler);
        using var client = new TraktDataClient(httpClient);

        var snapshot = await client.GetResourceAsync(TraktResourceSpec.UserLists(), TestClientId, TestAccessToken);

        Assert.Equal(JsonValueKind.Array, snapshot.ValueKind);
        var lists = TraktDataProjections.ParseLists(snapshot);
        Assert.Single(lists);
        Assert.Equal(99999, lists[0].TraktId);
    }

    [Fact]
    public async Task GetResourceAsync_429_RetryAfterDelta_RetriesAndSucceeds()
    {
        var handler = new FakeHttpMessageHandler();
        var timeProvider = new FakeTimeProvider();
        var recordedDelays = new List<TimeSpan>();

        handler.Enqueue(_ =>
        {
            var res = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            res.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(2));
            return res;
        });

        handler.Enqueue(_ =>
        {
            var res = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[ { \"id\": 500 } ]", Encoding.UTF8, "application/json")
            };
            res.Headers.Add("X-Pagination-Page", "1");
            res.Headers.Add("X-Pagination-Limit", "250");
            res.Headers.Add("X-Pagination-Page-Count", "1");
            res.Headers.Add("X-Pagination-Item-Count", "1");
            return res;
        });

        using var httpClient = new HttpClient(handler);
        using var client = new TraktDataClient(
            httpClient,
            timeProvider,
            delayAsync: (d, ct) => { recordedDelays.Add(d); return Task.CompletedTask; });

        var snapshot = await client.GetResourceAsync(TraktResourceSpec.History(), TestClientId, TestAccessToken);

        Assert.Equal(JsonValueKind.Array, snapshot.ValueKind);
        Assert.Single(recordedDelays);
        Assert.InRange(recordedDelays[0].TotalSeconds, 2.0, 3.0);
    }

    [Fact]
    public async Task GetResourceAsync_429_RetryAfterHttpDate_RetriesAndSucceeds()
    {
        var handler = new FakeHttpMessageHandler();
        var timeProvider = new FakeTimeProvider();
        var recordedDelays = new List<TimeSpan>();

        handler.Enqueue(_ =>
        {
            var res = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            res.Headers.RetryAfter = new RetryConditionHeaderValue(timeProvider.GetUtcNow().AddSeconds(3));
            return res;
        });

        handler.Enqueue(_ =>
        {
            var res = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[ { \"id\": 501 } ]", Encoding.UTF8, "application/json")
            };
            res.Headers.Add("X-Pagination-Page", "1");
            res.Headers.Add("X-Pagination-Limit", "250");
            res.Headers.Add("X-Pagination-Page-Count", "1");
            res.Headers.Add("X-Pagination-Item-Count", "1");
            return res;
        });

        using var httpClient = new HttpClient(handler);
        using var client = new TraktDataClient(
            httpClient,
            timeProvider,
            delayAsync: (d, ct) => { recordedDelays.Add(d); return Task.CompletedTask; });

        var snapshot = await client.GetResourceAsync(TraktResourceSpec.History(), TestClientId, TestAccessToken);

        Assert.Equal(JsonValueKind.Array, snapshot.ValueKind);
        Assert.Single(recordedDelays);
        Assert.InRange(recordedDelays[0].TotalSeconds, 3.0, 4.0);
    }

    [Fact]
    public async Task GetResourceAsync_429_RetriesExhausted_ThrowsRateLimited()
    {
        var handler = new FakeHttpMessageHandler();
        var timeProvider = new FakeTimeProvider();

        for (int i = 0; i < 4; i++)
        {
            handler.Enqueue(_ =>
            {
                var res = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                res.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(1));
                return res;
            });
        }

        using var httpClient = new HttpClient(handler);
        using var client = new TraktDataClient(
            httpClient,
            timeProvider,
            delayAsync: (_, _) => Task.CompletedTask);

        var ex = await Assert.ThrowsAsync<TraktClientException>(
            () => client.GetResourceAsync(TraktResourceSpec.Watchlist(), TestClientId, TestAccessToken));

        Assert.Equal(TraktFailureKind.RateLimited, ex.FailureKind);
    }

    [Fact]
    public async Task GetResourceAsync_HttpRedirect_ThrowsHttpError()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.MovedPermanently));

        using var httpClient = new HttpClient(handler);
        using var client = new TraktDataClient(httpClient);

        var ex = await Assert.ThrowsAsync<TraktClientException>(
            () => client.GetResourceAsync(TraktResourceSpec.Watchlist(), TestClientId, TestAccessToken));

        Assert.Equal(TraktFailureKind.HttpError, ex.FailureKind);
        Assert.Contains("redirect not permitted", ex.Message);
    }

    [Fact]
    public async Task GetResourceAsync_ResponseBytesExceeded_ThrowsInvalidResponse()
    {
        var handler = new FakeHttpMessageHandler();
        var largeString = new string('x', 2000);

        handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(largeString, Encoding.UTF8, "application/json")
        });

        using var httpClient = new HttpClient(handler);
        var options = new TraktDataClientOptions(MaxResponseBytesPerPage: 1000);
        using var client = new TraktDataClient(httpClient, options: options);

        var ex = await Assert.ThrowsAsync<TraktClientException>(
            () => client.GetResourceAsync(TraktResourceSpec.Watchlist(), TestClientId, TestAccessToken));

        Assert.Equal(TraktFailureKind.InvalidResponse, ex.FailureKind);
        Assert.Contains("exceeded safe limit", ex.Message);
    }

    [Fact]
    public async Task JsonElement_CloneOwnership_SurvivesClientDisposal()
    {
        var handler = new FakeHttpMessageHandler();
        const string payload = "[ { \"id\": 999, \"rank\": 1, \"type\": \"movie\" } ]";

        handler.Enqueue(_ =>
        {
            var res = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            res.Headers.Add("X-Pagination-Page", "1");
            res.Headers.Add("X-Pagination-Limit", "250");
            res.Headers.Add("X-Pagination-Page-Count", "1");
            res.Headers.Add("X-Pagination-Item-Count", "1");
            return res;
        });

        JsonElement snapshot;
        using (var httpClient = new HttpClient(handler))
        using (var client = new TraktDataClient(httpClient))
        {
            snapshot = await client.GetResourceAsync(TraktResourceSpec.Watchlist(), TestClientId, TestAccessToken);
        }

        // Must still be readable after client and inner documents are disposed
        Assert.Equal(JsonValueKind.Array, snapshot.ValueKind);
        var items = snapshot.EnumerateArray().ToList();
        Assert.Single(items);
        Assert.Equal(999, items[0].GetProperty("id").GetInt64());
    }

    [Fact]
    public void Projections_RetainPersonAndUnknownTypes_AndExtractLongHistoryIds()
    {
        const string payload = """
        [
            {
                "id": 9900000000001,
                "rank": 1,
                "type": "person",
                "notes": "Director notes",
                "person": {
                    "name": "Jane Director",
                    "ids": { "trakt": 505 }
                }
            },
            {
                "id": 9900000000002,
                "rank": 2,
                "type": "movie",
                "movie": {
                    "title": "Alpha Movie",
                    "year": 2024,
                    "ids": { "trakt": 101 }
                }
            }
        ]
        """;

        using var doc = JsonDocument.Parse(payload);
        var listItems = TraktDataProjections.ParseListItems(doc.RootElement);

        Assert.Equal(2, listItems.Count);
        Assert.Equal(9900000000001L, listItems[0].Id);
        Assert.Equal("person", listItems[0].Type);
        Assert.Equal("Jane Director", listItems[0].Title);
        Assert.Equal("Director notes", listItems[0].Notes);
        Assert.NotEqual(JsonValueKind.Undefined, listItems[0].RawElement.ValueKind);

        Assert.Equal(9900000000002L, listItems[1].Id);
        Assert.Equal("movie", listItems[1].Type);
        Assert.Equal("Alpha Movie", listItems[1].Title);
    }

    [Fact]
    public void Projections_WatchedShowsWithSeasons_ParsesNestedEpisodes()
    {
        const string payload = """
        [
            {
                "plays": 12,
                "last_watched_at": "2024-02-18T22:30:00.000Z",
                "show": {
                    "title": "Beta Series",
                    "year": 2023,
                    "ids": { "trakt": 30001, "imdb": "tt2000001" }
                },
                "seasons": [
                    {
                        "number": 1,
                        "episodes": [
                            { "number": 1, "plays": 1, "last_watched_at": "2024-02-10T20:00:00.000Z" },
                            { "number": 2, "plays": 2, "last_watched_at": "2024-02-18T22:30:00.000Z" }
                        ]
                    }
                ]
            }
        ]
        """;

        using var doc = JsonDocument.Parse(payload);
        var shows = TraktDataProjections.ParseWatchedShows(doc.RootElement);

        Assert.Single(shows);
        Assert.Equal(30001, shows[0].TraktId);
        Assert.Equal("Beta Series", shows[0].Title);
        Assert.Single(shows[0].Seasons);
        Assert.Equal(1, shows[0].Seasons[0].Number);
        Assert.Equal(2, shows[0].Seasons[0].Episodes.Count);
        Assert.Equal(2, shows[0].Seasons[0].Episodes[1].Plays);
    }
}
