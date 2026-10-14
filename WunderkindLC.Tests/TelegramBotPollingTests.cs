using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using WunderkindLC.Application.Services;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// MAIN bot long polling (<see cref="TelegramBotService"/>) must SURVIVE an HttpClient timeout.
/// <para>A timeout surfaces as <see cref="TaskCanceledException"/> (an <see cref="OperationCanceledException"/>).
/// The loop used to <c>break</c> on ANY cancellation, so one network hiccup stopped the bot silently: no log,
/// updates piled up on Telegram, and only a container restart revived it (prod, 2026-10-14). Only the
/// app's own <c>stoppingToken</c> may end the loop.</para>
/// </summary>
public class TelegramBotPollingTests
{
    private sealed class FakeEnv : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "WunderkindLC.Tests";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    /// <summary>First getUpdates — timeout (TaskCanceledException); later ones — empty batch.</summary>
    private sealed class FlakyTelegram : IHttpClientFactory
    {
        private int _calls;
        public readonly TaskCompletionSource SecondPoll = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private sealed class Handler(FlakyTelegram owner) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                if (!request.RequestUri!.AbsolutePath.EndsWith("/getUpdates"))
                    throw new InvalidOperationException("Unexpected Telegram call: " + request.RequestUri.AbsolutePath);
                if (Interlocked.Increment(ref owner._calls) == 1)
                    throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout");
                owner.SecondPoll.TrySetResult();
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"ok\":true,\"result\":[]}", Encoding.UTF8, "application/json"),
                });
            }
        }

        public HttpClient CreateClient(string name) => new(new Handler(this));
    }

    [Fact]
    public async Task Bot_survives_getUpdates_timeout_and_keeps_polling()
    {
        using var _ = TelegramTokenScope.Use();
        var http = new FlakyTelegram();
        var telegram = new TelegramService(http, NullLogger<TelegramService>.Instance);
        var bookShop = new BookShopBotService(telegram, new FakeEnv(), NullLogger<BookShopBotService>.Instance);
        var bot = new TelegramBotService(new ServiceCollection().BuildServiceProvider(), telegram, bookShop,
            NullLogger<TelegramBotService>.Instance);

        await bot.StartAsync(CancellationToken.None);
        try
        {
            // The fix waits 3 s after a timeout, then polls again. Before the fix the loop exited — no second poll.
            var done = await Task.WhenAny(http.SecondPoll.Task, Task.Delay(TimeSpan.FromSeconds(15)));
            Assert.True(done == http.SecondPoll.Task, "Bot stopped polling after a getUpdates timeout");
        }
        finally
        {
            await bot.StopAsync(CancellationToken.None);
        }
    }
}
