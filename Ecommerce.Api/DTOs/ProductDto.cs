using System.ComponentModel.DataAnnotations;

public class ProductCreateDto
{
    [Required]
    [MinLength(2)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [Range(0.0, double.MaxValue)]
    public decimal Price { get; set; }
}

public class ProductResponseDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
}
