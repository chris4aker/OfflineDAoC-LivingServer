---
version: 1.2.3
section: The server
---
- **Game loop workers: a stale interrupt can no longer strand the next batch** (#640). When a batch was signalled
  before a worker reached its idle wait, an interrupt delivered earlier stayed pending and could fire in the completion
  signal, inside the loop's `finally`; the worker loop then ended and the next batch waited for a worker that was gone.
  The worker now clears a pending interrupt as it starts a batch, and an interrupt in the completion signal no longer
  ends the loop. `UT_SeptemberFourteenWorkerRecovery` gains a test that pins how the interrupt is cleared, with a control.
