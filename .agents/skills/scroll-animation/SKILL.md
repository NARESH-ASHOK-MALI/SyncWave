---
name: scroll-animation
description: Use when adding or editing scroll-triggered reveals for the SyncWave walkthrough section or any other scroll-tied motion on the landing page.
---

# Scroll Animation Conventions

- GSAP + ScrollTrigger only, registered once in the entry component.
- Each walkthrough step: fade + 40px translateY, scrub true, start 'top 75%'
  end 'top 40%' — keep these values consistent across all steps unless a
  step specifically needs a different pace.
- Never stack more than one ScrollTrigger per element without namespacing IDs.
- Clean up ScrollTriggers in a useEffect return to avoid duplicates on
  hot-reload during dev.
