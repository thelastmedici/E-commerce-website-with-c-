using System;
using System.Collections.Generic;

public record OrderItemResponseDto(int ProductId, string ProductName, int Quantity, decimal Price);

public record OrderResponseDto(
    int Id,
    int UserId,
    DateTime CreatedAt,
    decimal Total,
    OrderStatus Status,
    DateTime? CancelledAt,
    DateTime? RefundedAt,
    List<OrderItemResponseDto> Items);

public class OrderStatusUpdateDto
{
    public OrderStatus Status { get; set; }
}
