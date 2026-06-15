using OrderFlow.Contracts.Models;

namespace OrderFlow.Contracts.Dtos;

public sealed record LoginRequest(string Username, string Password);
public sealed record TokenResponse(string AccessToken, string TokenType, int ExpiresInSeconds, string Role);

public sealed record CreateOrderItem(string Sku, int Quantity, decimal UnitPrice);
public sealed record CreateOrderRequest(List<CreateOrderItem> Items);

public sealed record OrderResponse(
    Guid Id,
    string CustomerId,
    string Status,
    string? StatusReason,
    decimal TotalAmount,
    List<OrderItem> Items,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
