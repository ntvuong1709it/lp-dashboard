-- Postgres design for LP Ledger. The scan-history tables (scan_cursors, nft_transfers, wallet_txs, receipts, position_events, block_times) are implemented by LpLedger.Infrastructure/Data/LedgerDb.cs and replace position_events and sync_state below; the snapshot and price tables are not wired yet.

create table wallets (
  address     text primary key,            -- lowercase 0x address
  label       text,
  created_at  timestamptz not null default now()
);

-- Immutable on-chain history, so each wallet is scanned once and then only new blocks are read.
create table position_events (
  wallet      text not null references wallets(address),
  protocol    text not null,               -- 'aerodrome'
  chain       text not null,               -- 'base'
  token_id    text not null,
  kind        text not null check (kind in ('add','remove','collect')),
  ts          timestamptz not null,
  block       bigint not null,
  tx_hash     text not null,
  log_index   int not null,
  amount0     numeric not null,
  amount1     numeric not null,
  usd         numeric not null,            -- valued at ts
  gas_usd     numeric,                     -- filled once gas tracking exists
  primary key (tx_hash, log_index)
);
create index on position_events (wallet, protocol, token_id, ts);

-- One row per wallet per day; feeds the portfolio history chart.
create table daily_snapshots (
  wallet        text not null references wallets(address),
  day           date not null,
  value_usd     numeric not null,
  unclaimed_usd numeric not null,
  net_pnl_usd   numeric not null,
  primary key (wallet, day)
);

create table position_snapshots (
  wallet      text not null references wallets(address),
  position_id text not null,
  day         date not null,
  value_usd   numeric not null,
  in_range    boolean not null,
  primary key (wallet, position_id, day)
);

create table price_cache (
  chain   text not null,
  token   text not null,
  hour_ts bigint not null,
  usd     numeric not null,
  primary key (chain, token, hour_ts)
);

-- Scan progress per wallet so refreshes read only new blocks.
create table sync_state (
  wallet      text not null references wallets(address),
  protocol    text not null,
  last_block  bigint not null,
  primary key (wallet, protocol)
);
