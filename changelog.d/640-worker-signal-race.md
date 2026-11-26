---
version: 1.2.3
section: The server
---
- **Game loop workers: a stale interrupt can no longer strand the next batch** (#640). When a batch was signalled
  before a worker reached its idle wait, an interrupt delivered earlier stayed pending and could fire in the completion
  signal, inside the loop's `finally`; the worker loop then ended and the next batch waited for a worker that was gone.
  Nothing in the game interrupts a worker (the watchdog waits, then restarts), so this showed only in a test, but the
  loop is now safe against it at the cost of one volatile read per batch: `InterruptWorker` flags the worker, which
  consumes the interrupt as it starts its next batch; an interrupt in the completion signal no longer ends the loop; and
  the game loop's barrier wait re-checks its count every 100 ms, so a lost wake-up can't hang it.
  `UT_SeptemberFourteenWorkerRecovery` gains tests for the interrupted completion and for how the interrupt is cleared.
