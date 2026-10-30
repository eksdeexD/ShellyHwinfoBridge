using Microsoft.Extensions.Time.Testing;
using ShellyHwinfoBridge.Core.Curves;
using ShellyHwinfoBridge.Core.Hwinfo;
using ShellyHwinfoBridge.Core.Polling;
using ShellyHwinfoBridge.Core.Shelly;
using ShellyHwinfoBridge.Core.Tests.TestSupport;

namespace ShellyHwinfoBridge.Core.Tests.Polling;

public class PollingServiceTests
{
    private readonly FakeShellyClient _client = new();
    private readonly RecordingRegistry _registry = new();

    private PollingService CreateService(TimeProvider? time = null) => new(
        _client,
        _registry,
        new PowerCalculator(PsuCurveSet.Single(TestCurves.Simple)),
        new PollingOptions(),
        timeProvider: time);

    [Fact]
    public async Task SuccessfulPollWritesReadingsAndGoesOnline()
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
        Assert.Equal((150, 130, 20, true), _registry.Last);
        Assert.Equal(130, received!.Power.DcWatts, 9);
        Assert.Equal(230, received.Raw.Volts);
    }

    [Fact]
    public async Task GoesOfflineAfterThreeFailuresAndRecovers()
    {
        var service = CreateService();
        var transitions = new List<(OnlineState From, OnlineState To)>();
        service.OnlineStateChanged += (_, e) => transitions.Add((e.Previous, e.Current));

        _client.Enqueue(new ShellyReading(150, 230, 0.6));
        await service.PollOnceAsync();

        _client.EnqueueFailure();
        _client.EnqueueFailure();
        await service.PollOnceAsync();
        await service.PollOnceAsync();
        Assert.Equal(OnlineState.Online, service.State);
        Assert.Equal(2, service.ConsecutiveFailures);
        Assert.Equal((150, 130, 20, true), _registry.Last);

        _client.EnqueueFailure();
        await service.PollOnceAsync();
        Assert.Equal(OnlineState.Offline, service.State);
        Assert.Equal((0, 0, 0, false), _registry.Last);

        // Keeps retrying while offline, without repeating the transition.
        _client.EnqueueFailure();
        await service.PollOnceAsync();
        Assert.Equal(4, service.ConsecutiveFailures);

        _client.Enqueue(new ShellyReading(200, 230, 0.8));
        await service.PollOnceAsync();
        Assert.Equal(OnlineState.Online, service.State);
        Assert.Equal(0, service.ConsecutiveFailures);
        Assert.Equal((200, 180, 20, true), _registry.Last);

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
        Assert.Equal((0, 0, 0, false), _registry.Last);
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
    public async Task PausedServiceDoesNotPollAndSetsOffline()
    {
        var service = CreateService();
        _client.Enqueue(new ShellyReading(150, 230, 0.6));
        await service.PollOnceAsync();

        service.Pause();
        Assert.Equal((0, 0, 0, false), _registry.Last);
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
    public async Task LoopPollsImmediatelyThenEachIntervalAndStopSetsOffline()
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
        Assert.Equal((0, 0, 0, false), _registry.Last);
    }

    [Fact]
    public void OptionsAreNormalized()
    {
        Assert.Equal(new PollingOptions { IntervalMs = 500, TimeoutMs = 400 }, new PollingOptions { IntervalMs = 100, TimeoutMs = 800 }.Normalize());
        Assert.Equal(new PollingOptions { IntervalMs = 5000, TimeoutMs = 800 }, new PollingOptions { IntervalMs = 9000 }.Normalize());
        Assert.Equal(new PollingOptions { IntervalMs = 1000, TimeoutMs = 800 }, new PollingOptions().Normalize());
    }

    private sealed class FakeShellyClient : IShellyClient
    {
        private readonly Queue<ShellyReading?> _results = new();

        public int Calls { get; private set; }

        public bool Hang { get; set; }

        public Task<ShellyReading>? Pending { get; set; }

        public void Enqueue(ShellyReading reading) => _results.Enqueue(reading);

        public void EnqueueFailure() => _results.Enqueue(null);

        public async Task<ShellyReading> GetReadingAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Hang)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            if (Pending is not null)
            {
                return await Pending;
            }

            return _results.Dequeue() ?? throw new ShellyException("unreachable");
        }

        public Task<ShellyDeviceInfo> GetDeviceInfoAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ShellyDeviceInfo("Fake plug", "TEST-1", "Test", 3, "shellytest-000000"));
    }

    private sealed class RecordingRegistry : IHwinfoSensorRegistry
    {
        public (double Ac, double Dc, double Loss, bool Online) Last { get; private set; }

        public bool EnsureKeys() => false;

        public void WriteReadings(double acWatts, double dcWatts, double lossWatts, bool online) =>
            Last = (Math.Round(acWatts, 6), Math.Round(dcWatts, 6), Math.Round(lossWatts, 6), online);

        public void WriteFormula(string formula)
        {
        }

        public void SetOffline() => WriteReadings(0, 0, 0, false);

        public void RemoveAll()
        {
        }
    }
}
