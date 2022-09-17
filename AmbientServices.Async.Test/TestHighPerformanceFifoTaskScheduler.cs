using AmbientServices;
using AmbientServices.Async;
using AmbientServices.Async.Utility;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

#nullable enable

namespace AmbientServices.Async.Test
{
    [TestClass]
    public class TestHighPerformanceFifoTaskScheduler
    {
        private static readonly AmbientService<IAmbientLogger> LoggerBackend = Ambient.GetService<IAmbientLogger>();
        private static readonly AmbientService<IAmbientStatistics> StatisticsBackend = Ambient.GetService<IAmbientStatistics>();
        private static readonly AmbientService<IMockCpuUsage> MockCpu = Ambient.GetService<IMockCpuUsage>();

        [TestMethod]
        public async Task RunWithStartNew()
        {
            //using IDisposable d = LoggerBackend.ScopedLocalOverride(new AmbientTraceLogger());
            List<Task<Task>> tasks = new();
            for (int i = 0; i < 1000; ++i)
            {
                FakeWork w = new(i, true);
                tasks.Add(HighPerformanceFifoTaskFactory.Default.StartNew(() => w.DoMixedWorkAsync(CancellationToken.None).AsTask()));
            }
            Task.WaitAll(tasks.ToArray());
            foreach (Task<Task> task in tasks)
            {
                await task.Result;
            }
            HighPerformanceFifoTaskScheduler.Default.Reset();
        }
        [TestMethod]
        public void RunInHighPerformanceFifoSynchronizationContext()
        {
            //using IDisposable d = LoggerBackend.ScopedLocalOverride(new AmbientTraceLogger());
            List<Task> tasks = new();
            for (int i = 0; i < 100; ++i)
            {
                FakeWork w = new(i, true);
                tasks.Add(RunInHPContext(() => w.DoMixedWorkAsync(CancellationToken.None).AsTask()));
            }
            Task.WaitAll(tasks.ToArray());
            HighPerformanceFifoTaskScheduler.Default.Reset();
        }
        [TestMethod]
        public async Task RunWithFunc()
        {
            //using IDisposable d = LoggerBackend.ScopedLocalOverride(new AmbientTraceLogger());
            using HighPerformanceFifoTaskScheduler scheduler = HighPerformanceFifoTaskScheduler.Start(nameof(RunWithFunc), ThreadPriority.Highest);
            List<Task<Task>> tasks = new();
            for (int i = 0; i < 1000; ++i)
            {
                FakeWork w = new(i, true);
                tasks.Add(scheduler.Run(() => w.DoMixedWorkAsync(CancellationToken.None).AsTask()));
            }
            Task.WaitAll(tasks.ToArray());
            foreach (Task<Task> task in tasks)
            {
                await task.Result;
            }
            scheduler.Reset();
        }
        [TestMethod]
        public async Task RunWithAction()
        {
            //using IDisposable d = LoggerBackend.ScopedLocalOverride(new AmbientTraceLogger());
            using HighPerformanceFifoTaskScheduler scheduler = HighPerformanceFifoTaskScheduler.Start(nameof(RunWithAction), ThreadPriority.Highest);
            ConcurrentBag<Task> tasks = new();
            for (int i = 0; i < 1000; ++i)
            {
                FakeWork w = new(i, true);
                tasks.Add(scheduler.Run(() => { tasks.Add(w.DoMixedWorkAsync(CancellationToken.None).AsTask()); }));
            }
            while (tasks.Count < 1000)
            {
                await Task.Delay(25);
            }
            Task.WaitAll(tasks.ToArray());
            scheduler.Reset();
        }
        [TestMethod]
        public async Task RunFireAndForget()
        {
            //using IDisposable d = LoggerBackend.ScopedLocalOverride(new AmbientTraceLogger());
            using HighPerformanceFifoTaskScheduler scheduler = HighPerformanceFifoTaskScheduler.Start(nameof(RunFireAndForget), ThreadPriority.Highest);
            ConcurrentBag<Task> tasks = new();
            for (int i = 0; i < 1000; ++i)
            {
                FakeWork w = new(i, true);
                scheduler.FireAndForget(() => tasks.Add(w.DoMixedWorkAsync(CancellationToken.None).AsTask()));
            }
            while (tasks.Count < 1000)
            {
                await Task.Delay(25);
            }
            Task.WaitAll(tasks.ToArray());
            scheduler.Reset();
        }
        [TestMethod]
        public async Task NoStatsStartNew()
        {
            using IDisposable d = StatisticsBackend.ScopedLocalOverride(null);
            using HighPerformanceFifoTaskScheduler scheduler = HighPerformanceFifoTaskScheduler.Start(nameof(NoStatsStartNew), ThreadPriority.Highest);
            HighPerformanceFifoTaskFactory testFactory = new(scheduler);
            List<Task<Task>> tasks = new();
            tasks.Add(scheduler.Run(() => new FakeWork(-1, true).DoMixedWorkAsync(CancellationToken.None).AsTask()));       // note that we need to do mixed work here because otherwise everything runs on one or two threads
            for (int i = 0; i < 100; ++i)
            {
                FakeWork w = new(i, true);
                tasks.Add(testFactory.StartNew(() => w.DoDelayOnlyWorkAsync(CancellationToken.None).AsTask()));
            }
            Task.WaitAll(tasks.ToArray());
            foreach (Task<Task> task in tasks)
            {
                await task.Result;
            }
            await scheduler.Run(() => ValueTask.CompletedTask);
            scheduler.Reset();
        }
        [TestMethod]
        public async Task NoStatsRunWithAction()
        {
            using IDisposable d = StatisticsBackend.ScopedLocalOverride(null);
            using HighPerformanceFifoTaskScheduler scheduler = HighPerformanceFifoTaskScheduler.Start(nameof(NoStatsRunWithAction), ThreadPriority.Highest);
            HighPerformanceFifoTaskFactory testFactory = new(scheduler);
            Task? task = null;
            await scheduler.Run(() => { task = new FakeWork(1, true).DoDelayOnlyWorkAsync(CancellationToken.None).AsTask(); });
            while (task == null)
            {
                await Task.Delay(25);
            }
        }
        [TestMethod]
        public async Task NoStatsFireAndForget()
        {
            using IDisposable d = StatisticsBackend.ScopedLocalOverride(null);
            using HighPerformanceFifoTaskScheduler scheduler = HighPerformanceFifoTaskScheduler.Start(nameof(NoStatsFireAndForget), ThreadPriority.Highest);
            HighPerformanceFifoTaskFactory testFactory = new(scheduler);
            Task? task = null;
            scheduler.FireAndForget(() => task = new FakeWork(1, true).DoDelayOnlyWorkAsync(CancellationToken.None).AsTask());
            while (task == null)
            {
                await Task.Delay(25);
            }
        }
        [TestMethod]
        public void ExecuteWithCatchAndLog()
        {
            using HighPerformanceFifoTaskScheduler scheduler = HighPerformanceFifoTaskScheduler.Start(nameof(ExecuteWithCatchAndLog));
            scheduler.ExecuteWithCatchAndLog(() => throw new ExpectedException());
            scheduler.ExecuteWithCatchAndLog(() => throw new TaskCanceledException());
            //scheduler.ExecuteWithCatchAndLog(() => throw new ThreadAbortException()); // can't construct this, so can't test it
        }
        [TestMethod]
        public void Constructors()
        {
            HighPerformanceFifoTaskFactory testFactory;
            testFactory = new(CancellationToken.None);
            testFactory = new(HighPerformanceFifoTaskScheduler.Default);
            testFactory = new(TaskCreationOptions.None, TaskContinuationOptions.None);
        }
        class MockCpuUsage : IMockCpuUsage
        {
            public float RecentUsage { get; set;}
        }
        [TestMethod]
        public void TooManyWorkers()
        {
            MockCpuUsage mockCpu = new();
            using IDisposable d = MockCpu.ScopedLocalOverride(mockCpu);
            using HighPerformanceFifoTaskScheduler scheduler = HighPerformanceFifoTaskScheduler.Start(nameof(TooManyWorkers), 10, 1, 2);
            Assert.IsTrue(scheduler.ReadyWorkers > 0);
            Assert.IsTrue(scheduler.BusyWorkers == 0);
            HighPerformanceFifoTaskFactory factory = new(scheduler);
            List<Task> tasks = new();
            // the scheduler should think the CPU is very high, so it should enter the crazy usage notify
            mockCpu.RecentUsage = 1.0f;
            for (int i = 0; i < 20; ++i)
            {
                FakeWork w = new(i, true);
                tasks.Add(factory.StartNew(() => w.DoDelayOnlyWorkAsync(CancellationToken.None).AsTask(), CancellationToken.None, TaskCreationOptions.None, scheduler));
            }
            Task.WaitAll(tasks.ToArray());
            scheduler.Reset();
        }
        [TestMethod]
        public async Task ResetManyWorkers()
        {
            MockCpuUsage mockCpu = new();
            using IDisposable s = StatisticsBackend.ScopedLocalOverride(null);
            using IDisposable c = MockCpu.ScopedLocalOverride(mockCpu);
            using HighPerformanceFifoTaskScheduler scheduler = HighPerformanceFifoTaskScheduler.Start(nameof(ResetManyWorkers), 10, 1, 50);
            DateTime lastScaleUp = scheduler.LastScaleUp;
            Debug.Assert(DateTime.UtcNow > lastScaleUp);
            DateTime lastScaleDown = scheduler.LastScaleDown;
            Debug.Assert(DateTime.UtcNow > lastScaleDown);
            DateTime lastReset = scheduler.LastResetTime;
            Debug.Assert(DateTime.UtcNow > lastReset);
            ConcurrentBag<Task> tasks = new();
            int i;
            for (i = 0; i < 50 && scheduler.ReadyWorkers < 3; ++i)
            {
                FakeWork w = new(i, true);
                tasks.Add(scheduler.Run(() => w.DoMixedWorkAsync(CancellationToken.None).AsTask()));       // note that we need to do mixed work here because otherwise everything runs on one or two threads
            }
            // reset the scheduler
            scheduler.Reset();

            Task.WaitAll(tasks.ToArray());
            Assert.IsTrue(scheduler.LastScaleUp > lastScaleUp, $"No scale up, threads={scheduler.Workers}");

            // wait for a while for the reset to finish
            while (scheduler.LastResetTime <= lastReset && DateTime.UtcNow < lastReset.AddSeconds(30))
            {
                // reset the scheduler again just in case
                scheduler.Reset();
                await Task.Delay(50);
            }
            Assert.IsTrue(scheduler.LastResetTime > lastReset, $"No reset, {scheduler.LastResetTime} vs {lastReset} vs {DateTime.UtcNow} i={i}, threads={scheduler.Workers}, readyworkers={scheduler.ReadyWorkers}, busyworkers={scheduler.BusyWorkers}");
        }
        [TestMethod]
        public async Task ScaleDown()
        {
            MockCpuUsage mockCpu = new();
            using IDisposable d = MockCpu.ScopedLocalOverride(mockCpu);     // install a mock CPU so that the scheduler will scale up even when the actual CPU is pegged (as it always should be during unit tests!)
            using HighPerformanceFifoTaskScheduler scheduler = HighPerformanceFifoTaskScheduler.Start(nameof(ScaleDown), 10, 1, 25);
            DateTime lastScaleUp = scheduler.LastScaleUp;
            Debug.Assert(DateTime.UtcNow > lastScaleUp);
            DateTime lastScaleDown = scheduler.LastScaleDown;
            Debug.Assert(DateTime.UtcNow > lastScaleDown);
            HighPerformanceFifoTaskFactory factory = new(scheduler);
            List<Task<Task>> tasks = new();
            int i;
            for (i = 0; i < 100 && scheduler.Workers < 10; ++i)
            {
                FakeWork w = new(i, true);
                tasks.Add(scheduler.Run(() => w.DoMixedWorkAsync(CancellationToken.None).AsTask()));       // note that we need to do mixed work here because otherwise everything runs on one or two threads
            }
            Task.WaitAll(tasks.ToArray());
            Assert.IsTrue(scheduler.LastScaleUp > lastScaleUp, $"No scale up, threads={scheduler.Workers}");
            // now wait for a while for the master thread to scale things down
            while (scheduler.LastScaleDown <= lastScaleDown && DateTime.UtcNow < lastScaleDown.AddSeconds(30))
            {
                await Task.Delay(50);
            }
            Assert.IsTrue(scheduler.LastScaleDown > lastScaleDown, $"No scale down, {scheduler.LastScaleDown} vs {lastScaleDown} vs {DateTime.UtcNow} i={i}, threads={scheduler.Workers}, readyworkers={scheduler.ReadyWorkers}, busyworkers={scheduler.BusyWorkers}");
        }
        [TestMethod, ExpectedException(typeof(ExpectedException))]
        public async Task StartNewException()
        {
            //using IDisposable d = LoggerBackend.ScopedLocalOverride(new AmbientTraceLogger());
            await HighPerformanceFifoTaskFactory.Default.StartNew(() => ThrowExpectedException());
        }
        [TestMethod]
        public void DisposedException()
        {
            HighPerformanceFifoTaskFactory test;
            using (HighPerformanceFifoTaskScheduler testScheduler = HighPerformanceFifoTaskScheduler.Start(nameof(DisposedException)))
            {
                test = new(testScheduler);
            }
            Assert.ThrowsException<TaskSchedulerException>(() => test.StartNew(() => { }));     // our code throws an ObjectDisposedException but TaskScheduler converts it
        }
        private static int UnobservedExceptions;
        [TestMethod]
        public async Task UnobservedTaskException()
        {
            //using IDisposable d = LoggerBackend.ScopedLocalOverride(new AmbientTraceLogger());
            try
            {
                TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
                _ = HighPerformanceFifoTaskFactory.Default.StartNew(() => ThrowExpectedException());
                for (int loop = 0; loop < 100; ++loop)
                {
                    if (UnobservedExceptions > 0) break;
                    await Task.Delay(100);
                }
                Assert.AreEqual(1, UnobservedExceptions);
            }
            finally
            {
                TaskScheduler.UnobservedTaskException -= TaskScheduler_UnobservedTaskException;
            }
        }

        private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            Assert.AreEqual(typeof(AggregateException), e.Exception?.GetType());
            Assert.AreEqual(typeof(ExpectedException), e.Exception?.InnerExceptions[0].GetType());
            Interlocked.Increment(ref UnobservedExceptions);
        }

        private static void ThrowExpectedException()
        {
            throw new ExpectedException();
        }
        private static async Task RunInHPContext(Func<Task> f)
        {
            System.Threading.SynchronizationContext? oldContext = SynchronizationContext.Current;
            bool resetContext = true;
            try
            {
                if (oldContext is HighPerformanceFifoSynchronizationContext) resetContext = false;
                else SynchronizationContext.SetSynchronizationContext(HighPerformanceFifoSynchronizationContext.Default);
                await f();
            }
            catch (AggregateException ex)
            {
                AA.ConvertAggregateException(ex);
                throw;
            }
            finally
            {
                if (resetContext) SynchronizationContext.SetSynchronizationContext(oldContext);
            }
        }
        [TestMethod]
        public void GetScheduledTasks()
        {
            IEnumerable<Task> tasks = HighPerformanceFifoTaskScheduler.Default.GetScheduledTasksDirect();
            Assert.AreEqual(0, tasks.Count());
        }
        [TestMethod]
        public async Task InvokeException()
        {
            await Assert.ThrowsExceptionAsync<ArgumentNullException>(() => HighPerformanceFifoTaskScheduler.Default.Run<int>(null!));
            Assert.ThrowsException<ArgumentNullException>(() => HighPerformanceFifoTaskScheduler.Default.Run(null!));
            Assert.ThrowsException<ArgumentNullException>(() => HighPerformanceFifoTaskScheduler.Default.FireAndForget(null!));
            Assert.ThrowsException<ArgumentNullException>(() => HighPerformanceFifoTaskScheduler.Default.ContinueWith(null!));
            await Assert.ThrowsExceptionAsync<ExpectedException>(() => HighPerformanceFifoTaskScheduler.Default.Run<int>(() => throw new ExpectedException()));
            await Assert.ThrowsExceptionAsync<ExpectedException>(() => HighPerformanceFifoTaskScheduler.Default.Run(() => throw new ExpectedException()));
        }
        [TestMethod]
        public void QueueTaskExceptions()
        {
            Assert.ThrowsException<ArgumentNullException>(() => HighPerformanceFifoTaskScheduler.Default.QueueTaskDirect(null!));
        }
        [TestMethod]
        public void SendAndPostExceptions()
        {
            Assert.ThrowsException<ArgumentNullException>(() => HighPerformanceFifoSynchronizationContext.Default.Post(null!, null));
            Assert.ThrowsException<ArgumentNullException>(() => HighPerformanceFifoSynchronizationContext.Default.Send(null!, null));
        }
        [TestMethod]
        public void CreateCopy()
        {
            SynchronizationContext copy = HighPerformanceFifoSynchronizationContext.Default.CreateCopy();
            Assert.IsNotNull(copy);
            Assert.IsInstanceOfType(copy, typeof(HighPerformanceFifoSynchronizationContext));
        }
        [TestMethod]
        public async Task Send()
        {
            bool called = false;
            HighPerformanceFifoSynchronizationContext.Default.Send(state =>
            {
                Assert.IsNull(state);
                called = true;
            }, null);
            for (int loop = 0; loop < 100; ++loop)
            {
                if (called) break;
                await Task.Delay(100);
            }
            Assert.IsTrue(called);
        }
        [TestMethod]
        public void Properties()
        {
            Assert.IsTrue(HighPerformanceFifoTaskScheduler.Default.Workers >= 0);
            Assert.IsTrue(HighPerformanceFifoTaskScheduler.Default.BusyWorkers >= 0);
            Assert.IsTrue(HighPerformanceFifoTaskScheduler.Default.MaximumConcurrencyLevel >= Environment.ProcessorCount);
        }
        [TestMethod]
        public void IntrusiveSinglyLinkedList()
        {
            InterlockedSinglyLinkedList<NodeTest> list1 = new();
            InterlockedSinglyLinkedList<NodeTest> list2 = new();
            NodeTest node = new() { Value = 1 };
            list1.Push(node);
            Assert.ThrowsException<ArgumentNullException>(() => list2.Push(null!));
            Assert.ThrowsException<InvalidOperationException>(() => list2.Push(node));
            list1.Validate();
            Assert.AreEqual(1, list1.Count);
            list1.Clear();
            Assert.AreEqual(0, list1.Count);
            list2.Push(node);
            NodeTest? popped = list2.Pop();
            Assert.AreEqual(node, popped);
        }
        class NodeTest : IntrusiveSinglyLinkedListNode
        {
            public int Value { get; set; }
        }
        [TestMethod]
        public async Task Worker()
        {
            HighPerformanceFifoTaskScheduler? scheduler = null;
            try
            {
                scheduler = HighPerformanceFifoTaskScheduler.Start(nameof(Worker));
                HighPerformanceFifoWorker worker = HighPerformanceFifoWorker.Start(scheduler, "1", ThreadPriority.Normal);  // the worker disposes of itself
                worker.Invoke(LongWait);
                Assert.IsTrue(worker.IsBusy);
                Assert.ThrowsException<InvalidOperationException>(() => worker.Invoke(LongWait));
                Assert.IsFalse(HighPerformanceFifoWorker.IsWorkerInternalMethod(null));
                Assert.IsFalse(HighPerformanceFifoWorker.IsWorkerInternalMethod(typeof(TestHighPerformanceFifoTaskScheduler).GetMethod(nameof(Worker))));
                Assert.IsTrue(HighPerformanceFifoWorker.IsWorkerInternalMethod(typeof(HighPerformanceFifoWorker).GetMethod("Invoke", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)));
                Assert.IsTrue(HighPerformanceFifoWorker.IsWorkerInternalMethod(typeof(HighPerformanceFifoWorker).GetMethod("WorkerFunc", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)));
                worker.Stop();
                Assert.ThrowsException<InvalidOperationException>(() => worker.Invoke(null!));
            }
            finally
            {
                scheduler?.Dispose();
            }
            Assert.ThrowsException<ObjectDisposedException>(() => scheduler.Run(() => { }));
            Assert.ThrowsException<ObjectDisposedException>(() => scheduler.FireAndForget(() => { }));
            await Assert.ThrowsExceptionAsync<ObjectDisposedException>(() => scheduler.Run(() => ValueTask.CompletedTask));
        }
        public void LongWait()
        {
            Thread.Sleep(5000);
        }
        [TestMethod]
        public void WorkerNullWork()
        {
            using HighPerformanceFifoTaskScheduler scheduler = HighPerformanceFifoTaskScheduler.Start(nameof(WorkerNullWork));
            HighPerformanceFifoWorker worker = scheduler.CreateWorker();    // the worker disposes of itself
            worker.Invoke(null!);
        }
    }
    public class FakeWork
    {
        private readonly bool _fast;
        private readonly long _id;

        public FakeWork(long id, bool fast)
        {
            _fast = fast;
            _id = id;
        }

        public async ValueTask DoMixedWorkAsync(CancellationToken cancel = default)
        {
            ulong hash = GetHash(_id);
            await Task.Yield();
            //string? threadName = Thread.CurrentThread.Name;

            Assert.AreEqual(typeof(HighPerformanceFifoSynchronizationContext), SynchronizationContext.Current?.GetType());
            Stopwatch s = Stopwatch.StartNew();
            for (int outer = 0; outer < (int)(hash % 256) && !cancel.IsCancellationRequested; ++outer)
            {
                Stopwatch cpu = Stopwatch.StartNew();
                // use some CPU
                for (int spin = 0; spin < (int)((hash >> 6) % (_fast ? 16UL : 256UL)); ++spin)
                {
                    double d1 = 0.0000000000000001;
                    double d2 = 0.0000000000000001;
                    for (int inner = 0; inner < (_fast ? 100 : 1000000); ++inner) { d2 = d1 * d2; }
                }
                cpu.Stop();
                Assert.AreEqual(typeof(HighPerformanceFifoSynchronizationContext), SynchronizationContext.Current?.GetType());
                Stopwatch mem = Stopwatch.StartNew();
                // use some memory
                int bytesPerLoop = (int)((hash >> 12) % (_fast ? 10UL : 1024UL));
                int loops = (int)((hash >> 22) % 1024);
                for (int memory = 0; memory < loops; ++memory)
                {
                    byte[] bytes = new byte[bytesPerLoop];
                }
                mem.Stop();
                Assert.AreEqual(typeof(HighPerformanceFifoSynchronizationContext), SynchronizationContext.Current?.GetType());
                Stopwatch io = Stopwatch.StartNew();
                // simulate I/O by blocking
                await Task.Delay((int)((hash >> 32) % (_fast ? 5UL : 500UL)), cancel);
                io.Stop();
                Assert.AreEqual(typeof(HighPerformanceFifoSynchronizationContext), SynchronizationContext.Current?.GetType());
            }
            Assert.AreEqual(typeof(HighPerformanceFifoSynchronizationContext), SynchronizationContext.Current?.GetType());
            //Debug.WriteLine($"Ran work {_id} on {threadName}!", "Work");
        }
        public async ValueTask DoDelayOnlyWorkAsync(CancellationToken cancel = default)
        {
            ulong hash = GetHash(_id);
            await Task.Yield();
            //string? threadName = Thread.CurrentThread.Name;

            Assert.AreEqual(typeof(HighPerformanceFifoSynchronizationContext), SynchronizationContext.Current?.GetType());
            Stopwatch s = Stopwatch.StartNew();
            for (int outer = 0; outer < (int)(hash % 256) && !cancel.IsCancellationRequested; ++outer)
            {
                Stopwatch io = Stopwatch.StartNew();
                // simulate I/O by blocking
                await Task.Delay((int)((hash >> 32) % (_fast ? 5UL : 500UL)), cancel);
                io.Stop();
                Assert.AreEqual(typeof(HighPerformanceFifoSynchronizationContext), SynchronizationContext.Current?.GetType());
            }
            Assert.AreEqual(typeof(HighPerformanceFifoSynchronizationContext), SynchronizationContext.Current?.GetType());
            //Debug.WriteLine($"Ran work {_id} on {threadName}!", "Work");
        }
        private static ulong GetHash(long id)
        {
            unchecked
            {
                ulong x = (ulong)id * 1_111_111_111_111_111_111UL;        // note that this is a prime number (but not a mersenne prime)
                x = (((x & 0xaaaaaaaaaaaaaaaa) >> 1) | ((x & 0x5555555555555555) << 1));
                x = (((x & 0xcccccccccccccccc) >> 2) | ((x & 0x3333333333333333) << 2));
                x = (((x & 0xf0f0f0f0f0f0f0f0) >> 4) | ((x & 0x0f0f0f0f0f0f0f0f) << 4));
                x = (((x & 0xff00ff00ff00ff00) >> 8) | ((x & 0x00ff00ff00ff00ff) << 8));
                x = (((x & 0xffff0000ffff0000) >> 16) | ((x & 0x0000ffff0000ffff) << 16));
                return ((x >> 32) | (x << 32));
            }
        }
    }
}
