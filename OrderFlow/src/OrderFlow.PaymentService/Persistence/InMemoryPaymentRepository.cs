using System.Collections.Concurrent;
using OrderFlow.Contracts.Abstractions;

namespace OrderFlow.PaymentService.Persistence;

public sealed class InMemoryPaymentRepository : IPaymentRepository
{
    private sealed record Payment(Guid PaymentId, decimal Amount, bool Refunded);

    private readonly ConcurrentDictionary<Guid, Payment> _byOrder = new();

    public Task<Guid> RecordChargeAsync(Guid orderId, decimal amount, CancellationToken ct = default)
    {
        var paymentId = Guid.NewGuid();
        _byOrder[orderId] = new Payment(paymentId, amount, Refunded: false);
        return Task.FromResult(paymentId);
    }

    public Task<bool> RefundAsync(Guid orderId, CancellationToken ct = default)
    {
        if (_byOrder.TryGetValue(orderId, out var p) && !p.Refunded)
        {
            _byOrder[orderId] = p with { Refunded = true };
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    public Task<Guid?> GetPaymentIdAsync(Guid orderId, CancellationToken ct = default) =>
        Task.FromResult(_byOrder.TryGetValue(orderId, out var p) ? p.PaymentId : (Guid?)null);
}
