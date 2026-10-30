namespace LpDashboard.Domain.Entities;

public class Token
{
    public int Id { get; set; }
    public Chain Chain { get; set; }
    public string Address { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public int Decimals { get; set; }
}
