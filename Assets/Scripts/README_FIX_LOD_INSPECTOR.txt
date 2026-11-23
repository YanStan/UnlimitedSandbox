Fix: Fine/Fast/Super/Hyper/Max streaming starvation and Inspector.

- Jobs are scheduled in near-to-far order; far LODs share a small cap of 12 pending jobs.
- Hyper queues group by Max parent; Super queues group by Hyper parent.
- Super/Fast/Fine generation extends one parent size past requested radius so full 4x4 child groups can finish and parent can be hidden.
- Inspector chunk sizes are no longer overwritten by OnValidate.
- IMPORTANT: chunk sizes must remain in a 1:4 ratio (defaults 4,16,64,256,1024). The generator logs an error and stops if they do not.
- Some settings are clamped to safe minimums on validation.
- Burst remains enabled for geometry jobs.

No Unity compile or runtime test was possible here. Test on a project copy.
