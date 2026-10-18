using OPFStrategyV1.Core.MarketData;

namespace OPFStrategyV1.Strategy;

internal sealed record OptionsGatePacket(string SnapshotId, string SignalId, string Product, string Path,
    DateTime DecisionUtc, bool IsLong, OpfCandle Evidence, decimal Risk,
    EsOptionsProxySnapshot Context, string? ExpectedReason)
{
    public string? Replay() => OptionsProxyEntryGate.Evaluate(Context, DecisionUtc, Path, IsLong,
        Evidence.Open, Evidence.High, Evidence.Low, Evidence.Close, Risk);
}

internal sealed record ReactionPacket(string SnapshotId, string SignalId, string Product, string Path,
    bool IsLong, decimal Price, decimal Trigger, decimal Stop, decimal Tick, DateTime Time, DateTime Expires,
    DateTime? HeldSince, decimal SignedVolume, decimal Volume, decimal MaxChase, string? ExpectedReason)
{
    public string? Replay() => IntradayEntryPolicy.Reaction(IsLong, Price, Trigger, Stop, Tick, Time, Expires,
        HeldSince, SignedVolume, Volume, MaxChase);
}
