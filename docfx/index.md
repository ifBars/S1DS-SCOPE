---
title: Organisations
description: Guides for running Schedule I dedicated servers with organisation-scoped progression.
---

# Organisations

Organisations turns a Schedule I dedicated server into an RP-oriented progression server. Players can keep playing solo, create an organisation, or join another player's organisation. The addon then resolves most shared progression through an owner key: `player:<steamId>` for solo players and `org:<organisationId>` for organisation members.

This changes the base co-op model. Instead of every connected player implicitly sharing the same world progression, customers, contracts, properties, wallets, dealer state, and quest state are scoped to the player or organisation that owns the activity.

## Start Here

- [Base Game Differences](docs/base-game-differences.md): the fastest way to understand what changes.
- [Organisation Lifecycle](docs/organisation-lifecycle.md): creating, inviting, leaving, and transferring ownership.
- [Deals and Customers](docs/deals-and-customers.md): how customer unlocks, contracts, and completed deals are scoped.
- [Dealers](docs/dealers.md): recruitment, ownership contention, assignments, inventory, cash, and completed deals.
- [Administration and Testing](docs/administration-and-testing.md): fresh-save rollout, builds, and smoke tests.

## Fresh Save Requirement

Run Organisations on a new dedicated-server save. The addon changes how ownership and progression are persisted, so adding it to an established shared co-op save can leave properties, customers, contracts, quests, or wallet state with the wrong owner.
