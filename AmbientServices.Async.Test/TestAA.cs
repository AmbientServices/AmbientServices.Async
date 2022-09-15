using AmbientServices.Utilities;
using AmbientServices.Async;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AmbientServices.Async.Test
{
    [TestClass]
    public class TestAA
    {
        /// <summary>
        /// Performs basic tests on the <see cref="AA"/> class.
        /// </summary>
        [TestMethod]
        public void AA_Basic()
        {
            Assert.AreNotEqual(AA.MultithreadedContext, AA.SinglethreadedContext);
            int result = AA.RunTaskSync(() =>
            {
                Task<int> task = AA_BasicAsync();
                return task;
            });
        }
        /// <summary>
        /// Performs basic tests on the <see cref="AA"/> class.
        /// </summary>
        [TestMethod]
        public void AA_AlreadyRun()
        {
            AA.RunTaskSync(() => Task.CompletedTask);
        }
        /// <summary>
        /// Performs basic tests on the <see cref="AA"/> class.
        /// </summary>
        [TestMethod]
        public void AA_NotAlreadyRun()
        {
            AA.RunTaskSync(() => new Task(() => { }));
        }
        /// <summary>
        /// Performs basic tests on the <see cref="AA"/> class.
        /// </summary>
        [TestMethod, ExpectedException(typeof(ExpectedException))]
        public void AA_AggregateExceptionUnwrap()
        {
            AA.RunTaskSync(() => throw new AggregateException(new ExpectedException(nameof(AA_AggregateExceptionUnwrap))));
        }
        /// <summary>
        /// Performs basic tests on the <see cref="AA"/> class.
        /// </summary>
        [TestMethod, ExpectedException(typeof(ExpectedException))]
        public void AA_AggregateExceptionUnwrapWithTask()
        {
            AA.RunTaskSync(async () => { await Task.Delay(10); throw new AggregateException(new ExpectedException(nameof(AA_AggregateExceptionUnwrapWithTask))); });
        }
        /// <summary>
        /// Performs basic tests on the <see cref="AA"/> class.
        /// </summary>
        [TestMethod, ExpectedException(typeof(ExpectedException))]
        public void AA_AggregateExceptionUnwrapWithReturn()
        {
            Assert.AreNotEqual(AA.MultithreadedContext, AA.SinglethreadedContext);
            int result = AA.RunTaskSync(async () =>
            {
                await Task.Delay(10);
                return await AA_BasicAsyncThrow(new AggregateException(new ExpectedException(nameof(AA_AggregateExceptionUnwrapWithReturn))));
            });
        }
        /// <summary>
        /// Performs basic tests on the <see cref="AA"/> class.
        /// </summary>
        [TestMethod, ExpectedException(typeof(AggregateException))]
        public void AA_AggregateExceptionCantUnwrap()
        {
            AA.RunTaskSync(() => throw new AggregateException(new ExpectedException(nameof(AA_AggregateExceptionCantUnwrap)), new ExpectedException(nameof(AA_AggregateExceptionCantUnwrap))));
        }
        /// <summary>
        /// Performs basic tests on the <see cref="AA"/> class.
        /// </summary>
        [TestMethod, ExpectedException(typeof(AggregateException))]
        public void AA_AggregateExceptionCantUnwrapWithTask()
        {
            AA.RunTaskSync(async () => { await Task.Delay(10); throw new AggregateException(new ExpectedException(nameof(AA_AggregateExceptionCantUnwrapWithTask)), new ExpectedException(nameof(AA_AggregateExceptionCantUnwrapWithTask))); });
        }
        /// <summary>
        /// Performs basic tests on the <see cref="AA"/> class.
        /// </summary>
        [TestMethod, ExpectedException(typeof(AggregateException))]
        public void AA_AggregateExceptionCantUnwrapWithReturn()
        {
            Assert.AreNotEqual(AA.MultithreadedContext, AA.SinglethreadedContext);
            int result = AA.RunTaskSync(async () =>
            {
                await Task.Delay(10);
                return await AA_BasicAsyncThrow(new AggregateException(new ExpectedException(nameof(AA_AggregateExceptionUnwrapWithReturn)), new ExpectedException(nameof(AA_AggregateExceptionUnwrapWithReturn))));
            });
        }
        /// <summary>
        /// Performs basic tests on the <see cref="AA"/> class.
        /// </summary>
        [TestMethod]
        public void AA_SynchronizeVariations()
        {
            TimeSpan timeout = TimeSpan.FromSeconds(60);
            AA.RunTaskSync(() =>
            {
                Task task = AA_BasicAsyncPart2();
                return task;
            });
            AA.RunTaskSync(() => AA_BasicAsyncPart2(default));
            int result = AA.RunTaskSync(AA_BasicAsyncPart3);
            result = AA.RunTaskSync(() => AA_BasicAsyncPart4(default));
        }

        /// <summary>
        /// Performs basic tests on the <see cref="AA"/> class.
        /// </summary>
        [TestMethod]
        public void AA_Misc()
        {
            // try to create a copy
            System.Threading.SynchronizationContext context = SynchronousSynchronizationContext.Default.CreateCopy();
            // try to send a delegate (should execute synchronously)
            int set = 0;
            SynchronousSynchronizationContext.Default.Send(new SendOrPostCallback(o =>
            {
                set = 1;
            }),
                this);
            Assert.AreEqual(1, set);

            Assert.AreEqual(1, SynchronousTaskScheduler.Default.MaximumConcurrencyLevel);
        }

        private Task<int> AA_BasicAsyncThrow(Exception ex)
        {
            throw ex;
        }

        private async Task<int> AA_BasicAsync()
        {
            await AA_BasicAsyncPart1();
            await AA_BasicAsyncPart2();
            await AA_BasicAsyncWithLock();
            int result = await AA_BasicAsyncPart3();
            return result;
        }

        private async Task AA_BasicAsyncPart1()
        {
            await Task.Delay(200);
            await Task.Delay(50);
        }

        private async Task AA_BasicAsyncPart2(CancellationToken cancel = default)
        {
            Thread.Sleep(200);
            Thread.Sleep(50);
            await Task.Yield();
        }
        private async Task<int> AA_BasicAsyncPart3()
        {
            await Task.Delay(50);
            return 1378902;
        }
        private async Task<int> AA_BasicAsyncPart4(CancellationToken cancel = default)
        {
            await Task.Delay(50, cancel);
            return 1378902;
        }

        private async Task AA_BasicAsyncWithLock(CancellationToken cancel = default)
        {
            using SemaphoreSlim lock2 = new(1);
            await lock2.WaitAsync(1000, cancel);
            try
            {
                await Task.Delay(50);
                await Task.Yield();
            }
            finally
            {
                lock2.Release();
            }
        }

#if NETSTANDARD2_1 || NETCOREAPP3_1_OR_GREATER || NET5_0_OR_GREATER
        /// <summary>
        /// Performs basic tests on the <see cref="AA"/> class.
        /// </summary>
        [TestMethod]
        public void AA_Enumerable()
        {
            int count = 0;
            foreach (int i in AA.AsyncEnumerableToEnumerable(() => TestAsyncEnum(10, 100)))
            {
                ++count;
            }
            Assert.AreEqual(100, count);
        }
        private async IAsyncEnumerable<int> TestAsyncEnum(int delayMilliseconds, int limit, [EnumeratorCancellation] CancellationToken cancel = default)
        {
            for (int loop = 0; loop < limit; ++loop)
            {
                await Task.Delay(delayMilliseconds);
                yield return loop;
            }
        }
        /// <summary>
        /// Performs basic tests on the <see cref="AA"/> class.
        /// </summary>
        [TestMethod]
        public void AA_Synchronize()
        {
            Assert.AreNotEqual(AA.MultithreadedContext, AA.SinglethreadedContext);
            AA.RunTaskSync(AsyncTest);
        }
        private async Task AsyncTest()
        {
            await Task.Delay(10);
        }
#endif


        /// <summary>
        /// Performs basic tests on the <see cref="AA"/> class.
        /// </summary>
        [TestMethod]
        public void AA_SyncInAsyncInSyncVoid()
        {
            int mainThreadId = Thread.CurrentThread.ManagedThreadId;
            int threadId = Thread.CurrentThread.ManagedThreadId;
            for (int count = 0; count < 3; ++count)
            {
                AA.RunTaskSync(() => Loop1(mainThreadId));
                threadId = Thread.CurrentThread.ManagedThreadId;
                Assert.AreEqual(mainThreadId, Thread.CurrentThread.ManagedThreadId);
            }
        }
        private async Task Loop1(int mainThreadId)
        {
            Assert.IsTrue(AA.UsingSynchronousExecution);
            int threadId = Thread.CurrentThread.ManagedThreadId;
            for (int count = 0; count < 3; ++count)
            {
                AA.RunTaskSync(() => Loop2(mainThreadId, Thread.CurrentThread.ManagedThreadId));
                threadId = Thread.CurrentThread.ManagedThreadId;
                Assert.AreEqual(mainThreadId, threadId);
            }
            Assert.IsTrue(AA.UsingSynchronousExecution);
            for (int count = 0; count < 3; ++count)
            {
                await AA.RunTaskAsync(() => Loop2(mainThreadId, Thread.CurrentThread.ManagedThreadId));
                threadId = Thread.CurrentThread.ManagedThreadId;
                Assert.AreEqual(mainThreadId, threadId);    // RunAsync, unlike a native await, keeps us on the same thread even though the action runs on another thread
            }
            Assert.IsTrue(AA.UsingSynchronousExecution);
            for (int count = 0; count < 3; ++count)
            {
                await Loop2(mainThreadId, Thread.CurrentThread.ManagedThreadId);    // this native await on a non-specially-created function triggers real async execution and thus a thread switch, also resulting in a switch to a non-synchronous execution context
                threadId = Thread.CurrentThread.ManagedThreadId;
                Assert.IsFalse(AA.UsingSynchronousExecution);
                Assert.AreNotEqual(mainThreadId, threadId);
            }
        }
        private async Task Loop2(int mainThreadId, int mainThreadId2)
        {
            int threadId = Thread.CurrentThread.ManagedThreadId;
            Assert.AreEqual(threadId, mainThreadId2);
            for (int count = 0; count < 3; ++count)
            {
                await AA.RunTask(() => Task.Delay(10));
                threadId = Thread.CurrentThread.ManagedThreadId;
                if (AA.UsingSynchronousExecution)
                {
                    Assert.AreEqual(mainThreadId, threadId);
                    Assert.AreEqual(mainThreadId2, threadId);
                }
                else
                {
                    Assert.AreNotEqual(mainThreadId, threadId);
                    // when we're not using sync execution, although the main thread is unavailable to execute tasks (because we only call sync stuff from there),
                    // main thread 2 *is* available, so we could end up using that here, so we can't check be sure that we're not running on that
                }
            }
            for (int count = 0; count < 3; ++count)
            {
                await Task.Delay(10);
                threadId = Thread.CurrentThread.ManagedThreadId;
                if (AA.UsingSynchronousExecution)
                {
                    Assert.AreEqual(mainThreadId, threadId);
                    Assert.AreEqual(mainThreadId2, threadId);
                }
                else
                {
                    Assert.AreNotEqual(mainThreadId, threadId);
                    // when we're not using sync execution, although the main thread is unavailable to execute tasks (because we only call sync stuff from there),
                    // main thread 2 *is* available, so we could end up using that here, so we can't check be sure that we're not running on that
                }
            }
        }


        /// <summary>
        /// Performs basic tests on the <see cref="AA"/> class.
        /// </summary>
        [TestMethod]
        public void AA_SyncInAsyncInSyncWithReturn()
        {
            int mainThreadId = Thread.CurrentThread.ManagedThreadId;
            int threadId = Thread.CurrentThread.ManagedThreadId;
            for (int count = 0; count < 3; ++count)
            {
                int result = AA.RunTaskSync(() => LoopWithReturn1(mainThreadId));
                threadId = Thread.CurrentThread.ManagedThreadId;
                Assert.AreEqual(mainThreadId, Thread.CurrentThread.ManagedThreadId);
            }
        }
        private async Task<int> LoopWithReturn1(int mainThreadId)
        {
            Assert.IsTrue(AA.UsingSynchronousExecution);
            int threadId = Thread.CurrentThread.ManagedThreadId;
            for (int count = 0; count < 3; ++count)
            {
                int result = AA.RunTaskSync(() => LoopWithReturn2(mainThreadId, Thread.CurrentThread.ManagedThreadId));
                threadId = Thread.CurrentThread.ManagedThreadId;
                Assert.AreEqual(mainThreadId, threadId);
            }
            Assert.IsTrue(AA.UsingSynchronousExecution);
            for (int count = 0; count < 3; ++count)
            {
                int result = await AA.RunTaskAsync(() => LoopWithReturn2(mainThreadId, Thread.CurrentThread.ManagedThreadId));
                threadId = Thread.CurrentThread.ManagedThreadId;
                Assert.AreEqual(mainThreadId, threadId);    // RunAsync, unlike a native await, keeps us on the same thread even though the action runs on another thread
            }
            Assert.IsTrue(AA.UsingSynchronousExecution);
            for (int count = 0; count < 3; ++count)
            {
                int result = await LoopWithReturn2(mainThreadId, Thread.CurrentThread.ManagedThreadId);    // this native await on a non-specially-created function triggers real async execution and thus a thread switch, also resulting in a switch to a non-synchronous execution context
                threadId = Thread.CurrentThread.ManagedThreadId;
                Assert.IsFalse(AA.UsingSynchronousExecution);
                Assert.AreNotEqual(mainThreadId, threadId);
            }
            return 0;
        }
        private async Task<int> LoopWithReturn2(int mainThreadId, int mainThreadId2)
        {
            int threadId = Thread.CurrentThread.ManagedThreadId;
            Assert.AreEqual(threadId, mainThreadId2);
            for (int count = 0; count < 3; ++count)
            {
                int result = await AA.RunTask(() => Task.FromResult(0));
                threadId = Thread.CurrentThread.ManagedThreadId;
                if (AA.UsingSynchronousExecution)
                {
                    Assert.AreEqual(mainThreadId, threadId);
                    Assert.AreEqual(mainThreadId2, threadId);
                }
                else
                {
                    Assert.AreNotEqual(mainThreadId, threadId);
                    // when we're not using sync execution, although the main thread is unavailable to execute tasks (because we only call sync stuff from there),
                    // main thread 2 *is* available, so we could end up using that here, so we can't check be sure that we're not running on that
                }
            }
            for (int count = 0; count < 3; ++count)
            {
                await Task.Delay(10);
                threadId = Thread.CurrentThread.ManagedThreadId;
                if (AA.UsingSynchronousExecution)
                {
                    Assert.AreEqual(mainThreadId, threadId);
                    Assert.AreEqual(mainThreadId2, threadId);
                }
                else
                {
                    Assert.AreNotEqual(mainThreadId, threadId);
                    // when we're not using sync execution, although the main thread is unavailable to execute tasks (because we only call sync stuff from there),
                    // main thread 2 *is* available, so we could end up using that here, so we can't check be sure that we're not running on that
                }
            }
            return 0;
        }


        /// <summary>
        /// Performs basic tests on the <see cref="AA"/> class.
        /// </summary>
        [TestMethod]
        public void AA_ValueTaskSyncInAsyncInSyncVoid()
        {
            int mainThreadId = Thread.CurrentThread.ManagedThreadId;
            int threadId = Thread.CurrentThread.ManagedThreadId;
            for (int count = 0; count < 3; ++count)
            {
                AA.RunSync(() => ValueTaskLoop1(mainThreadId));
                threadId = Thread.CurrentThread.ManagedThreadId;
                Assert.AreEqual(mainThreadId, Thread.CurrentThread.ManagedThreadId);
            }
        }
        private async ValueTask ValueTaskLoop1(int mainThreadId)
        {
            Assert.IsTrue(AA.UsingSynchronousExecution);
            int threadId = Thread.CurrentThread.ManagedThreadId;
            for (int count = 0; count < 3; ++count)
            {
                AA.RunSync(() => ValueTaskLoop2(mainThreadId, Thread.CurrentThread.ManagedThreadId));
                threadId = Thread.CurrentThread.ManagedThreadId;
                Assert.AreEqual(mainThreadId, threadId);
            }
            Assert.IsTrue(AA.UsingSynchronousExecution);
            for (int count = 0; count < 3; ++count)
            {
                await AA.RunAsync(() => ValueTaskLoop2(mainThreadId, Thread.CurrentThread.ManagedThreadId));
                threadId = Thread.CurrentThread.ManagedThreadId;
                Assert.AreEqual(mainThreadId, threadId);    // RunAsync, unlike a native await, keeps us on the same thread even though the action runs on another thread
            }
            Assert.IsTrue(AA.UsingSynchronousExecution);
            for (int count = 0; count < 3; ++count)
            {
                await ValueTaskLoop2(mainThreadId, Thread.CurrentThread.ManagedThreadId);    // this native await on a non-specially-created function triggers real async execution and thus a thread switch, also resulting in a switch to a non-synchronous execution context
                threadId = Thread.CurrentThread.ManagedThreadId;
                Assert.IsFalse(AA.UsingSynchronousExecution);
                Assert.AreNotEqual(mainThreadId, threadId);
            }
        }
        private async ValueTask ValueTaskLoop2(int mainThreadId, int mainThreadId2)
        {
            int threadId = Thread.CurrentThread.ManagedThreadId;
            Assert.AreEqual(threadId, mainThreadId2);
            for (int count = 0; count < 3; ++count)
            {
                await AA.RunTask(() => Task.Delay(10));
                threadId = Thread.CurrentThread.ManagedThreadId;
                if (AA.UsingSynchronousExecution)
                {
                    Assert.AreEqual(mainThreadId, threadId);
                    Assert.AreEqual(mainThreadId2, threadId);
                }
                else
                {
                    Assert.AreNotEqual(mainThreadId, threadId);
                    // when we're not using sync execution, although the main thread is unavailable to execute tasks (because we only call sync stuff from there),
                    // main thread 2 *is* available, so we could end up using that here, so we can't check be sure that we're not running on that
                }
            }
            for (int count = 0; count < 3; ++count)
            {
                await Task.Delay(10);
                threadId = Thread.CurrentThread.ManagedThreadId;
                if (AA.UsingSynchronousExecution)
                {
                    Assert.AreEqual(mainThreadId, threadId);
                    Assert.AreEqual(mainThreadId2, threadId);
                }
                else
                {
                    Assert.AreNotEqual(mainThreadId, threadId);
                    // when we're not using sync execution, although the main thread is unavailable to execute tasks (because we only call sync stuff from there),
                    // main thread 2 *is* available, so we could end up using that here, so we can't check be sure that we're not running on that
                }
            }
        }


        /// <summary>
        /// Performs basic tests on the <see cref="AA"/> class.
        /// </summary>
        [TestMethod]
        public void AA_ValueTaskSyncInAsyncInSyncWithReturn()
        {
            int mainThreadId = Thread.CurrentThread.ManagedThreadId;
            int threadId = Thread.CurrentThread.ManagedThreadId;
            for (int count = 0; count < 3; ++count)
            {
                int result = AA.RunSync(() => ValueTaskLoopWithReturn1(mainThreadId));
                threadId = Thread.CurrentThread.ManagedThreadId;
                Assert.AreEqual(mainThreadId, Thread.CurrentThread.ManagedThreadId);
            }
        }
        private async ValueTask<int> ValueTaskLoopWithReturn1(int mainThreadId)
        {
            Assert.IsTrue(AA.UsingSynchronousExecution);
            int threadId = Thread.CurrentThread.ManagedThreadId;
            for (int count = 0; count < 3; ++count)
            {
                int result = AA.RunSync(() => ValueTaskLoopWithReturn2(mainThreadId, Thread.CurrentThread.ManagedThreadId));
                threadId = Thread.CurrentThread.ManagedThreadId;
                Assert.AreEqual(mainThreadId, threadId);
            }
            Assert.IsTrue(AA.UsingSynchronousExecution);
            for (int count = 0; count < 3; ++count)
            {
                int result = await AA.RunAsync(() => ValueTaskLoopWithReturn2(mainThreadId, Thread.CurrentThread.ManagedThreadId));
                threadId = Thread.CurrentThread.ManagedThreadId;
                Assert.AreEqual(mainThreadId, threadId);    // RunAsync, unlike a native await, keeps us on the same thread even though the action runs on another thread
            }
            Assert.IsTrue(AA.UsingSynchronousExecution);
            for (int count = 0; count < 3; ++count)
            {
                int result = await ValueTaskLoopWithReturn2(mainThreadId, Thread.CurrentThread.ManagedThreadId);    // this native await on a non-specially-created function triggers real async execution and thus a thread switch, also resulting in a switch to a non-synchronous execution context
                threadId = Thread.CurrentThread.ManagedThreadId;
                Assert.IsFalse(AA.UsingSynchronousExecution);
                Assert.AreNotEqual(mainThreadId, threadId);
            }
            return 0;
        }
        private async ValueTask<int> ValueTaskLoopWithReturn2(int mainThreadId, int mainThreadId2)
        {
            int threadId = Thread.CurrentThread.ManagedThreadId;
            Assert.AreEqual(threadId, mainThreadId2);
            for (int count = 0; count < 3; ++count)
            {
                int result = await AA.Run(() => new ValueTask<int>(0));
                threadId = Thread.CurrentThread.ManagedThreadId;
                if (AA.UsingSynchronousExecution)
                {
                    Assert.AreEqual(mainThreadId, threadId);
                    Assert.AreEqual(mainThreadId2, threadId);
                }
                else
                {
                    Assert.AreNotEqual(mainThreadId, threadId);
                    // when we're not using sync execution, although the main thread is unavailable to execute tasks (because we only call sync stuff from there),
                    // main thread 2 *is* available, so we could end up using that here, so we can't check to be sure that we're not running on that
                }
            }
            for (int count = 0; count < 3; ++count)
            {
                await AA.Run(() => default);
                threadId = Thread.CurrentThread.ManagedThreadId;
                if (AA.UsingSynchronousExecution)
                {
                    Assert.AreEqual(mainThreadId, threadId);
                    Assert.AreEqual(mainThreadId2, threadId);
                }
                else
                {
                    Assert.AreNotEqual(mainThreadId, threadId);
                    // when we're not using sync execution, although the main thread is unavailable to execute tasks (because we only call sync stuff from there),
                    // main thread 2 *is* available, so we could end up using that here, so we can't check to be sure that we're not running on that
                }
            }
            for (int count = 0; count < 3; ++count)
            {
                await Task.Delay(10);
                threadId = Thread.CurrentThread.ManagedThreadId;
                if (AA.UsingSynchronousExecution)
                {
                    Assert.AreEqual(mainThreadId, threadId);
                    Assert.AreEqual(mainThreadId2, threadId);
                }
                else
                {
                    Assert.AreNotEqual(mainThreadId, threadId);
                    // when we're not using sync execution, although the main thread is unavailable to execute tasks (because we only call sync stuff from there),
                    // main thread 2 *is* available, so we could end up using that here, so we can't check to be sure that we're not running on that
                }
            }
            return 0;
        }
        /// <summary>
        /// Performs basic tests on the <see cref="AA"/> class.
        /// </summary>
        [TestMethod]
        public void Sync_Enumerator()
        {
            int count = 0;
            foreach (int ret in 1.ToSingleItemEnumerable())
            {
                Assert.AreEqual(1, ++count);
                Assert.AreEqual(1, ret);
            }
        }
#if NETSTANDARD2_1 || NETCOREAPP3_1_OR_GREATER || NET5_0_OR_GREATER
        /// <summary>
        /// Performs basic tests on the <see cref="AA"/> class.
        /// </summary>
        [TestMethod]
        public void Synchronized_AsyncEnumerator()
        {
            int mainThreadId = Thread.CurrentThread.ManagedThreadId;
            int threadId = Thread.CurrentThread.ManagedThreadId;
            foreach (int ret in AA.AsyncEnumerableToEnumerable(() => EnumerateAsync(10, default)))
            {
                Assert.AreEqual(mainThreadId, Thread.CurrentThread.ManagedThreadId);
            }
        }
        /// <summary>
        /// Performs basic tests on the <see cref="AA"/> class.
        /// </summary>
        [TestMethod]
        public void Sync_AsyncEnumerator()
        {
            int mainThreadId = Thread.CurrentThread.ManagedThreadId;
            int threadId = Thread.CurrentThread.ManagedThreadId;
            AA.RunTaskSync(async () =>
            {
                await foreach (int ret in EnumerateAsync(10, default))
                {
                    Assert.AreEqual(mainThreadId, Thread.CurrentThread.ManagedThreadId);
                }
            });
            AA.RunSync(() => AA.AwaitForEach(EnumerateAsync(10, default), t =>
            {
                Assert.AreEqual(mainThreadId, Thread.CurrentThread.ManagedThreadId);
            }, default));
            AA.RunSync(() => AA.AwaitForEach(EnumerateAsync(10, default), (t, c) =>
            {
                Assert.AreEqual(mainThreadId, Thread.CurrentThread.ManagedThreadId);
                return default;
            }, default));
            AA.RunTaskSync(async () =>
            {
                foreach (int ret in await EnumerateAsync(10, default).ToListAsync())
                {
                    Assert.AreEqual(mainThreadId, Thread.CurrentThread.ManagedThreadId);
                }
            });
            AA.RunTaskSync(async () =>
            {
                foreach (int ret in await EnumerateAsync(10, default).GetAsyncEnumerator().ToListAsync())
                {
                    Assert.AreEqual(mainThreadId, Thread.CurrentThread.ManagedThreadId);
                }
            });
            AA.RunTaskSync(async () =>
            {
                foreach (int ret in await EnumerateAsync(10, default).ToArrayAsync())
                {
                    Assert.AreEqual(mainThreadId, Thread.CurrentThread.ManagedThreadId);
                }
            });
            AA.RunTaskSync(async () =>
            {
                foreach (int ret in await EnumerateAsync(10, default).GetAsyncEnumerator().ToArrayAsync())
                {
                    Assert.AreEqual(mainThreadId, Thread.CurrentThread.ManagedThreadId);
                }
            });
            AA.RunTaskSync(async () =>
            {
                foreach (int ret in await EnumerateAsync(10, default).ToEnumerableAsync())
                {
                    Assert.AreEqual(mainThreadId, Thread.CurrentThread.ManagedThreadId);
                }
            });
            AA.RunTaskSync(async () =>
            {
                foreach (int ret in await EnumerateAsync(10, default).GetAsyncEnumerator().ToEnumerableAsync())
                {
                    Assert.AreEqual(mainThreadId, Thread.CurrentThread.ManagedThreadId);
                }
            });
            AA.RunTaskSync(async () =>
            {
                await foreach (int ret in new int[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }.ToAsyncEnumerable())
                {
                    Assert.AreEqual(mainThreadId, Thread.CurrentThread.ManagedThreadId);
                }
            });
            AA.RunTaskSync(async () =>
            {
                await foreach (int ret in ((IEnumerable<int>)new int[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }).GetEnumerator().ToAsyncEnumerable())
                {
                    Assert.AreEqual(mainThreadId, Thread.CurrentThread.ManagedThreadId);
                }
            });
            AA.RunTaskSync(async () =>
            {
                await foreach (int ret in 1.ToSingleItemAsyncEnumerable())
                {
                    Assert.AreEqual(mainThreadId, Thread.CurrentThread.ManagedThreadId);
                }
            });
            AA.RunTaskSync(async () =>
            {
                await foreach (int ret in Array.Empty<int>().ToAsyncEnumerable())
                {
                    Assert.Fail("There should be no entries!");
                }
            });
            AA.RunTaskSync(async () =>
            {
                await foreach (int ret in ((IEnumerable<int>)Array.Empty<int>()).GetEnumerator().ToAsyncEnumerable())
                {
                    Assert.Fail("There should be no entries!");
                }
            });
            foreach (int ret in AA.AsyncEnumerableToEnumerable(() => EnumerateAsync(10, default)))
            {
                Assert.AreEqual(mainThreadId, Thread.CurrentThread.ManagedThreadId);
            }
        }
        /// <summary>
        /// Performs basic tests on the <see cref="AA"/> class.
        /// </summary>
        [TestMethod]
        public async Task AA_AsyncEnumerator()
        {
            int mainThreadId = Thread.CurrentThread.ManagedThreadId;
            int threadId = Thread.CurrentThread.ManagedThreadId;
            int limit = 10;
            int max = 0;
            await foreach (int ret in EnumerateAsync(10, default))
            {
                max = Math.Max(max, ret);
            }
            Assert.AreEqual(limit - 1, max);
            max = 0;
            await AA.Run(() => AA.AwaitForEach(EnumerateAsync(limit, default), ret =>
            {
                max = Math.Max(max, ret);
            }, default));
            Assert.AreEqual(limit - 1, max);
            max = 0;
            await AA.Run(() => AA.AwaitForEach(EnumerateAsync(limit, default), (ret, c) =>
            {
                max = Math.Max(max, ret);
                return default;
            }, default));
            Assert.AreEqual(limit - 1, max);
            max = 0;
            foreach (int ret in await EnumerateAsync(limit, default).ToListAsync())
            {
                max = Math.Max(max, ret);
            }
            Assert.AreEqual(limit - 1, max);
            max = 0;
            foreach (int ret in await EnumerateAsync(limit, default).GetAsyncEnumerator().ToListAsync())
            {
                max = Math.Max(max, ret);
            }
            Assert.AreEqual(limit - 1, max);
            max = 0;
            foreach (int ret in await EnumerateAsync(limit, default).ToArrayAsync())
            {
                max = Math.Max(max, ret);
            }
            Assert.AreEqual(limit - 1, max);
            max = 0;
            foreach (int ret in await EnumerateAsync(limit, default).GetAsyncEnumerator().ToArrayAsync())
            {
                max = Math.Max(max, ret);
            }
            Assert.AreEqual(limit - 1, max);
            max = 0;
            foreach (int ret in await EnumerateAsync(limit, default).ToEnumerableAsync())
            {
                max = Math.Max(max, ret);
            }
            Assert.AreEqual(limit - 1, max);
            max = 0;
            foreach (int ret in await EnumerateAsync(limit, default).GetAsyncEnumerator().ToEnumerableAsync())
            {
                max = Math.Max(max, ret);
            }
            Assert.AreEqual(limit - 1, max);
            max = 0;
            foreach (int ret in AA.AsyncEnumerableToEnumerable(() => EnumerateAsync(limit, default)))
            {
                max = Math.Max(max, ret);
            }
            Assert.AreEqual(limit - 1, max);
        }
        private async IAsyncEnumerable<int> EnumerateAsync(int limit, [EnumeratorCancellation] CancellationToken cancel = default)
        {
            for (int ret = 0; ret < limit; ++ret)
            {
                cancel.ThrowIfCancellationRequested();
                await AA.RunTask(() => Task.Delay(10));
                yield return ret;
            }
        }

        /// <summary>
        /// Performs basic tests on the <see cref="AA"/> class.
        /// </summary>
        [TestMethod]
        public async Task AA_AsyncEnumeratorExceptions()
        {
            int mainThreadId = Thread.CurrentThread.ManagedThreadId;
            await Assert.ThrowsExceptionAsync<ArgumentNullException>(async () =>
            {
                foreach (int ret in await NullAsyncEnumerable().ToListAsync())
                {
                    Assert.AreEqual(mainThreadId, Thread.CurrentThread.ManagedThreadId);
                }
            });
            await Assert.ThrowsExceptionAsync<ArgumentNullException>(async () =>
            {
                await foreach (int ret in ((int[])null!).ToAsyncEnumerable())
                {
                    Assert.Fail("Should be empty!");
                }
            });
            await Assert.ThrowsExceptionAsync<ArgumentNullException>(async () =>
            {
                await foreach (int ret in ((IEnumerator<int>)null!).ToAsyncEnumerable())
                {
                    Assert.Fail("Should be empty!");
                }
            });
        }
        private IAsyncEnumerable<int> NullAsyncEnumerable()
        {
            return null!;
        }
        /// <summary>
        /// Performs basic tests on the <see cref="AA"/> class.
        /// </summary>
        [TestMethod]
        public void AA_InfiniteEnumeratorAsyncToSync()
        {
            foreach (int ret in AA.AsyncEnumerableToEnumerable(() => InfiniteEnumerateAsync(default)))
            {
                if (ret >= 10) break;
            }
        }
        private async IAsyncEnumerable<int> InfiniteEnumerateAsync([EnumeratorCancellation] CancellationToken cancel = default)
        {
            int ret = 0;
            while (true)
            {
                await AA.RunTask(() => Task.Delay(10));
                yield return ++ret;
            }
        }
#endif
        [TestMethod]
        public void SynchronousSynchronizationContextExceptions()
        {
            SynchronousSynchronizationContext c = SynchronousSynchronizationContext.Default;
            Assert.ThrowsException<ArgumentNullException>(() => c.Send(null!, null));
            Assert.ThrowsException<ArgumentNullException>(() => c.Post(null!, null));
        }
        [TestMethod]
        public void AA_ArgumentNullExceptions()
        {
            Assert.ThrowsException<ArgumentNullException>(() => AA.ConvertAggregateException(null!));
            Assert.ThrowsException<ArgumentNullException>(() =>
            {
                foreach (var x in AA.AsyncEnumerableToEnumerable<IAsyncEnumerable<int>>(null!))
                { 
                }
            });
        }
#if NETSTANDARD2_1 || NETCOREAPP3_1_OR_GREATER || NET5_0_OR_GREATER
        [TestMethod]
        public async Task AA_ArgumentNullExceptionsAsync()
        {
            await Assert.ThrowsExceptionAsync<ArgumentNullException>(async () => await AA.AwaitForEach<int>(null!, n => { }));
            await Assert.ThrowsExceptionAsync<ArgumentNullException>(async () => await AA.AwaitForEach<int>(1.ToSingleItemAsyncEnumerable(), (Action<int>)null!));
            await Assert.ThrowsExceptionAsync<ArgumentNullException>(async () => await AA.AwaitForEach<int>(null!, (i, c) => new ValueTask()));
            await Assert.ThrowsExceptionAsync<ArgumentNullException>(async () => await AA.AwaitForEach<int>(1.ToSingleItemAsyncEnumerable(), null!));
        }
#endif
        [TestMethod]
        public void FilterAsyncFromStackTrace()
        {
            FilteredStackTrace trace;
            AA.RunTaskSync(async () =>
            {
                string? projectPath = AssemblyUtilities.GetCallingCodeSourceFolder(1, 1)?.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
                FilteredStackTrace.AddSourcePathToErase(projectPath ?? "Z:\\");
                FilteredStackTrace.AddNamespaceToFilter("Async.Synchronize");
                trace = new FilteredStackTrace(new ExpectedException("This is a test"), 0, true);
                Assert.IsTrue(string.IsNullOrEmpty(trace.ToString().Trim()));
                await Task.CompletedTask;
            });
        }
    }
}
