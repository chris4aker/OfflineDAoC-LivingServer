using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

[NonParallelizable]
public class UT_SeptemberFourteenWorkerRecovery
{
    // #640 review, point 3: an interrupt inside a worker's completion signal
    // must neither end the worker nor hang the game loop's barrier. The test
    // hook replaces Signal() with "Signal(), then throw as an interrupt would".
    [Test]
    public void AnInterruptedCompletionSignalNeitherLosesTheWorkerNorHangsTheBatch()
    {
        var previous = SynchronizationContext.Current;
        try
        {
            using var pool = new GameLoopThreadPoolMultiThreaded(2);
            pool.Init();
            Type type = typeof(GameLoopThreadPoolMultiThreaded);
            FieldInfo hook = type.GetField("_completionSignalForTests", BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo interrupted = type.GetField("_interruptedCompletions", BindingFlags.NonPublic | BindingFlags.Instance);
            var workers = (Thread[])type.GetField("_workers", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(pool);
            Assert.That(hook, Is.Not.Null);
            Assert.That(interrupted, Is.Not.Null);
            Thread worker = workers[0];
            hook.SetValue(pool, (Action<CountdownEvent>)(barrier =>
            {
                barrier.Signal();
                throw new ThreadInterruptedException();
            }));

            int RunBatch()
            {
                int count = 0;
                Exception error = null;
                var caller = new Thread(() =>
                {
                    try { pool.ExecuteForEach(new List<int> { 0, 1 }, 2, _ => Interlocked.Increment(ref count)); }
                    catch (Exception e) { error = e; }
                }) { IsBackground = true };
                caller.Start();
                Assert.That(caller.Join(5000), Is.True, "the batch completed");
                Assert.That(error, Is.Null);
                return count;
            }

            Assert.That(RunBatch(), Is.EqualTo(2));
            // The hook signals first, so the batch can end before the worker's
            // catch counts the interruption: wait for the count, briefly.
            Assert.That(SpinWait.SpinUntil(() => (int)interrupted.GetValue(pool) == 1, 5000), Is.True,
                "the interrupted completion was counted");
            hook.SetValue(pool, null);
            Assert.That(RunBatch(), Is.EqualTo(2), "the next batch has its worker");
            Assert.That(workers[0], Is.SameAs(worker), "the worker was kept, not restarted");
            Assert.That(worker.IsAlive, Is.True);
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    // #640: the worker loop relies on ConsumeStaleInterrupt (Thread.Sleep(0))
    // clearing an interrupt that is pending while the thread is not blocked.
    // Pin that on the runtime CI uses, so the fix can't silently rest on a
    // platform detail (Java's sleep(0), for one, does not throw).
    [Test]
    public void ConsumeStaleInterruptClearsAPendingInterrupt()
    {
        MethodInfo consume = typeof(GameLoopThreadPoolMultiThreaded)
            .GetMethod("ConsumeStaleInterrupt", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That(consume, Is.Not.Null);
        Exception error = null;
        var thread = new Thread(() =>
        {
            try
            {
                Thread.CurrentThread.Interrupt();
                consume.Invoke(null, null);
                // A pending interrupt would throw at this first blocking wait.
                Thread.Sleep(20);
            }
            catch (Exception e) { error = e; }
        }) { IsBackground = true };
        thread.Start();
        Assert.That(thread.Join(5000), Is.True);
        Assert.That(error, Is.Null, "Thread.Sleep(0) left the interrupt pending");
    }

    // Control for the test above: without ConsumeStaleInterrupt the same
    // pending interrupt does throw at the first blocking wait.
    [Test]
    public void APendingInterruptThrowsAtTheNextWaitWithoutConsuming()
    {
        bool interrupted = false;
        var thread = new Thread(() =>
        {
            try
            {
                Thread.CurrentThread.Interrupt();
                Thread.Sleep(20);
            }
            catch (ThreadInterruptedException) { interrupted = true; }
        }) { IsBackground = true };
        thread.Start();
        Assert.That(thread.Join(5000), Is.True);
        Assert.That(interrupted, Is.True);
    }

    [Test]
    public void IdleWorkerInterruptionCannotConsumeTheNextBatchSignal()
    {
        var previous = SynchronizationContext.Current;
        try
        {
            using var pool = new GameLoopThreadPoolMultiThreaded(2);
            pool.Init();
            var signals = (ManualResetEventSlim[])typeof(GameLoopThreadPoolMultiThreaded)
                .GetField("_workReady", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(pool);
            int recoveries = 0;
            for (int round = 0; round < 30; round++)
            {
                // A pending interrupt just before the batch, through the one
                // sanctioned way to interrupt a worker (#640 review: the
                // worker consumes it only when InterruptWorker flagged it).
                pool.InterruptWorker(0);
                Exception error = null;
                int count = 0;
                using var finished = new ManualResetEventSlim(false);
                var caller = new Thread(() =>
                {
                    try { pool.ExecuteForEach(new List<int> { 0, 1 }, 2, _ => Interlocked.Increment(ref count)); }
                    catch (Exception e) { error = e; }
                    finally { finished.Set(); }
                }) { IsBackground = true };
                caller.Start();
                if (!finished.Wait(1500))
                {
                    recoveries++;
                    // Test cleanup only: re-signal the lost batch so no test
                    // leaves a background caller hung or blocks disposal.
                    signals[0].Set();
                }
                Assert.That(finished.Wait(10000), Is.True, "Test batch could not be recovered");
                caller.Join();
                Assert.That(error, Is.Null);
                Assert.That(count, Is.EqualTo(2));
                if (recoveries > 0) break;
            }
            Assert.That(recoveries, Is.Zero, "Idle interrupt swallowed the next batch signal and stranded its completion barrier");
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }
}
