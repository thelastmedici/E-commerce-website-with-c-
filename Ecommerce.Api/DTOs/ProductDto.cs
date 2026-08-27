using System.ComponentModel.DataAnnotations;

public class ProductCreateDto
{
    [Required]
    [StringLength(200, MinimumLength = 2)]
    [RegularExpression(@".*\S.*", ErrorMessage = "Name must contain non-whitespace characters.")]
    public string Name { get; set; } = string.Empty;

    [Required]
    [Range(typeof(decimal), "0.01", "79228162514264337593543950335")]
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
