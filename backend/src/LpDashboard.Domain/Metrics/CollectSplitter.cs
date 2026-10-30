using LpDashboard.Domain.Entities;

namespace LpDashboard.Domain.Metrics;

/// <summary>
/// Splits Collect amounts into principal vs fees. DecreaseLiquidity only credits tokensOwed;
/// the following Collect pays out principal + fees together, so principal owed is consumed first.
/// </summary>
public static class CollectSplitter
{
    public static (decimal Owed0, decimal Owed1) Apply(IEnumerable<PositionEvent> events)
    {
        decimal owed0 = 0, owed1 = 0;

        foreach (var e in Order(events))
        {
            switch (e.Type)
            {
                case PositionEventType.IncreaseLiquidity:
                    e.Principal0 = e.Amount0;
                    e.Principal1 = e.Amount1;
                    e.Fee0 = e.Fee1 = 0;
                    break;
                case PositionEventType.DecreaseLiquidity:
                    e.Principal0 = e.Amount0;
                    e.Principal1 = e.Amount1;
                    e.Fee0 = e.Fee1 = 0;
                    owed0 += e.Amount0;
                    owed1 += e.Amount1;
                    break;
                case PositionEventType.Collect:
                    var p0 = System.Math.Min(e.Amount0, owed0);
                    var p1 = System.Math.Min(e.Amount1, owed1);
                    owed0 -= p0;
                    owed1 -= p1;
                    e.Principal0 = p0;
                    e.Principal1 = p1;
                    e.Fee0 = e.Amount0 - p0;
                    e.Fee1 = e.Amount1 - p1;
                    break;
            }
        }

        return (owed0, owed1);
    }

    public static IEnumerable<PositionEvent> Order(IEnumerable<PositionEvent> events) =>
        events.OrderBy(e => e.BlockNumber).ThenBy(e => e.LogIndex);
}
