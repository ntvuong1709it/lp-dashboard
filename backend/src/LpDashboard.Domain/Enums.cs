namespace LpDashboard.Domain;

public enum Chain
{
    Base,
    Ethereum,
    Solana,
    Robinhood
}

public enum Protocol
{
    UniswapV3,
    AerodromeSlipstream,
    UniswapV4,
    OrcaWhirlpool,
    RaydiumClmm,
    MeteoraDlmm
}

public enum ChainFamily
{
    Evm,
    Solana
}

public enum PositionStatus
{
    Open,
    Closed
}

public enum PositionEventType
{
    IncreaseLiquidity,
    DecreaseLiquidity,
    Collect,
    RewardClaim
}
