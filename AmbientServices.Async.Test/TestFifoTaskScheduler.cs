using AmbientServices;
using AmbientServices.Utilities;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

#nullable enable

namespace AmbientServices.Test
{
    [TestClass]
    public class TestFifoTaskScheduler
    {
        private static readonly AmbientService<IAmbientLogger> LoggerBackend = Ambient.GetService<IAmbientLogger>();
        private static readonly AmbientService<IAmbientStatistics> StatisticsBackend = Ambient.GetService<IAmbientStatistics>();
        private static readonly AmbientService<IMockCpuUsage> MockCpu = Ambient.GetService<IMockCpuUsage>();
        private static int TasksInlined;

        private static void FifoTaskScheduler_TaskInlined(object? sender, TaskInlinedEventArgs e)
        {
            Interlocked.Increment(ref TasksInlined);
        }

        [TestMethod]
        public void UnhandledException()
        {
            Guid unique = Guid.NewGuid();
            UnhandledExceptionTracker tracker = new(unique);
            using FifoTaskScheduler scheduler = FifoTaskScheduler.Start(nameof(UnhandledException), priority: ThreadPriority.Highest);
            try
            {
                FifoTaskScheduler.UnhandledException += tracker.FifoTaskScheduler_UnhandledException;
                scheduler.ExecuteAction(() => throw new ExpectedException(nameof(UnhandledException) + ":" + unique));
            }
            finally
            {
                FifoTaskScheduler.UnhandledException -= tracker.FifoTaskScheduler_UnhandledException;
            }
            Assert.AreEqual(1, tracker.UnhandledExceptions);
            Assert.AreEqual(1, tracker.FifoTaskSchedulerCount);
        }
        [TestMethod]
        public void TaskInlinedEvent()
        {
            try
            {
                FifoTaskScheduler.TaskInlined += FifoTaskScheduler_TaskInlined;
                // force a task to run inline
                using FifoTaskScheduler scheduler = FifoTaskScheduler.Start(nameof(TaskInlinedEvent), 1, 2, 10);
                FifoTaskFactory factory = new(scheduler);
                List<Task> tasks = new();
                CancellationTokenSource cts = new();
                for (int i = 0; i < 2000; ++i)
                {
                    FakeWork w = new(i, true);
                    tasks.Add(factory.StartNew(() =>
                    {
                        if (TasksInlined > 0) { cts.Cancel(); return Task.CompletedTask; }
                        return w.DoDelayOnlyWorkAsync(CancellationToken.None).AsTask();
                    }, CancellationToken.None, TaskCreationOptions.None, scheduler));
                }
                Task.WaitAll(tasks.ToArray());
                if (TasksInlined > 0) return;   // this works too!
            }
            catch (OperationCanceledException)
            {
                // we expect to get canceled for this test!
                return;
            }
            finally
            {
                FifoTaskScheduler.TaskInlined -= FifoTaskScheduler_TaskInlined;
            }
            Assert.Fail("Unable to force task to run inline!");
        }

        [TestMethod]
        public async Task RunWithStartNew()
        {
            //using IDisposable d = LoggerBackend.ScopedLocalOverride(new AmbientTraceLogger());
            List<Task<Task>> tasks = new();
            for (int i = 0; i < 1000; ++i)
            {
                FakeWork w = new(i, true);
                tasks.Add(FifoTaskFactory.Default.StartNew(() => w.DoMixedWorkAsync(CancellationToken.None).AsTask()));
            }
            await Task.WhenAll(tasks.ToArray());
            foreach (Task<Task> task in tasks)
            {
                await task.Result;
            }
            FifoTaskScheduler.Default.Reset();
        }
        [TestMethod]
        public async Task RunWithFunc()
        {
            //using IDisposable d = LoggerBackend.ScopedLocalOverride(new AmbientTraceLogger());
            using FifoTaskScheduler scheduler = FifoTaskScheduler.Start(nameof(RunWithFunc), priority: ThreadPriority.Highest);
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
            using FifoTaskScheduler scheduler = FifoTaskScheduler.Start(nameof(RunWithAction), priority: ThreadPriority.Highest);
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
            using FifoTaskScheduler scheduler = FifoTaskScheduler.Start(nameof(RunFireAndForget), priority: ThreadPriority.Highest);
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
            using FifoTaskScheduler scheduler = FifoTaskScheduler.Start(nameof(NoStatsStartNew), priority: ThreadPriority.Highest);
            FifoTaskFactory testFactory = new(scheduler);
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
            await scheduler.Run(() => new ValueTask());
            scheduler.Reset();
        }
        [TestMethod]
        public async Task NoStatsRunWithAction()
        {
            using IDisposable d = StatisticsBackend.ScopedLocalOverride(null);
            using FifoTaskScheduler scheduler = FifoTaskScheduler.Start(nameof(NoStatsRunWithAction), priority: ThreadPriority.Highest);
            FifoTaskFactory testFactory = new(scheduler);
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
            using FifoTaskScheduler scheduler = FifoTaskScheduler.Start(nameof(NoStatsFireAndForget), priority: ThreadPriority.Highest);
            FifoTaskFactory testFactory = new(scheduler);
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
            using FifoTaskScheduler scheduler = FifoTaskScheduler.Start(nameof(ExecuteWithCatchAndLog));
            scheduler.ExecuteWithCatchAndLog(() => throw new ExpectedException());
            scheduler.ExecuteWithCatchAndLog(() => throw new TaskCanceledException());
            //scheduler.ExecuteWithCatchAndLog(() => throw new ThreadAbortException()); // can't construct this, so can't test it
        }
        [TestMethod]
        public void Constructors()
        {
            FifoTaskFactory testFactory;
            testFactory = new(CancellationToken.None);
            testFactory = new(FifoTaskScheduler.Default);
            testFactory = new(TaskCreationOptions.None, TaskContinuationOptions.None);
        }
        class MockCpuUsage : IMockCpuUsage
        {
            public float RecentUsage { get; set; }
        }
        [TestMethod]
        public void TooManyWorkers()
        {
            MockCpuUsage mockCpu = new();
            using IDisposable d = MockCpu.ScopedLocalOverride(mockCpu);
            using FifoTaskScheduler scheduler = FifoTaskScheduler.Start(nameof(TooManyWorkers), 1, 2, 10);
            Assert.IsGreaterThan(0, scheduler.ReadyWorkers);
            Assert.AreEqual(0, scheduler.BusyWorkers);
            FifoTaskFactory factory = new(scheduler);
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
            using FifoTaskScheduler scheduler = FifoTaskScheduler.Start(nameof(ResetManyWorkers), 1, 100, 10);
            DateTime lastScaleUp = scheduler.LastScaleUp;
            Debug.Assert(DateTime.UtcNow > lastScaleUp);
            DateTime lastScaleDown = scheduler.LastScaleDown;
            Debug.Assert(DateTime.UtcNow > lastScaleDown);
            DateTime lastReset = scheduler.LastResetTime;
            Debug.Assert(DateTime.UtcNow > lastReset);
            ConcurrentBag<Task> tasks = new();
            int i;
            for (i = 0; i < 250 && scheduler.ReadyWorkers < 3; ++i)
            {
                FakeWork w = new(i, true);
                tasks.Add(scheduler.Run(() => w.DoMixedWorkAsync(CancellationToken.None).AsTask()));       // note that we need to do mixed work here because otherwise everything runs on one or two threads
            }
            // reset the scheduler
            scheduler.Reset();

            Task.WaitAll(tasks.ToArray());
            Assert.IsTrue(scheduler.LastScaleUp > lastScaleUp, $"No scale up, i={i}, threads={scheduler.Workers}");

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
            using FifoTaskScheduler scheduler = FifoTaskScheduler.Start(nameof(ScaleDown), 1, 25, 10);
            DateTime lastScaleUp = scheduler.LastScaleUp;
            Debug.Assert(DateTime.UtcNow > lastScaleUp);
            DateTime lastScaleDown = scheduler.LastScaleDown;
            Debug.Assert(DateTime.UtcNow > lastScaleDown);
            FifoTaskFactory factory = new(scheduler);
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
        [TestMethod]
        public async Task StartNewException()
        {
            //using IDisposable d = LoggerBackend.ScopedLocalOverride(new AmbientTraceLogger());
            await Assert.ThrowsExactlyAsync<ExpectedException>(async () => await FifoTaskFactory.Default.StartNew(() => ThrowExpectedException()));
        }
        [TestMethod]
        public void DisposedException()
        {
            FifoTaskFactory test;
            using (FifoTaskScheduler testScheduler = FifoTaskScheduler.Start(nameof(DisposedException)))
            {
                test = new(testScheduler);
            }
            Assert.Throws<TaskSchedulerException>(() => test.StartNew(() => { }));     // our code throws an ObjectDisposedException but TaskScheduler converts it
        }

        [TestMethod]        // Note that this test will fail when run in isolation, possibly because the garbage collection can't be forced to collect the task, or the debugger holds on to the task?
        public async Task UnobservedTaskException()
        {
            //using IDisposable d = LoggerBackend.ScopedLocalOverride(new AmbientTraceLogger());
            Guid unique = Guid.NewGuid();
            UnhandledExceptionTracker tracker = new(unique);
            try
            {
                TaskScheduler.UnobservedTaskException += tracker.TaskScheduler_UnobservedTaskException;
                FifoTaskScheduler.UnhandledException += tracker.TaskScheduler_UnobservedTaskException;
                StartTaskWithUnobservedException(unique);
                for (int loop = 0; loop < 100; ++loop)
                {
                    if (tracker.UnhandledExceptions > 0) break;
                    await Task.Delay(100);
                    GC.Collect(2, GCCollectionMode.Forced, true, true);
                    GC.WaitForPendingFinalizers();
                }
                Assert.IsGreaterThanOrEqualTo(1, tracker.UnhandledExceptions);
                Assert.IsGreaterThanOrEqualTo(1, tracker.TaskSchedulerCount);
            }
            finally
            {
                FifoTaskScheduler.UnhandledException -= tracker.TaskScheduler_UnobservedTaskException;
                TaskScheduler.UnobservedTaskException -= tracker.TaskScheduler_UnobservedTaskException;
            }
        }
        private static void StartTaskWithUnobservedException(Guid unique)
        {
            _ = FifoTaskFactory.Default.StartNew(() => ThrowExpectedUnobservedTaskException(unique));
        }

        class UnhandledExceptionTracker
        {
            private Guid _unique;
            private int _unhandledExceptions;
            private int _taskSchedulerCount;
            private int _fifoTaskSchedulerCount;

            internal UnhandledExceptionTracker(Guid unique)
            {
                _unique = unique;
            }

            internal int UnhandledExceptions => _unhandledExceptions;
            internal int TaskSchedulerCount => _taskSchedulerCount;
            internal int FifoTaskSchedulerCount => _fifoTaskSchedulerCount;

            internal void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
            {
                Assert.AreEqual(typeof(AggregateException), e.Exception?.GetType());
                Assert.AreEqual(typeof(ExpectedException), e.Exception?.InnerExceptions[0].GetType());
                if ((e.Exception?.InnerExceptions[0] as ExpectedException)?.TestName?.Contains(_unique.ToString()) ?? false) Interlocked.Increment(ref _unhandledExceptions);
                Interlocked.Increment(ref _taskSchedulerCount);
                e.SetObserved();
            }
            internal void FifoTaskScheduler_UnhandledException(object? sender, UnobservedTaskExceptionEventArgs e)
            {
                Assert.AreEqual(typeof(AggregateException), e.Exception?.GetType());
                Assert.AreEqual(typeof(ExpectedException), e.Exception?.InnerExceptions[0].GetType());
                if ((e.Exception?.InnerExceptions[0] as ExpectedException)?.TestName?.Contains(_unique.ToString()) ?? false) Interlocked.Increment(ref _unhandledExceptions);
                Interlocked.Increment(ref _fifoTaskSchedulerCount);
                e.SetObserved();
            }
        }

        private static void ThrowExpectedUnobservedTaskException(Guid unique)
        {
            throw new ExpectedException(nameof(UnobservedTaskException) + ":" + unique);
        }
        private static void ThrowExpectedException()
        {
            throw new ExpectedException();
        }
        private static async Task RunAndUnwrapAggregateExceptions(Func<Task> f)
        {
            try
            {
                await f();
            }
            catch (AggregateException ex)
            {
                Async.ConvertAggregateException(ex);
                throw;
            }
        }
        [TestMethod]
        public void GetScheduledTasks()
        {
            IEnumerable<Task> tasks = FifoTaskScheduler.Default.GetScheduledTasksDirect();
            Assert.AreEqual(0, tasks.Count());
        }
        [TestMethod]
        public async Task InvokeException()
        {
            await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => FifoTaskScheduler.Default.Run<int>(null!));
            await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => FifoTaskScheduler.Default.TransferWork((Func<ValueTask>?)null!));
            await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => FifoTaskScheduler.Default.TransferWork((Func<ValueTask<int>>?)null!));
            Assert.ThrowsExactly<ArgumentNullException>(() => FifoTaskScheduler.Default.Run(null!));
            Assert.ThrowsExactly<ArgumentNullException>(() => FifoTaskScheduler.Default.FireAndForget(null!));
            await Assert.ThrowsExactlyAsync<ExpectedException>(() => FifoTaskScheduler.Default.Run<int>(() => throw new ExpectedException()));
            await Assert.ThrowsExactlyAsync<ExpectedException>(() => FifoTaskScheduler.Default.Run(() => throw new ExpectedException()));
        }
        [TestMethod]
        public void QueueTaskExceptions()
        {
            Assert.ThrowsExactly<ArgumentNullException>(() => FifoTaskScheduler.Default.QueueTaskDirect(null!));
        }
        [TestMethod]
        public void Properties()
        {
            Assert.IsGreaterThanOrEqualTo(0, FifoTaskScheduler.Default.Workers);
            Assert.IsGreaterThanOrEqualTo(0, FifoTaskScheduler.Default.BusyWorkers);
            Assert.IsGreaterThanOrEqualTo(Environment.ProcessorCount, FifoTaskScheduler.Default.MaximumConcurrencyLevel);
        }
        [TestMethod]
        public void IntrusiveSinglyLinkedList()
        {
            InterlockedSinglyLinkedList<NodeTest> list1 = new();
            InterlockedSinglyLinkedList<NodeTest> list2 = new();
            NodeTest node = new() { Value = 1 };
            list1.Push(node);
            Assert.ThrowsExactly<ArgumentNullException>(() => list2.Push(null!));
            Assert.ThrowsExactly<InvalidOperationException>(() => list2.Push(node));
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
            FifoTaskScheduler? scheduler = null;
            try
            {
                scheduler = FifoTaskScheduler.Start(nameof(Worker));
                FifoWorker worker = FifoWorker.Start(scheduler, "1", ThreadPriority.Normal);  // the worker disposes of itself
                worker.Invoke(LongWait);
                Assert.IsTrue(worker.IsBusy);
                Assert.ThrowsExactly<InvalidOperationException>(() => worker.Invoke(LongWait));
                Assert.IsFalse(FifoWorker.IsWorkerInternalMethod(null));
                Assert.IsFalse(FifoWorker.IsWorkerInternalMethod(typeof(TestFifoTaskScheduler).GetMethod(nameof(Worker))));
                Assert.IsTrue(FifoWorker.IsWorkerInternalMethod(typeof(FifoWorker).GetMethod("Invoke", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)));
                Assert.IsTrue(FifoWorker.IsWorkerInternalMethod(typeof(FifoWorker).GetMethod("WorkerFunc", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)));
                worker.Stop();
                Assert.ThrowsExactly<InvalidOperationException>(() => worker.Invoke(null!));
            }
            finally
            {
                scheduler?.Dispose();
            }
            Assert.ThrowsExactly<ObjectDisposedException>(() => scheduler.Run(() => { }));
            Assert.ThrowsExactly<ObjectDisposedException>(() => scheduler.FireAndForget(() => { }));
            await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => scheduler.Run(() => new ValueTask()));
        }
        public void LongWait()
        {
            Thread.Sleep(5000);
        }
        [TestMethod]
        public void WorkerNullWork()
        {
            using FifoTaskScheduler scheduler = FifoTaskScheduler.Start(nameof(WorkerNullWork));
            FifoWorker worker = scheduler.CreateWorker();    // the worker disposes of itself
            worker.Invoke(null!);
        }
        private AsyncLocal<int> ali = new();
        [TestMethod]
        public async Task AsyncLocalFlow()
        {
            int testvalue = 48902343;
            ali.Value = testvalue;
            using FifoTaskScheduler scheduler = FifoTaskScheduler.Start(nameof(AsyncLocalFlow));
            TaskCompletionSource<bool> completion = new();
            await Task.Factory.StartNew(() =>
            {
                Assert.AreEqual(testvalue, ali.Value);
            }, CancellationToken.None, TaskCreationOptions.None, scheduler);
            await scheduler.Run(() => Assert.AreEqual(testvalue, ali.Value));
        }
        [TestMethod]
        public async Task ExceptionHandling1()
        {
            using FifoTaskScheduler scheduler = FifoTaskScheduler.Start(nameof(ExceptionHandling1));
            scheduler.FireAndForget(() => throw new ExpectedException());
            await Task.Delay(1000);
        }
        [TestMethod]
        public async Task ExceptionHandling2()
        {
            using FifoTaskScheduler scheduler = FifoTaskScheduler.Start(nameof(ExceptionHandling2));
            Task? t = null;
            try
            {
                t = ThrowExceptionAsyncTask();
                await t;
            }
            catch
            {
            }
            scheduler.QueueTaskDirect(t!);
            await Task.Delay(1000);
        }
        [TestMethod]
        public async Task ExceptionHandling4()
        {
            using FifoTaskScheduler scheduler = FifoTaskScheduler.Start(nameof(ExceptionHandling4));
            await ExpectExceptionTask(() => scheduler.Run(() => throw new ExpectedException()));
            await Task.Delay(1000);
        }
        [TestMethod]
        public async Task ExceptionHandling8()
        {
            using FifoTaskScheduler scheduler = FifoTaskScheduler.Start(nameof(ExceptionHandling8));
            await ExpectException(async () => await scheduler.Run(() => throw new ExpectedException()));
            await Task.Delay(1000);
        }
        [TestMethod]
        public async Task ExceptionHandling9()
        {
            using FifoTaskScheduler scheduler = FifoTaskScheduler.Start(nameof(ExceptionHandling9));
            await ExpectException(async () => await scheduler.TransferWork(ThrowExceptionAsync));
            await Task.Delay(1000);
        }
        [TestMethod]
        public async Task ExceptionHandling10()
        {
            using FifoTaskScheduler scheduler = FifoTaskScheduler.Start(nameof(ExceptionHandling10));
            await ExpectException(async () => await scheduler.TransferWork(ThrowExceptionAsyncType<int>));
            await Task.Delay(1000);
        }
        private async Task ThrowExceptionAsyncTask()
        {
            await Task.Delay(100);
            throw new ExpectedException();
        }
        private ValueTask ThrowExceptionAsync()
        {
            throw new ExpectedException();
        }
        private ValueTask<T> ThrowExceptionAsyncType<T>()
        {
            throw new ExpectedException();
        }
        private async ValueTask ExpectExceptionTask(Func<Task> t)
        {
            try
            {
                await t();
                throw new InvalidOperationException();
            }
            catch (ExpectedException)
            {
                // ok!
            }
        }
        private void ExpectException(Action a)
        {
            try
            {
                a();
                throw new InvalidOperationException();
            }
            catch (ExpectedException)
            {
                // ok!
            }
        }
        private async ValueTask ExpectException(Func<ValueTask> a)
        {
            try
            {
                await a();
                throw new InvalidOperationException();
            }
            catch (ExpectedException)
            {
                // ok!
            }
        }
        [TestMethod]
        public async Task StartNewAmbientThreadSchedulerSwitching()
        {
            using FifoTaskScheduler scheduler = FifoTaskScheduler.Start(nameof(StartNewAmbientThreadSchedulerSwitching));
            TaskCompletionSource<bool> completion = new();
            await Task.Factory.StartNew(() => VerifyTaskSchedulerRemainsCustom(), CancellationToken.None, TaskCreationOptions.None, scheduler).Unwrap();
        }
        [TestMethod]
        public async Task TransferWorkAmbientThreadSchedulerSwitching()
        {
            await FifoTaskScheduler.Default.TransferWork(async () => 
            {
                await VerifyTaskSchedulerRemainsCustom();
            });
        }

        private async Task VerifyTaskSchedulerRemainsCustom()
        {
            Assert.IsFalse(ReferenceEquals(TaskScheduler.Current, TaskScheduler.Default));
            await Task.Yield();
            Assert.IsFalse(ReferenceEquals(TaskScheduler.Current, TaskScheduler.Default));
            await Task.Delay(100);
            Assert.IsFalse(ReferenceEquals(TaskScheduler.Current, TaskScheduler.Default));
            await Task.Delay(100).ConfigureAwait(true);
            Assert.IsFalse(ReferenceEquals(TaskScheduler.Current, TaskScheduler.Default));

            // ... more arbitrary async processing

            Assert.IsFalse(ReferenceEquals(TaskScheduler.Current, TaskScheduler.Default));
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

        public async ValueTask DoMixedWorkAsync(CancellationToken cancel = default, bool verifyHPThread = true)
        {
            ulong hash = GetHash(_id);
            await Task.Yield();
            //string? threadName = Thread.CurrentThread.Name;

            if (verifyHPThread) Assert.AreEqual(typeof(FifoTaskScheduler), TaskScheduler.Current.GetType());
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
                if (verifyHPThread) Assert.AreEqual(typeof(FifoTaskScheduler), TaskScheduler.Current.GetType());
                Stopwatch mem = Stopwatch.StartNew();
                // use some memory
                int bytesPerLoop = (int)((hash >> 12) % (_fast ? 10UL : 1024UL));
                int loops = (int)((hash >> 22) % 1024);
                for (int memory = 0; memory < loops; ++memory)
                {
                    byte[] bytes = new byte[bytesPerLoop];
                }
                mem.Stop();
                if (verifyHPThread) Assert.AreEqual(typeof(FifoTaskScheduler), TaskScheduler.Current.GetType());
                Stopwatch io = Stopwatch.StartNew();
                // simulate I/O by blocking
                await Task.Delay((int)((hash >> 32) % (_fast ? 5UL : 500UL)), cancel);
                io.Stop();
                if (verifyHPThread) Assert.AreEqual(typeof(FifoTaskScheduler), TaskScheduler.Current.GetType());
            }
            if (verifyHPThread) Assert.AreEqual(typeof(FifoTaskScheduler), TaskScheduler.Current.GetType());
            //Debug.WriteLine($"Ran work {_id} on {threadName}!", "Work");
        }
        public async ValueTask DoDelayOnlyWorkAsync(CancellationToken cancel = default)
        {
            ulong hash = GetHash(_id);
            await Task.Yield();
            //string? threadName = Thread.CurrentThread.Name;

            Assert.AreEqual(typeof(FifoTaskScheduler), TaskScheduler.Current.GetType());
            Stopwatch s = Stopwatch.StartNew();
            for (int outer = 0; outer < (int)(hash % 256) && !cancel.IsCancellationRequested; ++outer)
            {
                Stopwatch io = Stopwatch.StartNew();
                // simulate I/O by blocking
                await Task.Delay((int)((hash >> 32) % (_fast ? 5UL : 500UL)), cancel);
                io.Stop();
                Assert.AreEqual(typeof(FifoTaskScheduler), TaskScheduler.Current.GetType());
            }
            Assert.AreEqual(typeof(FifoTaskScheduler), TaskScheduler.Current.GetType());
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
