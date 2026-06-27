---
title: Quests and Deaddrops
description: Scoped quest, cartel, product market, and deaddrop behavior.
---

# Quests and Deaddrops

Quest state is scoped so one RP group can progress without forcing every other player or organisation into the same quest state.

## Quest Scope

Each owner key has a quest scope record. The scope includes quest-manager data, cartel status, cartel deal data, cartel influence, map region unlocks, product market state, variable values, deaddrop quest data, and customer state snippets needed by quest flows.

The server hydrates the active owner scope into runtime quest systems and suppresses capture while it is replaying scoped state. That prevents a hydrated client view from being accidentally written into the wrong owner scope.

## Deaddrops

Deaddrop quests and storage are scoped. Active deaddrops are resolved by owner key, and pending supplier deaddrop reservations prevent another owner key from taking the same physical drop while the original owner is preparing it.

When a player opens or mutates deaddrop state, the server checks whether the current owner key can access that deaddrop. Other owner keys are denied until the reservation or active scoped ownership no longer applies.
