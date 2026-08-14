using System.Collections.Generic;

public record OrderItemCreateDto(int ProductId, int Quantity);

public record OrderCreateDto(int UserId, List<OrderItemCreateDto> Items);
