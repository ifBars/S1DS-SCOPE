---
title: Deals and Customers
description: Customer unlocks, active contracts, completed deals, and two-player organisation behavior.
---

# Deals and Customers

Customer progression is scoped so RP groups can compete without every unlock and contract becoming global server progress.

## Solo Deal Flow

For a solo player, customer actions resolve to `player:<steamId>`.

When a solo player accepts a customer contract:

1. The customer GUID is reserved in `ActiveCustomerContracts`.
2. The reservation stores the solo owner key.
3. The customer record is upserted for that owner key.
4. The accepted contract is saved as a scoped contract record.
5. Completion releases the active reservation and records the completed customer state back to the same owner key.

Another solo player or organisation cannot take over that active customer contract while it is reserved.

## Two Players in One Organisation

Two members in the same organisation resolve to the same `org:<organisationId>` owner key. That means one member can start an organisation-scoped customer flow and another member can continue work against that same organisation scope.

The active customer contract is still locked against other owner keys. It is not locked against the second member of the same organisation because both members are operating under the same owner key.

## Separate Players or Organisations

A separate solo player or a different organisation gets a different owner key. If the customer already has an active contract reservation for another owner key, the action is rejected with the same user-facing denial path used by the service.

## Customer State

The scoped customer record stores unlock state, relationship delta, recommendation state, offered deal count, completed deliveries, and serialized customer data. The server hydrates this state before mutations and records it after important contract or relationship events.
