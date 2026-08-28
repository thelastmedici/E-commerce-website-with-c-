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
