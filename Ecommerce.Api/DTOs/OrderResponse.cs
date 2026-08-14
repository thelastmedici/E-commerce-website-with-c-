using System;
using System.Collections.Generic;

public record OrderItemResponseDto(int ProductId, string ProductName, int Quantity, decimal Price);

public record OrderResponseDto(int Id, int UserId, DateTime CreatedAt, decimal Total, List<OrderItemResponseDto> Items);
