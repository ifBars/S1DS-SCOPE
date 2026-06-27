using System;
using System.Collections.Generic;

namespace DedicatedServerMod.Organisations.Utils;

internal sealed class EventSubscriptionHub
{
    private readonly List<Action> _unsubscribeActions = new List<Action>();

    public void Add(Action subscribe, Action unsubscribe)
    {
        subscribe();
        _unsubscribeActions.Add(unsubscribe);
    }

    public void Clear()
    {
        for (int i = _unsubscribeActions.Count - 1; i >= 0; i--)
        {
            try
            {
                _unsubscribeActions[i]();
            }
            catch
            {
            }
        }

        _unsubscribeActions.Clear();
    }
}
