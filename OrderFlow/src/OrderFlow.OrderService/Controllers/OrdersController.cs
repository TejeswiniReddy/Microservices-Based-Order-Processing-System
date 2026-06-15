using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderFlow.Contracts.Abstractions;
using OrderFlow.Contracts.Dtos;
using OrderFlow.Contracts.Events;
using OrderFlow.Contracts.Models;
using OrderFlow.Platform.Security;

namespace OrderFlow.OrderService.Controllers;

[ApiController]
[Route("orders")]
[Authorize(Policy = "Customers")]
public sealed class OrdersController : ControllerBase
{
    private readonly IOrderRepository _orders;
    private readonly IEventBus _bus;

    public OrdersController(IOrderRepository orders, IEventBus bus)
    {
        _orders = orders;
        _bus = bus;
    }

    private string CustomerId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";
    private bool IsAdmin => User.IsInRole(Roles.Admin);

    /// <summary>Place an order. Kicks off the distributed saga via OrderCreated.</summary>
    [HttpPost]
    public async Task<ActionResult<OrderResponse>> Create([FromBody] CreateOrderRequest request, CancellationToken ct)
    {
        if (request.Items is null || request.Items.Count == 0)
            return BadRequest(new { error = "order_requires_items" });

        var order = new Order
        {
            CustomerId = CustomerId,
            Items = request.Items.Select(i => new OrderItem
            {
                Sku = i.Sku,
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice
            }).ToList(),
            Status = OrderStatus.PaymentProcessing
        };

        await _orders.AddAsync(order, ct);

        await _bus.PublishAsync(new OrderCreated
        {
            OrderId = order.Id,
            CustomerId = order.CustomerId,
            TotalAmount = order.TotalAmount,
            Items = order.Items
        }, ct);

        // 202: the saga is now running asynchronously; clients poll GET /orders/{id}.
        return AcceptedAtAction(nameof(GetById), new { id = order.Id }, ToResponse(order));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OrderResponse>> GetById(Guid id, CancellationToken ct)
    {
        var order = await _orders.GetAsync(id, ct);
        if (order is null) return NotFound();
        if (order.CustomerId != CustomerId && !IsAdmin) return Forbid();
        return ToResponse(order);
    }

    /// <summary>Orders belonging to the calling customer.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<OrderResponse>>> Mine(CancellationToken ct)
    {
        var list = await _orders.ListByCustomerAsync(CustomerId, ct);
        return Ok(list.Select(ToResponse));
    }

    private static OrderResponse ToResponse(Order o) => new(
        o.Id, o.CustomerId, o.Status.ToString(), o.StatusReason,
        o.TotalAmount, o.Items, o.CreatedAt, o.UpdatedAt);
}

[ApiController]
[Route("admin/orders")]
[Authorize(Policy = "AdminOnly")]
public sealed class AdminOrdersController : ControllerBase
{
    private readonly IOrderRepository _orders;
    public AdminOrdersController(IOrderRepository orders) => _orders = orders;

    /// <summary>Admin-only: every order in the system.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<OrderResponse>>> All(CancellationToken ct)
    {
        var list = await _orders.ListAsync(ct);
        return Ok(list.Select(o => new OrderResponse(
            o.Id, o.CustomerId, o.Status.ToString(), o.StatusReason,
            o.TotalAmount, o.Items, o.CreatedAt, o.UpdatedAt)));
    }
}
