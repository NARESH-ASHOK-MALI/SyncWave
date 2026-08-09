# SyncWave Bug Fix Work Plan: Muted/Zero Endpoint Volume Silently Blocks Device Output

**Branch name suggestion:** `fix/endpoint-volume-unmute-on-connect`
**Priority:** High (silent failure, no error shown to user, looks like app bug not OS state)
**Estimated effort:** 1–2 hours implementation + 30 min testing

---

## 1. Problem Statement

If a secondary output device's **Windows endpoint volume** is at 0% or muted *before* SyncWave initializes its pipeline for that device, the app's per-device volume slider has no audible effect. Moving the SyncWave slider changes internal software gain via `VolumeWaveProvider`, but the OS-level endpoint mute/volume is a separate downstream multiplier applied by the Windows audio engine — after SyncWave's output leaves the app. A muted/zeroed endpoint zeroes the signal regardless of app-level gain.

**User-visible symptom:** Device A is connected, Windows volume was left at 0/muted from a prior session. User opens SyncWave, sets Source (Device B), plays audio, raises Device A's slider in-app — no sound from Device A. Looks like a broken slider; it isn't.

---

## 2. Root Cause

- `VolumeWaveProvider.Volume` only scales samples *before* they reach `WasapiOut`.
- `WasapiOut` renders to an OS audio **endpoint**, whose volume/mute state is controlled independently via `IAudioEndpointVolume` (`MMDevice.AudioEndpointVolume`).
- SyncWave never reads or corrects this endpoint state — it assumes endpoints are unmuted at some reasonable level when the pipeline starts.

---

## 3. Fix Overview

Add an **endpoint normalization step** that runs when a device's audio pipeline is (re)built, using NAudio's `MMDevice.AudioEndpointVolume` API to:
1. Unmute the endpoint if muted.
2. Raise `MasterVolumeLevelScalar` to 50% if it's at/near 0.

This is a **one-time correction at connect time**, not a persistent lock — SyncWave should not fight the user if they deliberately change Windows volume afterward. This preserves current design philosophy (per-device control lives in the app, not in Windows) while fixing the specific silent-start failure.

---

## 4. Files Likely Affected

*(Confirm exact names/paths in the repo before starting — these are based on the architecture described in prior work.)*

| File | Change |
|---|---|
| Device pipeline setup/init class (wherever `BufferedWaveProvider` → `VolumeWaveProvider` → `WasapiOut` chain is constructed per device) | Call endpoint-normalization method before/while building the pipeline for each non-Source device |
| A new or existing `AudioDeviceHelper` / `AudioEndpointUtils` static class | Add `EnsureEndpointIsAudible(MMDevice device)` method |
| Device connect/refresh logic (wherever devices are enumerated and added to the active output list) | Ensure normalization runs on initial connect AND on reconnect (e.g., device unplugged/replugged mid-session) |
| ViewModel/UI slider handler (optional, defensive) | Optionally re-check endpoint mute state when user first interacts with that device's slider in a session |

---

## 5. Implementation Steps

### Step 1 — Add the helper method
Create (or extend an existing utils class):

```csharp
using NAudio.CoreAudioApi;

public static class AudioEndpointUtils
{
    /// <summary>
    /// Ensures a device's OS-level endpoint is not muted and not at 0 volume,
    /// so the app's internal VolumeWaveProvider gain is actually audible.
    /// Runs once per pipeline (re)build — does not persistently override
    /// user changes made afterward via Windows.
    /// </summary>
    public static void EnsureEndpointIsAudible(MMDevice device)
    {
        if (device?.AudioEndpointVolume == null) return;

        var epVolume = device.AudioEndpointVolume;

        if (epVolume.Mute)
        {
            epVolume.Mute = false;
        }

        if (epVolume.MasterVolumeLevelScalar < 0.01f)
        {
            epVolume.MasterVolumeLevelScalar = 1.0f;
        }
    }
}
```

### Step 2 — Call it during pipeline construction
Locate the method that builds the `BufferedWaveProvider` → `VolumeWaveProvider` → `WasapiOut` chain for each non-Source device. Call `AudioEndpointUtils.EnsureEndpointIsAudible(mmDevice)` **before** `WasapiOut.Init(...)` is called, using the same `MMDevice` reference already resolved for that output.

Since COM audio calls in this codebase are already routed through `Task.Run()` to MTA threadpool threads (per existing convention for `ActivateAudioInterfaceAsync`), keep this call on the same thread context as the rest of the pipeline init — `IAudioEndpointVolume` is also a COM interface and should follow the same threading discipline already established in the project.

### Step 3 — Handle reconnect/hot-plug case
If a device is disconnected and reconnected mid-session (already-handled scenario elsewhere in the app per device enumeration logic), make sure the same `EnsureEndpointIsAudible` call fires again on re-add — not just on first app launch. A device could be muted at the OS level *after* being reconnected, independent of its first-connect state.

### Step 4 — (Optional, defensive) Re-check on first slider interaction
When the user moves a device's slider in the SyncWave UI for the first time in a session, optionally re-call `EnsureEndpointIsAudible` before applying the new `VolumeWaveProvider.Volume` value. This catches edge cases where something external (another app, a hardware mute button, Windows itself) re-muted the endpoint after pipeline init but before the user touched the slider. Skip this if it adds complexity disproportionate to the benefit — Step 2 alone likely covers the vast majority of real cases.

### Step 5 — Do NOT make this a persistent lock
Explicitly avoid re-forcing the endpoint to 100% on every volume-changed event or on a timer. The fix corrects the *starting state* only. If the user manually lowers Device A's Windows volume after SyncWave has started, that's an intentional action outside the app's per-device model — SyncWave should not override it. Overriding it would break trust and produce a different confusing bug ("why does my Windows volume keep jumping back to 100%?").

---

## 6. Testing Plan

Perform each test with High Performance Mode both **off** and **on**, since buffer/threading differs between modes.

| # | Scenario | Steps | Expected Result |
|---|---|---|---|
| 1 | Baseline repro (pre-fix confirmation) | Mute Device A in Windows → connect Device B as Source → play audio → raise Device A slider in SyncWave | *(Before fix)* No audio from A. *(After fix)* Audio plays from A. |
| 2 | Zero-volume (not muted) repro | Set Device A's Windows volume to 0% (not muted) → repeat above | Audio plays from A after fix. |
| 3 | Normal case regression | Device A already at 50% Windows volume, unmuted → connect as normal | No behavior change — endpoint untouched since it wasn't muted/zero. |
| 4 | User lowers Windows volume mid-session | With SyncWave running and Device A audible, manually mute Device A in Windows | SyncWave does **not** auto-unmute it — confirms fix doesn't fight user intent. |
| 5 | Hot-plug reconnect while muted | Mute Device A in Windows → disconnect device → reconnect → SyncWave re-adds it | Audio plays from A without needing app restart. |
| 6 | Multiple secondary devices, mixed states | Device A muted, Device C at 0%, Device D normal — all non-Source | All three become audible after fix; D's behavior unchanged. |
| 7 | Source device unaffected | Confirm fix is never applied to the Source device's own endpoint (Source uses native OS path per existing design and should not be touched) | No change to Source device behavior. |

---

## 7. Edge Cases / Notes for Implementation

- **Don't touch the Source device's endpoint.** The Source device intentionally uses the native OS audio path (per existing architecture) and is excluded from the per-device pipeline — confirm the normalization call is only wired into the non-Source device pipeline path, not applied globally to all enumerated devices.
- **`MMDevice.AudioEndpointVolume` can throw** on some virtual/software devices or during rapid hot-plug transitions. Wrap the call in a try/catch and log-and-continue rather than letting it crash pipeline init — a failed volume-normalization attempt shouldn't block the device from being added at all (worst case: it stays silent, same as today, not worse).
- **Threading:** since this touches a COM interface, verify it behaves correctly when called from whatever thread context the rest of pipeline setup runs on (MTA via `Task.Run()`, per existing convention). Test specifically for COM threading exceptions during rapid connect/disconnect cycles.
- **No UI/UX changes required** for the core fix — this is a backend correction. A "Device was muted in Windows — volume restored" toast/notification is a nice-to-have, not required for the fix itself; consider only if you want extra transparency for users troubleshooting their own setup.

---

## 8. Rollout

Consistent with existing branch-based workflow:
1. Build `fix/endpoint-volume-unmute-on-connect` branch off `main`.
2. Implement Steps 1–3 (core fix) first; treat Step 4 as optional/stretch.
3. Run through the full test matrix in Section 6 manually (this is a hardware/OS-state-dependent bug — not easily unit-testable).
4. Verify no regression on the Source device and on already-unmuted secondary devices.
5. Fast-forward merge into `main` after real-device verification, per existing project convention.

---

## 9. Definition of Done

- [x] `EnsureEndpointIsAudible` helper implemented and called during non-Source device pipeline init
- [x] Reconnect/hot-plug path also triggers the check
- [x] Source device confirmed untouched
- [ ] Manual test matrix (Section 6) passed on real hardware, both performance modes
- [ ] No regression: devices already unmuted/audible behave identically to before
- [ ] Confirmed fix does not persistently override user's manual Windows volume changes mid-session
- [ ] Merged to `main`
