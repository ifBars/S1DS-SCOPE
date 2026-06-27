---
title: Wallets and Properties
description: Scoped online balance, ATM deposits, purchases, and property access.
---

# Wallets and Properties

Organisations scopes money and property through the same owner-key model used by deals and quests.

## Wallets

Each solo player or organisation has a scoped online balance. Client finance patches hydrate the visible online balance from the latest server snapshot, and server-side transactions write back to the scoped wallet.

ATM behavior is also scoped:

- Deposits add cash to the current owner key's online balance.
- Withdrawals remove money from the current owner key's online balance.
- Weekly deposit totals are tracked per owner key.
- Weekly totals reset when the game's ATM week pass runs.

The config key `banking.weeklyAtmDepositLimit` controls the maximum weekly ATM deposit for a solo player or organisation.

The same weekly pass can also process optional dealer retention fees when `dealers.enableDealerRetentionFees` is enabled. Those fees come from scoped dealer cash, not directly from the player or organisation wallet.

## Property

Properties are reserved by owner key. If a solo player buys property, it belongs to `player:<steamId>`. If an organisation member buys property, it belongs to `org:<organisationId>`.

Only the owning solo player or organisation can access a reserved property. When a solo player joins an organisation, their personal property reservations are moved to the organisation owner key.

## Purchase Rules

The client validates scoped online funds before purchases, and the server applies the authoritative scoped transaction. That keeps base-game purchase screens usable while preventing a player from spending another owner key's balance.
