using UnityEngine;

/// <summary>
/// Drop this component on any GameObject to have it automatically call
/// <see cref="Minit.LoadingDone"/> on the first Update frame after the scene loads.
///
/// This is a convenience for games that don't have a precise readiness signal.
/// If your game has its own loading gate (e.g. assets loaded, first frame rendered,
/// countdown finished), call <see cref="Minit.LoadingDone"/> directly from that
/// point instead and leave this component off.
/// </summary>
public class MinitReady : MonoBehaviour
{
    private bool _fired;

    private void Update()
    {
        if (_fired) return;
        _fired = true;
        Minit.LoadingDone();
        enabled = false;
    }
}
