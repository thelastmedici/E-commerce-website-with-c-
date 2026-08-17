using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

public class OrderItemCreateDto
{
	[Required]
	[Range(1, int.MaxValue)]
	public int ProductId { get; set; }

	[Required]
	[Range(1, int.MaxValue)]
	public int Quantity { get; set; }
}

public class OrderCreateDto
{
	[Required]
	[Range(1, int.MaxValue)]
	public int UserId { get; set; }

	[Required]
	[MinLength(1)]
	public List<OrderItemCreateDto> Items { get; set; } = new();
}
