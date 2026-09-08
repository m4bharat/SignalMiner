using SignalMiner.Application;
using SignalMiner.Domain;
namespace SignalMiner.Tests;

internal sealed class TestSafety : IOutreachSafety
{
    public int Submitted;
    public int Begun;
    public Task<OutreachCapacity> CapacityAsync(CancellationToken c) => Task.FromResult(new OutreachCapacity(50,10,50-Submitted));
    public Task<IOutreachSendLease> BeginAsync(Guid id, IReadOnlyList<string> emails, CancellationToken c) { Begun++; return Task.FromResult<IOutreachSendLease>(new Lease(this)); }
    private sealed class Lease(TestSafety owner) : IOutreachSendLease
    {
        public Task SubmittedAsync(EmailDeliveryResult result) { owner.Submitted++; return Task.CompletedTask; }
        public Task UncertainAsync(string details) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
internal sealed class TestSuppressions : ISuppressionService
{
    public Dictionary<string,EmailSuppression> Records = new();
    public Task<IReadOnlyDictionary<string,EmailSuppression>> FindAsync(IEnumerable<string> emails,CancellationToken c) => Task.FromResult<IReadOnlyDictionary<string,EmailSuppression>>(Records);
    public Task<EmailSuppression?> RecordAsync(Guid id, SuppressionRequest r,CancellationToken c) => throw new NotSupportedException();
    public Task<SuppressionSearchResult> SearchAsync(string? q,int p,int s,CancellationToken c)=>throw new NotSupportedException();
}
