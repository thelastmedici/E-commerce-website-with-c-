using System.ComponentModel.DataAnnotations;

public class ProductCreateDto
{
    [Required]
    [StringLength(200, MinimumLength = 2)]
    [RegularExpression(@"^[\p{L}\p{N} \-'\.,()]+$", ErrorMessage = "Name contains invalid characters.")]
    public string Name { get; set; } = string.Empty;

    [Required]
    [Range(0.01, 1000000.00, ErrorMessage = "Price must be at least 0.01 and reasonably bounded.")]
    public decimal Price { get; set; }

    [Required]
    [Range(0, int.MaxValue)]
    public int Stock { get; set; } = 0;
}

public class ProductResponseDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Stock { get; set; }
}

public class ProductQueryDto
{
    [StringLength(100)]
    public string? Search { get; set; }

    [Range(typeof(decimal), "0", "79228162514264337593543950335")]
    public decimal? MinPrice { get; set; }

    [Range(typeof(decimal), "0", "79228162514264337593543950335")]
    public decimal? MaxPrice { get; set; }

    public bool? InStock { get; set; }

    [Range(1, 1000000)]
    public int Page { get; set; } = 1;

    [Range(1, 100)]
    public int PageSize { get; set; } = 20;
}

public record ProductListResponseDto(
    List<ProductResponseDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);
