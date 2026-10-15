using MatrixMultiply.Core.Api;

namespace MatrixMultiply.Tests;

public class AdaptiveGateTests
{
    [Fact]
    public async Task HalvesTheLimitWhenARequestIsRejected()
    {
        var gate = new AdaptiveGate(8, 32);
        await gate.EnterAsync(CancellationToken.None);

        gate.ExitTooManyRequests();

        Assert.Equal(4, gate.Limit);
    }

    [Fact]
    public async Task RaisesTheLimitAfterASuccessWindow()
    {
        var gate = new AdaptiveGate(2, 10);

        for (var i = 0; i < AdaptiveGate.SuccessWindow; i++)
        {
            await gate.EnterAsync(CancellationToken.None);
            gate.ExitSuccess();
        }

        Assert.Equal(2 + AdaptiveGate.IncreaseStep, gate.Limit);
    }

    [Fact]
    public async Task DoesNotExceedTheCeiling()
    {
        var gate = new AdaptiveGate(8, 10);

        for (var i = 0; i < AdaptiveGate.SuccessWindow; i++)
        {
            await gate.EnterAsync(CancellationToken.None);
            gate.ExitSuccess();
        }

        Assert.Equal(10, gate.Limit);
    }

    [Fact]
    public async Task BlocksUntilASlotIsReleased()
    {
        var gate = new AdaptiveGate(1, 4);
        await gate.EnterAsync(CancellationToken.None);
        var waiting = gate.EnterAsync(CancellationToken.None);
        await Task.Delay(50);
        Assert.False(waiting.IsCompleted);

        gate.ExitSuccess();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await waiting.WaitAsync(timeout.Token);
        gate.ExitSuccess();
    }
}
