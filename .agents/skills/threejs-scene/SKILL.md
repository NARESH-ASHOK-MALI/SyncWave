---
name: threejs-scene
description: Use when building or editing the React Three Fiber hero scene for the SyncWave landing page — the source-orb-to-device-nodes fan-out visualization.
---

# Three.js Scene Conventions

- Central "source" sphere pulses via useFrame + sine wave on scale, never via CSS.
- Device nodes orbit the source on independent angle/radius/speed per device —
  never synchronize their phase, the point is that devices are independent.
- Connect source to each device with drei <Line>, recomputed every frame from
  live node position (not static geometry) since nodes move.
- Keep total scene under ~3 device nodes + 1 source + lines for perf; more
  needs instancing.
- Dispose of geometries/materials on unmount; check with r3f-perf during dev.
- Respect prefers-reduced-motion: if set, freeze orbit position and only
  pulse opacity, don't disable the scene entirely.
