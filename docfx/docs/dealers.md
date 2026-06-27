---
title: Dealers
description: Dealer recommendation, recruitment, ownership contention, assignments, inventory, cash, and completed deals.
---

# Dealers

Dealer state is scoped to prevent the base shared-co-op behavior where every player effectively uses the same recruited dealer.

## Recommendation and Recruitment

A dealer must be recommended in the current owner scope before recruitment. Recruitment then claims that dealer for the owner key.

If another owner key has already recruited the same dealer NPC, recruitment is rejected. This is true for:

- Solo player versus solo player.
- Solo player versus organisation.
- Organisation versus organisation.

Members of the same organisation share the same owner key, so they share the organisation's recruited dealer state.

## Assigning Customers

Dealer customer assignments are stored on the scoped dealer record. A customer can only be assigned when that customer is unlocked in the same owner scope.

When a dealer tries to accept a customer contract, the server checks:

1. The dealer is recruited for an owner key.
2. The customer NPC is assigned to that scoped dealer.
3. The customer is unlocked in the same owner scope.
4. The customer is not already reserved by a different active contract owner.

If all checks pass, the dealer contract resolves to the dealer owner's scope.

## Cash, Inventory, and Completed Deals

Dealer cash, inventory, and completed-deal counts are stored on the scoped dealer record. Server patches hydrate dealer runtime state from the scoped record before mutations and capture it after inventory, payment, or completed-deal events.

## Optional Retention Fees

Server owners can enable a weekly dealer upkeep rule for larger RP servers:

```toml
[dealers]
enableDealerRetentionFees = true
weeklyDealerRetentionFee = 1000
```

Retention fees are disabled by default. When enabled, the server processes them during the same weekly pass that resets scoped ATM deposit totals.

On Sunday, recruited dealers whose scoped cash is below `weeklyDealerRetentionFee` send a warning text to the player or organisation members who own that dealer:

> Hey boss, my cash is getting low. I need next week's paycheck by tomorrow or I'm out.

The warning is sent through the dealer's phone conversation for that owner scope and is also mirrored as a short notification for online members. It is recorded once per dealer per in-game day to avoid spam.

For each recruited scoped dealer:

1. If the dealer has at least `weeklyDealerRetentionFee` in scoped dealer cash, that amount is subtracted and the dealer remains recruited.
2. If the dealer cannot cover the fee, the dealer stops working for that owner key.
3. When a dealer stops working, customer assignments are cleared and scoped dealer cash is reset to zero.
4. Recommendation state is preserved, so the previous owner can re-recruit the dealer later if no other owner claims them first.

This makes dealer ownership contestable over time without forcing the rule onto smaller servers.
