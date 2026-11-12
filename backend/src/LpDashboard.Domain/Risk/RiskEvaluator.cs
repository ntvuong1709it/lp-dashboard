using System.Globalization;
using LpDashboard.Domain.Metrics;

namespace LpDashboard.Domain.Risk;

public sealed record TokenRiskAssessment(TokenRiskFacts Facts, RiskVerdict Verdict, IReadOnlyList<RiskFinding> Findings, bool Trusted);

/// <param name="Verdict">Worst of the pool's own findings and both token verdicts.</param>
/// <param name="OwnVerdict">The pool's own findings and sources only.</param>
/// <param name="Findings">The pool's own findings. The tokens' findings live on their own assessments.</param>
public sealed record PoolRiskAssessment(
    PoolRiskFacts Facts,
    RiskVerdict Verdict,
    RiskVerdict OwnVerdict,
    IReadOnlyList<RiskFinding> Findings,
    RiskVerdict? Token0Verdict,
    RiskVerdict? Token1Verdict);

/// <summary>
/// Turns collected facts into findings and a verdict. Pure: no I/O, no clock (the facts carry their own collection
/// time), so every rule is a unit test. The rules are the table in docs/risk-screen-spec.md section 5.
/// </summary>
public static class RiskEvaluator
{
    public static TokenRiskAssessment EvaluateToken(TokenRiskFacts f, RiskOptions o)
    {
        var findings = new List<RiskFinding>();
        var trusted = o.IsTrusted(f.TokenAddress);
        var gp = f.GoPlus;
        var hp = f.HoneypotIs;

        // HONEYPOT: any source says it cannot be sold, or the sell tax is confiscatory.
        var honeypot = new List<string>();
        if (hp?.IsHoneypot == true) honeypot.Add($"honeypot.is flags it as a honeypot{Reason(hp.Reason)}");
        if (hp?.SimulationFailed == true) honeypot.Add("honeypot.is: the simulated sell failed");
        if (gp?.IsHoneypot == true) honeypot.Add("GoPlus flags it as a honeypot");
        var sell = Max(("GoPlus", gp?.SellTaxPct), ("honeypot.is", hp?.SellTaxPct));
        if (sell is { } hs && hs.Value > o.SellTaxHoneypotPct)
            honeypot.Add($"sell tax {Pct(hs.Value)} ({hs.Source}), above {Pct(o.SellTaxHoneypotPct)}");
        if (honeypot.Count > 0)
            findings.Add(new(RiskCodes.Honeypot, RiskSeverity.Red, "Honeypot or failing sell", string.Join("; ", honeypot),
                Names(f, RiskSources.GoPlus, RiskSources.HoneypotIs)));

        // SELL_TAX / BUY_TAX: noticeable, not confiscatory.
        if (sell is { } s && s.Value >= o.TaxWarnPct && s.Value <= o.SellTaxHoneypotPct)
            findings.Add(new(RiskCodes.SellTax, RiskSeverity.Yellow, "Sell tax", $"{Pct(s.Value)} ({s.Source})", s.Source));
        var buy = Max(("GoPlus", gp?.BuyTaxPct), ("honeypot.is", hp?.BuyTaxPct));
        if (buy is { } b && b.Value >= o.TaxWarnPct)
            findings.Add(new(RiskCodes.BuyTax, RiskSeverity.Yellow, "Buy tax", $"{Pct(b.Value)} ({b.Source})", b.Source));

        // PRIVILEGED_MINT_OR_BLACKLIST: someone can mint or freeze the token. Trusted tokens are exempt by config.
        var controller = RiskFactsAnalysis.Controller(f);
        var privileged = false;
        if (!trusted)
        {
            var powers = new List<string>();
            if (gp?.CanMint == true) powers.Add("mint");
            if (gp?.CanBlacklist == true) powers.Add("blacklist");
            if (gp?.CanPauseTransfers == true) powers.Add("pause transfers");
            if (powers.Count > 0)
            {
                var what = string.Join(" and ", powers);
                if (controller is not null)
                {
                    var role = controller.IsProxyAdmin ? "proxy admin" : "owner";
                    var (sev, who) = controller.IsContract switch
                    {
                        false => (RiskSeverity.Red, "an externally owned account (one key)"),
                        true => (RiskSeverity.Yellow, "a contract (assumed multisig or timelock)"),
                        null => (RiskSeverity.Yellow, "an address that could not be classified"),
                    };
                    findings.Add(new(RiskCodes.PrivilegedMintOrBlacklist, sev, "Owner can mint or blacklist",
                        $"Can {what}; {role} {controller.Address} is {who}", Names(f, RiskSources.GoPlus, RiskSources.Rpc), controller.Address));
                    privileged = true;
                }
                else if (!RiskFactsAnalysis.OwnerRenounced(f))
                {
                    // Powers exist but nobody is named: role-based access, or the node could not be asked.
                    findings.Add(new(RiskCodes.PrivilegedMintOrBlacklist, RiskSeverity.Yellow, "Owner can mint or blacklist",
                        $"Can {what}; the controlling address could not be identified (role-based access, or the on-chain read failed)",
                        Names(f, RiskSources.GoPlus, RiskSources.Rpc)));
                    privileged = true;
                }
                // else renounced: the powers cannot be used any more, no finding.
            }
        }

        // TOP_HOLDER_CONCENTRATION (Red) and TOP10_CONCENTRATION (Yellow), pools and locked holders excluded.
        var top = RiskFactsAnalysis.TopHolder(f);
        var creator = RiskFactsAnalysis.CreatorShare(f);
        var concentration = new List<string>();
        var concentrationRed = false;
        if (top is not null && top.SharePct > o.TopHolderRedPct)
        {
            // GoPlus does not tag protocol contracts (a vote escrow, a lending vault), so on a token from the trusted list a
            // contract holder is far more likely a protocol than a person: Yellow, with the address in the evidence.
            var protocolLike = trusted && top.IsContract;
            concentration.Add($"largest holder {top.Address} has {Pct(top.SharePct)} of supply (limit {Pct(o.TopHolderRedPct)}){TagOf(top)}"
                + (protocolLike ? " and is a contract (trusted token: probably a protocol vault or escrow)" : ""));
            concentrationRed |= !protocolLike;
        }
        if (creator is { } c && c > o.CreatorRedPct)
        {
            concentration.Add($"creator {gp!.CreatorAddress} has {Pct(c)} of supply (limit {Pct(o.CreatorRedPct)})");
            concentrationRed = true;
        }
        if (concentration.Count > 0)
            findings.Add(new(RiskCodes.TopHolderConcentration, concentrationRed ? RiskSeverity.Red : RiskSeverity.Yellow,
                concentrationRed ? "One wallet holds most of the supply" : "A contract holds most of the supply",
                string.Join("; ", concentration), "GoPlus", top?.Address ?? gp?.CreatorAddress));
        if (RiskFactsAnalysis.Top10Share(f) is { } top10 && top10 > o.Top10YellowPct)
            findings.Add(new(RiskCodes.Top10Concentration, RiskSeverity.Yellow, "Top 10 holders hold most of the supply",
                $"{Pct(top10)} of supply (limit {Pct(o.Top10YellowPct)}), pools and locked holders excluded", "GoPlus"));

        // UNVERIFIED_SOURCE: Basescan wins over GoPlus when both answered.
        if (f.Contract?.SourceVerified is { } verified)
        {
            if (!verified) findings.Add(new(RiskCodes.UnverifiedSource, RiskSeverity.Yellow, "Contract source not verified", "Basescan reports no verified source", "Basescan"));
        }
        else if (gp?.IsOpenSource == false)
        {
            findings.Add(new(RiskCodes.UnverifiedSource, RiskSeverity.Yellow, "Contract source not verified", "GoPlus reports the contract is not open source", "GoPlus"));
        }

        // UPGRADEABLE_PROXY: the code behind the token can be swapped. Exempt for trusted tokens like the privilege rules.
        var implementation = RiskFactsAnalysis.Implementation(f);
        if (!trusted)
        {
            if (implementation is not null)
            {
                var admin = f.OnChain?.ProxyAdmin;
                var adminText = RiskAddresses.IsZero(admin)
                    ? "no admin slot set"
                    : $"admin {admin} is {(f.OnChain!.ProxyAdminIsContract switch { true => "a contract", false => "an externally owned account", null => "of unknown type" })}";
                findings.Add(new(RiskCodes.UpgradeableProxy, RiskSeverity.Yellow, "Upgradeable proxy",
                    $"Implementation {implementation}; {adminText}", "on-chain (RPC)", implementation));
            }
            else if (f.OnChain is null && gp?.IsProxy == true)
            {
                findings.Add(new(RiskCodes.UpgradeableProxy, RiskSeverity.Yellow, "Upgradeable proxy", "GoPlus reports a proxy contract (implementation not read on-chain)", "GoPlus"));
            }
        }

        // OWNER_NOT_RENOUNCED: only worth a line when nothing above already speaks about the owner.
        if (!trusted && !privileged && controller is { IsProxyAdmin: false })
            findings.Add(new(RiskCodes.OwnerNotRenounced, RiskSeverity.Info, "Owner not renounced",
                $"Owner {controller.Address} still exists, but the token has no mint, blacklist or pause function", "on-chain (RPC)", controller.Address));

        // Market facts.
        var young = new List<string>();
        if (f.Contract?.CreatedAt is { } created && Age(f, created) < o.YoungDays)
            young.Add($"token contract created {AgeText(f, created)} ago (Basescan)");
        if (f.Market?.FirstPoolCreatedAt is { } pool && Age(f, pool) < o.YoungDays)
            young.Add($"first pool created {AgeText(f, pool)} ago (DexScreener)");
        if (young.Count > 0)
            findings.Add(new(RiskCodes.TokenYoung, RiskSeverity.Yellow, "Token is very new", string.Join("; ", young), Names(f, RiskSources.DexScreener, RiskSources.Etherscan)));

        if (f.Market?.LiquidityUsd is { } liquidity)
        {
            if (liquidity < o.MinLiquidityUsd)
                findings.Add(new(RiskCodes.ThinLiquidity, RiskSeverity.Yellow, "Thin liquidity", $"{Usd(liquidity)} across the token's pools (minimum {Usd(o.MinLiquidityUsd)})", "DexScreener"));
            if (liquidity > 0 && f.Market.Volume24hUsd is { } volume && f.Market.Trades24h is { } trades
                && volume > liquidity * o.WashVolumeToLiquidity && trades < o.WashMaxTrades)
                findings.Add(new(RiskCodes.WashSuspect, RiskSeverity.Yellow, "Volume looks artificial",
                    $"24 h volume {Usd(volume)} is {(volume / liquidity).ToString("0.#", CultureInfo.InvariantCulture)}x liquidity with only {trades} trades (heuristic: DexScreener has no unique-trader count)", "DexScreener"));
        }

        if (f.Prices?.HasHistory == false)
            findings.Add(new(RiskCodes.PriceHistoryMissing, RiskSeverity.Yellow, "No price history", "DefiLlama has no historical price for this token, so P&L and time in range are estimates", "DefiLlama"));
        if (f.Prices?.PriceUsd is { } price && AssetClassifier.Classify(f.Symbol) == AssetClass.Stablecoin)
        {
            var off = Math.Abs(price - 1m) * 100m;
            if (off > o.StableDepegPct)
                findings.Add(new(RiskCodes.StableDepeg, RiskSeverity.Yellow, "Stablecoin off its peg", $"{f.Symbol} is priced at {price.ToString("0.####", CultureInfo.InvariantCulture)} USD ({Pct(off)} from $1)", "DefiLlama"));
        }

        if (trusted)
            findings.Add(new(RiskCodes.TrustedToken, RiskSeverity.Info, "Trusted token (config list)", "Exempt from the owner, mint, blacklist and proxy rules; honeypot and concentration checks still ran", "config"));

        AddSourceUnavailable(findings, f.Sources);

        return new TokenRiskAssessment(f, Verdict(findings, f.Sources), findings, trusted);
    }

    public static PoolRiskAssessment EvaluatePool(
        PoolRiskFacts f, TokenRiskAssessment? token0, TokenRiskAssessment? token1, RiskOptions o)
    {
        var findings = new List<RiskFinding>();

        if (f.PoolCreatedAt is { } created)
        {
            var age = (f.CollectedAt - created).TotalDays;
            if (age < o.YoungDays)
                findings.Add(new(RiskCodes.PoolYoung, RiskSeverity.Yellow, "Pool is very new",
                    $"created {AgeText(f.CollectedAt, created)} ago (limit {o.YoungDays} days)", "DexScreener"));
        }

        if (f.FeeAprPct is { } fee && f.RewardAprPct is { } reward && fee + reward > 0)
        {
            var share = reward / (fee + reward) * 100m;
            if (share > o.EmissionDependentPct)
                findings.Add(new(RiskCodes.EmissionDependent, RiskSeverity.Yellow, "APR depends on emissions",
                    $"{Pct(share)} of the APR is gauge emissions (fees {Pct(fee)}, emissions {Pct(reward)}); it ends when the emissions do", "pool explorer"));
        }

        if (f.GaugeAlive == false)
            findings.Add(new(RiskCodes.GaugeKilled, RiskSeverity.Yellow, "Gauge is not alive", "The voter reports this pool's gauge as killed, so it earns no emissions", "on-chain (RPC)"));

        AddSourceUnavailable(findings, f.Sources);

        var own = Verdict(findings, f.Sources);
        var parts = new List<RiskVerdict> { own };
        parts.Add(token0?.Verdict ?? RiskVerdict.NotChecked);
        parts.Add(token1?.Verdict ?? RiskVerdict.NotChecked);
        return new PoolRiskAssessment(f, RiskVerdict.Combine([.. parts]), own, findings, token0?.Verdict, token1?.Verdict);
    }

    /// <summary>
    /// Red if any Red finding, Yellow if any Yellow. Otherwise Green only when every required source answered; Partial when
    /// one did not, and NotChecked when nothing answered at all. "No flags" is never "safe", but it is the best Green can say.
    /// </summary>
    public static RiskVerdict Verdict(IReadOnlyList<RiskFinding> findings, IReadOnlyList<SourceResult> sources)
    {
        var run = sources.Count(s => s.Answered);
        var total = sources.Count;
        if (findings.Any(x => x.Severity == RiskSeverity.Red)) return new(RiskLevel.Red, run, total);
        if (findings.Any(x => x.Severity == RiskSeverity.Yellow)) return new(RiskLevel.Yellow, run, total);
        if (run == 0) return new(RiskLevel.NotChecked, run, total);
        if (sources.Any(s => s.Required && !s.Answered)) return new(RiskLevel.Partial, run, total);
        return new(RiskLevel.Green, run, total);
    }

    private static void AddSourceUnavailable(List<RiskFinding> findings, IReadOnlyList<SourceResult> sources)
    {
        var failed = sources.Where(s => !s.Answered).ToList();
        if (failed.Count == 0) return;
        var evidence = string.Join(", ", failed.Select(s =>
            RiskSources.DisplayName(s.Name) + (string.IsNullOrWhiteSpace(s.Detail) ? "" : $" ({s.Detail})")));
        findings.Add(new(RiskCodes.SourceUnavailable, RiskSeverity.Info, "Some checks did not run",
            $"{sources.Count - failed.Count} of {sources.Count} checks ran. Did not run: {evidence}", "app"));
    }

    private static (string Source, decimal Value)? Max(params (string Source, decimal? Value)[] values)
    {
        (string Source, decimal Value)? best = null;
        foreach (var (source, value) in values)
            if (value is { } v && (best is null || v > best.Value.Value)) best = (source, v);
        return best;
    }

    private static string Names(TokenRiskFacts f, params string[] sources) =>
        string.Join(", ", sources.Where(f.Answered).Select(RiskSources.DisplayName)) is { Length: > 0 } names ? names : RiskSources.DisplayName(sources[0]);

    private static double Age(TokenRiskFacts f, DateTime at) => (f.CollectedAt - at).TotalDays;

    private static string AgeText(TokenRiskFacts f, DateTime at) => AgeText(f.CollectedAt, at);

    private static string AgeText(DateTime now, DateTime at)
    {
        var span = now - at;
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        return span.TotalDays >= 1 ? $"{span.TotalDays.ToString("0.#", CultureInfo.InvariantCulture)} days" : $"{span.TotalHours.ToString("0.#", CultureInfo.InvariantCulture)} hours";
    }

    private static string Reason(string? reason) => string.IsNullOrWhiteSpace(reason) ? "" : $" ({reason})";

    private static string TagOf(HolderFact h) => string.IsNullOrWhiteSpace(h.Tag) ? "" : $" [{h.Tag}]";

    private static string Pct(decimal v) => v.ToString("0.##", CultureInfo.InvariantCulture) + "%";

    private static string Usd(decimal v) => "$" + v.ToString("#,0", CultureInfo.InvariantCulture);
}
