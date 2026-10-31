using Microsoft.Extensions.Time.Testing;
using ShellyHwinfoBridge.Core.Curves;
using ShellyHwinfoBridge.Core.Polling;
using ShellyHwinfoBridge.Core.Shelly;
using ShellyHwinfoBridge.Core.Tests.TestSupport;

namespace ShellyHwinfoBridge.Core.Tests.Polling;

public class PollingServiceTests
{
    private readonly FakeShellyClient _client = new();

    private PollingService CreateService(TimeProvider? time = null, FakeShellyClient? client = null, bool withCurve = true, string deviceId = "pc", TimeSpan startDelay = default) => new(
        client ?? _client,
        withCurve ? new PowerCalculator(PsuCurveSet.Single(TestCurves.Simple)) : null,
        new PollingOptions(),
        timeProvider: time,
        deviceId: deviceId,
        startDelay: startDelay);

    [Fact]
    public async Task SuccessfulPollReportsTheReadingWithTheDeviceIdAndGoesOnline()
    {
        var service = CreateService();
        var states = new List<OnlineState>();
        PowerReadingEventArgs? received = null;
        service.OnlineStateChanged += (_, e) => states.Add(e.Current);
        service.ReadingReceived += (_, e) => received = e;
        _client.Enqueue(new ShellyReading(150, 230, 0.6));

        Assert.True(await service.PollOnceAsync());

        Assert.Equal(OnlineState.Online, service.State);
        Assert.Equal([OnlineState.Online], states);
        Assert.Equal(130, received!.Power.DcWatts, 9);
        Assert.Equal(20, received.Power.LossWatts, 9);
        Assert.Equal(230, received.Raw.Volts);
        Assert.Equal("pc", received.DeviceId);
        Assert.True(received.IsPrimary);
    }

    [Fact]
    public async Task ADeviceWithoutCurveReportsAcOnly()
    {
        var service = CreateService(withCurve: false, deviceId: "monitor-id");
        PowerReadingEventArgs? received = null;
        service.ReadingReceived += (_, e) => received = e;
        _client.Enqueue(new ShellyReading(31.2, 230, 0.2));

        await service.PollOnceAsync();

        Assert.Equal(31.2, received!.Power.AcWatts, 9);
        Assert.Equal(0, received.Power.DcWatts);
        Assert.Equal(0, received.Power.LossWatts);
        Assert.Equal("monitor-id", received.DeviceId);
        Assert.False(received.IsPrimary);
    }

    [Fact]
    public async Task GoesOfflineAfterThreeFailuresAndRecovers()
    {
        var service = CreateService();
        var transitions = new List<(OnlineState From, OnlineState To)>();
        var failures = new List<PollFailedEventArgs>();
        service.OnlineStateChanged += (_, e) => transitions.Add((e.Previous, e.Current));
        service.PollFailed += (_, e) => failures.Add(e);

        _client.Enqueue(new ShellyReading(150, 230, 0.6));
        await service.PollOnceAsync();

        _client.EnqueueFailure();
        _client.EnqueueFailure();
        await service.PollOnceAsync();
        await service.PollOnceAsync();
        Assert.Equal(OnlineState.Online, service.State);
        Assert.Equal(2, service.ConsecutiveFailures);

        _client.EnqueueFailure();
        await service.PollOnceAsync();
        Assert.Equal(OnlineState.Offline, service.State);

        // Keeps retrying while offline, without repeating the transition.
        _client.EnqueueFailure();
        await service.PollOnceAsync();
        Assert.Equal(4, service.ConsecutiveFailures);
        Assert.Equal([1, 2, 3, 4], failures.Select(f => f.ConsecutiveFailures));
        Assert.Equal([OnlineState.Online, OnlineState.Online, OnlineState.Offline, OnlineState.Offline], failures.Select(f => f.State));

        _client.Enqueue(new ShellyReading(200, 230, 0.8));
        await service.PollOnceAsync();
        Assert.Equal(OnlineState.Online, service.State);
        Assert.Equal(0, service.ConsecutiveFailures);

        Assert.Equal(
            [(OnlineState.Unknown, OnlineState.Online), (OnlineState.Online, OnlineState.Offline), (OnlineState.Offline, OnlineState.Online)],
            transitions);
    }

    [Fact]
    public async Task OfflineFromStartAfterThreeFailures()
    {
        var service = CreateService();
        for (var i = 0; i < 3; i++)
        {
            _client.EnqueueFailure();
            await service.PollOnceAsync();
        }

        Assert.Equal(OnlineState.Offline, service.State);
    }

    [Fact]
    public async Task TimeoutCountsAsFailure()
    {
        var service = CreateService();
        _client.Hang = true;

        for (var i = 0; i < 3; i++)
        {
            await service.PollOnceAsync();
        }

        Assert.Equal(OnlineState.Offline, service.State);
    }

    [Fact]
    public async Task PausedServiceDoesNotPollAndGoesUnknown()
    {
        var service = CreateService();
        _client.Enqueue(new ShellyReading(150, 230, 0.6));
        await service.PollOnceAsync();

        service.Pause();
        Assert.Equal(OnlineState.Unknown, service.State);
        Assert.False(await service.PollOnceAsync());
        Assert.Equal(1, _client.Calls);

        service.Resume();
        _client.Enqueue(new ShellyReading(150, 230, 0.6));
        Assert.True(await service.PollOnceAsync());
        Assert.Equal(OnlineState.Online, service.State);
    }

    [Fact]
    public async Task OverlappingPollIsSkipped()
    {
        var service = CreateService();
        var gate = new TaskCompletionSource<ShellyReading>();
        _client.Pending = gate.Task;

        var first = service.PollOnceAsync();
        Assert.False(await service.PollOnceAsync());

        gate.SetResult(new ShellyReading(100, 230, 0.4));
        Assert.True(await first);
        Assert.Equal(1, _client.Calls);
    }

    [Fact]
    public async Task LoopPollsImmediatelyThenEachIntervalAndStopGoesUnknown()
    {
        var time = new FakeTimeProvider();
        var service = CreateService(time);
        var polls = 0;
        var signal = new SemaphoreSlim(0);
        service.ReadingReceived += (_, _) =>
        {
            Interlocked.Increment(ref polls);
            signal.Release();
        };
        for (var i = 0; i < 5; i++)
        {
            _client.Enqueue(new ShellyReading(100, 230, 0.4));
        }

        service.Start();
        Assert.True(await signal.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, polls);

        time.Advance(TimeSpan.FromMilliseconds(1000));
        Assert.True(await signal.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(2, polls);

        await service.StopAsync();
        Assert.False(service.IsRunning);
        Assert.Equal(OnlineState.Unknown, service.State);
    }

    [Fact]
    public async Task StartDelayStaggersTheFirstPollAndKeepsTheInterval()
    {
        var time = new FakeTimeProvider();
        var service = CreateService(time, startDelay: TimeSpan.FromMilliseconds(500));
        var signal = new SemaphoreSlim(0);
        var at = new List<TimeSpan>();
        var start = time.GetUtcNow();
        service.ReadingReceived += (_, _) =>
        {
            lock (at)
            {
                at.Add(time.GetUtcNow() - start);
            }

            signal.Release();
        };
        for (var i = 0; i < 5; i++)
        {
            _client.Enqueue(new ShellyReading(100, 230, 0.4));
        }

        service.Start();
        await WaitForTimersAsync(time);
        Assert.False(await signal.WaitAsync(TimeSpan.FromMilliseconds(100)));

        time.Advance(TimeSpan.FromMilliseconds(500));
        Assert.True(await signal.WaitAsync(TimeSpan.FromSeconds(5)));
        await WaitForTimersAsync(time);
        time.Advance(TimeSpan.FromMilliseconds(1000));
        Assert.True(await signal.WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.Equal([TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(1500)], at);
        await service.StopAsync();
    }

    [Fact]
    public async Task OneDeviceFailingNeverAffectsAnother()
    {
        var time = new FakeTimeProvider();
        var good = new FakeShellyClient();
        var bad = new FakeShellyClient { Hang = true };
        var pc = CreateService(time, good, deviceId: "pc");
        var monitor = CreateService(time, bad, withCurve: false, deviceId: "monitor", startDelay: TimeSpan.FromMilliseconds(500));
        var signal = new SemaphoreSlim(0);
        pc.ReadingReceived += (_, _) => signal.Release();
        for (var i = 0; i < 10; i++)
        {
            good.Enqueue(new ShellyReading(100, 230, 0.4));
        }

        pc.Start();
        monitor.Start();
        Assert.True(await signal.WaitAsync(TimeSpan.FromSeconds(5)));
        for (var tick = 0; tick < 4; tick++)
        {
            await WaitForTimersAsync(time);
            time.Advance(TimeSpan.FromMilliseconds(500));
            await WaitForTimersAsync(time);
            time.Advance(TimeSpan.FromMilliseconds(500));
            Assert.True(await signal.WaitAsync(TimeSpan.FromSeconds(5)));
        }

        Assert.Equal(OnlineState.Online, pc.State);
        Assert.Equal(0, pc.ConsecutiveFailures);
        Assert.Equal(5, good.Calls);
        Assert.True(bad.Calls >= 1);
        await pc.StopAsync();
        await monitor.StopAsync();
    }

    [Fact]
    public void OptionsAreNormalized()
    {
        Assert.Equal(new PollingOptions { IntervalMs = 500, TimeoutMs = 400 }, new PollingOptions { IntervalMs = 100, TimeoutMs = 800 }.Normalize());
        Assert.Equal(new PollingOptions { IntervalMs = 5000, TimeoutMs = 800 }, new PollingOptions { IntervalMs = 9000 }.Normalize());
        Assert.Equal(new PollingOptions { IntervalMs = 1000, TimeoutMs = 800 }, new PollingOptions().Normalize());
    }

    /// <summary>Lets the background loops reach their next timer wait before the fake clock moves.</summary>
    private static async Task WaitForTimersAsync(FakeTimeProvider time)
    {
        _ = time;
        await Task.Delay(50);
    }

    internal sealed class FakeShellyClient : IShellyClient
    {
        private readonly Queue<ShellyReading?> _results = new();
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public bool Hang { get; set; }

        public Task<ShellyReading>? Pending { get; set; }

        public void Enqueue(ShellyReading reading)
        {
            lock (_results)
            {
                _results.Enqueue(reading);
            }
        }

        public void EnqueueFailure()
        {
            lock (_results)
            {
                _results.Enqueue(null);
            }
        }

        public async Task<ShellyReading> GetReadingAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            if (Hang)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            if (Pending is not null)
            {
                return await Pending;
            }

            ShellyReading? next;
            lock (_results)
            {
                next = _results.Count > 0 ? _results.Dequeue() : null;
            }

            return next ?? throw new ShellyException("unreachable");
        }

        public Task<ShellyDeviceInfo> GetDeviceInfoAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ShellyDeviceInfo("Fake plug", "TEST-1", "Test", 3, "shellytest-000000"));
    }
}
