---
title: Base Game Differences
description: What Organisations changes compared with normal Schedule I co-op.
---

# Base Game Differences

In normal Schedule I co-op, many systems behave as one shared crew. If one player buys property, recruits a dealer, unlocks a customer, or progresses a shared quest, the world generally treats that as shared progress for everyone in the session.

Organisations replaces that with scoped ownership.

## Owner Keys

Every scoped action resolves to an owner key:

- Solo player: `player:<steamId>`
- Organisation: `org:<organisationId>`

Two players in the same organisation share the same organisation owner key. Two solo players, or two different organisations, do not.

## Behavior Matrix

| System | Base co-op behavior | Organisations behavior |
| --- | --- | --- |
| Online balance | Shared server/world online balance | Scoped wallet per solo player or organisation |
| ATM deposits | Shared weekly total | Scoped weekly deposit total; reset on ATM week pass |
| Property ownership | Shared player-owned property state | One owner key reserves each property |
| Customer unlocks | Shared customer relationship and unlock state | Customer state is per owner key |
| Customer contracts | Shared active customer deal flow | Active customer contract is reserved to the owner key that accepted it |
| Completed deals | Shared customer progression | Completed delivery state is written back to the accepting owner key |
| Dealer recruitment | Dealer can effectively serve the shared co-op crew | A recruited dealer can work for one owner key at a time |
| Dealer assignments | Shared dealer customer list | Assignments are scoped to the dealer's owner key |
| Dealer cash/inventory | Shared dealer runtime state | Captured and hydrated from the scoped dealer record |
| Dealer retention | No organisation-specific upkeep | Optional weekly retention fee can make unpaid dealers stop working for their current owner |
| Quests and deaddrops | Shared quest/deaddrop progress | Quest, cartel, product market, and deaddrop state are scoped |
| Vehicles | Shared access rules from the base server | Purchased vehicles are tracked against a player or organisation owner |

## Important Non-Goal

Dealer retention fees are disabled by default. When `dealers.enableDealerRetentionFees` is enabled, the server warns the owner scope by dealer text message on Sunday if a recruited dealer is below `dealers.weeklyDealerRetentionFee`. The fee is charged from each recruited dealer's scoped cash during the Monday weekly ATM reset. Dealers that cannot cover the fee stop working for that player or organisation and become available to recruit again.
