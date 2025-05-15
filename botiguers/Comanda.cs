namespace botiguers;

public class Comandes
{
    public List<long> id { get; set; } = new();
    public List<ProductesComanda> productesComanda { get; set; } = new();
    public List<ProductesACambiar> cambiar { get; set; } = new();
}