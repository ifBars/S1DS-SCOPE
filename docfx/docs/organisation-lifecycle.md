---
title: Organisation Lifecycle
description: Creating, joining, managing, and leaving organisations.
---

# Organisation Lifecycle

Players start with a solo scope. They can continue alone, create an organisation, or accept an invite.

## Create an Organisation

Creating an organisation:

- Creates a new organisation record.
- Makes the creator the owner.
- Moves the creator from `player:<steamId>` to `org:<organisationId>` for future scoped progression.
- Creates a scoped wallet with the configured starting online balance.

The phone app is the main client UI for creating and managing organisations.

## Invite and Join

Owners and officers can invite players. Invites expire after a short lifetime and are tied to the invitee Steam ID. Accepting an invite moves the player into the organisation owner key. From that point, customer, contract, property, wallet, dealer, quest, and deaddrop state resolves through the organisation.

## Roles

Organisations support three roles:

| Role | Purpose |
| --- | --- |
| Owner | Full control; can invite, kick, and transfer ownership |
| Officer | Can invite and review organisation state |
| Member | Uses organisation progression and can review organisation state |

## Leaving and Transfer

Members can leave an organisation and return to their solo owner key. Owners must transfer ownership before leaving so the organisation is not left without an owner.

When a player joins an organisation, personal property reservations owned by that player are moved to the organisation owner key. This keeps previously purchased property accessible to the organisation instead of leaving it stranded in the player's old solo scope.
