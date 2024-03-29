﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace AmbientServices
{
    /// <summary>
    /// A static class to hold utility functions for calling async functions in a non-async context or vice versa, especially during the transition of a code base to async/await.
    /// When migrating code from sync to async, begin from the bottom off the call stack.
    /// Use <see cref="RunSync(Func{ValueTask})"/> or <see cref="RunTaskSync(Func{Task})"/> at the transition from sync to async, forcing the task to run in a synchronous ambient context.
    /// Use await <see cref="Run(Func{ValueTask})"/> or <see cref="RunTask(Func{Task})"/> as the default asynchronous invocation, which will run synchronously in a synchronous ambient context, and asynchronously in an asynchronous ambient context.
    /// Use await <see cref="RunAsync(Func{ValueTask},SynchronizationContext?)"/> or <see cref="RunTaskAsync(Func{Task},SynchronizationContext?)"/> to force asynchronous execution within a synchronous ambient context (even within <see cref="RunSync(Func{ValueTask})"/>).
    /// As migration progresses, calls to <see cref="RunSync(Func{ValueTask})"/> and <see cref="RunTaskSync(Func{Task})"/> move up the call stack, being gradually replaced by calls to <see cref="Run(Func{ValueTask})"/> or <see cref="RunTask(Func{Task})"/>.
    /// Calls that use await without one of these as the target will run asynchonously in a newly spawned async ambient context.
    /// </summary>
    public static class Async
    {
        private static SynchronizationContext sDefaultAsyncContext = new();

        /// <summary>
        /// Gets or sets the default async context, which will be used when forcing tasks to the async context from within a synchronous context for example when calling <see cref="RunAsync"/> without specifying an explicit context.
        /// </summary>
        public static SynchronizationContext DefaultAsyncContext
        {
            get { return sDefaultAsyncContext; }
            set { Interlocked.Exchange(ref sDefaultAsyncContext, value); }
        }
        /// <summary>
        /// Gets the single threaded context to use for spawning tasks to be run on the current thread.
        /// </summary>
        public static System.Threading.SynchronizationContext SinglethreadedContext => SynchronousSynchronizationContext.Default;

        /// <summary>
        /// Gets whether or not the current context is using synchronous execution.
        /// </summary>
        public static bool UsingSynchronousExecution => SynchronizationContext.Current == SynchronousSynchronizationContext.Default;

        private static void RunIfNeeded(Task task)
        {
            if (task.Status == TaskStatus.Created)
            {
                task.RunSynchronously(SynchronousTaskScheduler.Default);
            }
        }
        /// <summary>
        /// Converts the specified <see cref="AggregateException"/> into the inner exception type if there is only one inner exception.
        /// If there is more than one inner exception, just returns.
        /// </summary>
        /// <param name="ex">The <see cref="AggregateException"/></param>
        /// <exception cref="ArgumentNullException">If <paramref name="ex"/> is null.</exception>
        public static void ConvertAggregateException(AggregateException ex)
        {
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(ex);
#else
            if (ex == null) throw new ArgumentNullException(nameof(ex));
#endif
            if (ex.InnerExceptions.Count < 2)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerExceptions[0]).Throw();
            }
        }

        private static void WaitAndUnwrapException(this Task t, bool continueOnCapturedContext)
        {
            try
            {
                t.ConfigureAwait(continueOnCapturedContext).GetAwaiter().GetResult();
            }
            catch (AggregateException ex)
            {
                ConvertAggregateException(ex);
                throw;
            }
        }
        private static T WaitAndUnwrapException<T>(this Task<T> t, bool continueOnCapturedContext)
        {
            try
            {
                return t.ConfigureAwait(continueOnCapturedContext).GetAwaiter().GetResult();
            }
            catch (AggregateException ex)
            {
                ConvertAggregateException(ex);
                throw;
            }
        }
        // NOTE that the following are not currently needed because we can't force a ValueTask to run synchronously, and after it's done running, we can't call GetResult() on it because we would have already gotten the results
        //        private static void WaitAndUnwrapException(this ValueTask t, bool continueOnCapturedContext)
        //        private static T WaitAndUnwrapException<T>(this ValueTask<T> t, bool continueOnCapturedContext)

        private static void RunInTemporaryContextWithExceptionConversion(SynchronizationContext newContext, Action a)
        {
            System.Threading.SynchronizationContext? oldContext = SynchronizationContext.Current;
            try
            {
                SynchronizationContext.SetSynchronizationContext(newContext);
                a();
            }
            catch (AggregateException ex)
            {
                ConvertAggregateException(ex);
                throw;
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(oldContext);
            }
        }
        private static T RunInTemporaryContextWithExceptionConversion<T>(SynchronizationContext newContext, Func<T> a)
        {
            System.Threading.SynchronizationContext? oldContext = SynchronizationContext.Current;
            try
            {
                SynchronizationContext.SetSynchronizationContext(newContext);
                return a();
            }
            catch (AggregateException ex)
            {
                ConvertAggregateException(ex);
                throw;
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(oldContext);
            }
        }

        /// <summary>
        /// Runs the specified asynchronous action using the currently ambient mode.
        /// </summary>
        /// <param name="a">The asynchronous action to run.</param>
        [DebuggerStepThrough]
        public static Task RunTask(Func<Task> a)
        {
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(a);
#else
            if (a is null) throw new ArgumentNullException(nameof(a));
#endif
            if (SynchronizationContext.Current == SynchronousSynchronizationContext.Default)
            {
                RunTaskSync(a);
                return Task.CompletedTask;
            }
            else
            {
                return a();
            }
        }
        /// <summary>
        /// Runs the specified asynchronous action using the currently ambient mode.
        /// </summary>
        /// <param name="a">The cancelable asynchronous action to run.</param>
        [DebuggerStepThrough]
        public static Task<T> RunTask<T>(Func<Task<T>> a)
        {
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(a);
#else
            if (a is null) throw new ArgumentNullException(nameof(a));
#endif
            if (SynchronizationContext.Current == SynchronousSynchronizationContext.Default)
            {
                T t = RunTaskSync(a);
                return Task.FromResult(t);
            }
            else
            {
                return a();
            }
        }
        /// <summary>
        /// Runs the specified asynchronous action using the currently ambient mode.
        /// </summary>
        /// <param name="a">The cancelable asynchronous action to run.</param>
        [DebuggerStepThrough]
        public static ValueTask Run(Func<ValueTask> a)
        {
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(a);
#else
            if (a is null) throw new ArgumentNullException(nameof(a));
#endif
            if (SynchronizationContext.Current == SynchronousSynchronizationContext.Default)
            {
                RunSync(a);
                return default;
            }
            else
            {
                return a();
            }
        }
        /// <summary>
        /// Runs the specified asynchronous action using the currently ambient mode.
        /// </summary>
        /// <param name="a">The cancelable asynchronous action to run.</param>
        [DebuggerStepThrough]
        public static ValueTask<T> Run<T>(Func<ValueTask<T>> a)
        {
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(a);
#else
            if (a is null) throw new ArgumentNullException(nameof(a));
#endif
            if (SynchronizationContext.Current == SynchronousSynchronizationContext.Default)
            {
                T t = RunSync(a);
                return new ValueTask<T>(t);
            }
            else
            {
                return a();
            }
        }
        /// <summary>
        /// Runs the specified asynchronous action asynchronously and switches the ambient synchronization context to the asynchronous one during the operation.
        /// Use this to run the action in an asynchronous ambient context, but wait on the current thread for it to finish.
        /// </summary>
        /// <param name="a">The asynchronous action to run.</param>
        /// <param name="context">The <see cref="SynchronizationContext"/> to use to asynchronously run the task, or null to use <see cref="DefaultAsyncContext"/>.</param>
        [DebuggerStepThrough]
        public static Task RunTaskAsync(Func<Task> a, SynchronizationContext? context = null)
        {
            return RunInTemporaryContextWithExceptionConversion(context ?? sDefaultAsyncContext, () =>
            {
                Task task = Task.Run(a);       // this should run the task on another thread and should be started using the ambient synchronization context, which is now the async one
                task.WaitAndUnwrapException(false);
                return task;
            });
        }
        /// <summary>
        /// Runs the specified asynchronous action asynchronously and switches the ambient synchronization context to the asynchronous one during the operation.
        /// Use this to run the action in an asynchronous ambient context, but wait on the current thread for it to finish.
        /// </summary>
        /// <param name="a">The cancelable asynchronous action to run.</param>
        /// <param name="context">The <see cref="SynchronizationContext"/> to use to asynchronously run the task, or null to use <see cref="DefaultAsyncContext"/>.</param>
        [DebuggerStepThrough]
        public static Task<T> RunTaskAsync<T>(Func<Task<T>> a, SynchronizationContext? context = null)
        {
            return RunInTemporaryContextWithExceptionConversion(context ?? sDefaultAsyncContext, () =>
            {
                Task<T> task = Task.Run(a);       // this should run the task on another thread and should be started using the ambient synchronization context, which is now the async one
                task.WaitAndUnwrapException(false);
                return task;
            });
        }
        /// <summary>
        /// Runs the specified asynchronous action asynchronously and switches the ambient synchronization context to the asynchronous one during the operation.
        /// Use this to run the action in an asynchronous ambient context, but wait on the current thread for it to finish.
        /// </summary>
        /// <param name="a">The cancelable asynchronous action to run.</param>
        /// <param name="context">The <see cref="SynchronizationContext"/> to use to asynchronously run the task, or null to use <see cref="DefaultAsyncContext"/>.</param>
        [DebuggerStepThrough]
        public static ValueTask RunAsync(Func<ValueTask> a, SynchronizationContext? context = null)
        {
            return RunInTemporaryContextWithExceptionConversion(context ?? sDefaultAsyncContext, () =>
            {
                Task task = Task.Run(() => a().AsTask());           // I'm not seeing a way to do this without this conversion (which negates the optimization provided by ValueTask)
                task.WaitAndUnwrapException(false);
                return new ValueTask();
            });
        }
        /// <summary>
        /// Runs the specified asynchronous action asynchronously and switches the ambient synchronization context to the asynchronous one during the operation.
        /// Use this to run the action in an asynchronous ambient context, but wait on the current thread for it to finish.
        /// </summary>
        /// <param name="a">The cancelable asynchronous action to run.</param>
        /// <param name="context">The <see cref="SynchronizationContext"/> to use to asynchronously run the task, or null to use <see cref="DefaultAsyncContext"/>.</param>
        [DebuggerStepThrough]
        public static ValueTask<T> RunAsync<T>(Func<ValueTask<T>> a, SynchronizationContext? context = null)
        {
            return RunInTemporaryContextWithExceptionConversion(context ?? sDefaultAsyncContext, () =>
            {
                Task<T> task = Task.Run(() => a().AsTask());        // I'm not seeing a way to do this without this conversion (which negates the optimization provided by ValueTask FWIW)
                T result = task.WaitAndUnwrapException(false);
                return new ValueTask<T>(result);
            });
        }

        /// <summary>
        /// Runs the specified asynchronous action synchronously on the current thread, staying on the current thread.
        /// </summary>
        /// <param name="a">The action to run.</param>
        [DebuggerStepThrough]
        public static void RunTaskSync(Func<Task> a)
        {
            RunInTemporaryContextWithExceptionConversion(SynchronousSynchronizationContext.Default, () =>
            {
                using Task task = a();
                RunIfNeeded(task);
                task.WaitAndUnwrapException(true);
            });
        }
        /// <summary>
        /// Runs the specified asynchronous action synchronously on the current thread, staying on the current thread.
        /// </summary>
        /// <param name="a">The cancelable asynchronous action to run.</param>
        [DebuggerStepThrough]
        public static T RunTaskSync<T>(Func<Task<T>> a)
        {
            return RunInTemporaryContextWithExceptionConversion(SynchronousSynchronizationContext.Default, () =>
            {
                using Task<T> task = a();
                RunIfNeeded(task);
                return task.WaitAndUnwrapException(true);
            });
        }


        /// <summary>
        /// Runs the specified asynchronous action synchronously on the current thread, staying on the current thread.
        /// </summary>
        /// <param name="a">The cancelable asynchronous action to run.</param>
        [DebuggerStepThrough]
        public static T RunSync<T>(Func<ValueTask<T>> a)
        {
            return RunInTemporaryContextWithExceptionConversion(SynchronousSynchronizationContext.Default, () =>
            {
                ValueTask<T> valueTask = a();
                Task<T> task = valueTask.AsTask();          // I'm not seeing a way to do this without this conversion (which negates the optimization provided by ValueTask FWIW)
                RunIfNeeded(task);
                return task.WaitAndUnwrapException(true);
            });
        }


        /// <summary>
        /// Runs the specified asynchronous action synchronously on the current thread, staying on the current thread.
        /// </summary>
        /// <param name="a">The cancelable asynchronous action to run.</param>
        [DebuggerStepThrough]
        public static void RunSync(Func<ValueTask> a)
        {
            RunInTemporaryContextWithExceptionConversion(SynchronousSynchronizationContext.Default, () =>
            {
                ValueTask valueTask = a();
                Task task = valueTask.AsTask();          // I'm not seeing a way to do this without this conversion (which negates the optimization provided by ValueTask)
                RunIfNeeded(task);
                task.WaitAndUnwrapException(true);
            });
        }
#if NETSTANDARD2_1 || NETCOREAPP3_1_OR_GREATER || NET5_0_OR_GREATER
        /// <summary>
        /// Synchronously converts an <see cref="IAsyncEnumerable{T}"/> into an <see cref="IEnumerable{T}"/>.
        /// Works with infinite collections.
        /// </summary>
        /// <typeparam name="T">The type being enumerated.</typeparam>
        /// <param name="funcAsyncEnumerable">A delegate that returns an <see cref="IAsyncEnumerable{T}"/></param>
        /// <param name="cancel">A <see cref="CancellationToken"/> which the caller can use to notify the executor to cancel the operation before it finishes.</param>
        /// <returns>The <see cref="IEnumerable{T}"/>.</returns>
        [DebuggerStepThrough]
        public static IEnumerable<T> AsyncEnumerableToEnumerable<T>(Func<IAsyncEnumerable<T>> funcAsyncEnumerable, CancellationToken cancel = default)
        {
System.Diagnostics.Debug.WriteLine("AsyncEnumerableToEnumerable");
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(funcAsyncEnumerable);
#else
            if (funcAsyncEnumerable == null) throw new ArgumentNullException(nameof(funcAsyncEnumerable));
#endif
System.Diagnostics.Debug.WriteLine("AsyncEnumerableToEnumerable funcAsyncEnumerable not null");
            IAsyncEnumerator<T> asyncEnum = funcAsyncEnumerable().GetAsyncEnumerator(cancel);
            try
            {
                while (RunSync(() => asyncEnum.MoveNextAsync()))
                {
                    cancel.ThrowIfCancellationRequested();
                    yield return asyncEnum.Current;
                }
            }
            finally
            {
                RunSync(() => asyncEnum.DisposeAsync());
            }
        }
        /// <summary>
        /// Iterates through the specified async enumerable using the ambient synchronicity and a synchronous delegate.
        /// </summary>
        /// <typeparam name="T">The type being enumerated.</typeparam>
        /// <param name="asyncEnumerable">The <see cref="IAsyncEnumerable{T}"/> to enumerate.</param>
        /// <param name="action">The action to perform on each enumerated item.</param>
        /// <param name="cancel">A <see cref="CancellationToken"/> that may be used to interrupt the enumeration.</param>
        /// <returns>A <see cref="ValueTask"/> for the iteration.</returns>
        /// <exception cref="ArgumentNullException">If <paramref name="asyncEnumerable"/> or <paramref name="action"/> are null.</exception>
        [DebuggerStepThrough]
        public static async ValueTask AwaitForEach<T>(IAsyncEnumerable<T> asyncEnumerable, Action<T> action, CancellationToken cancel = default)
        {
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(asyncEnumerable);
#else
            if (asyncEnumerable == null) throw new ArgumentNullException(nameof(asyncEnumerable));
#endif
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(action);
#else
            if (action == null) throw new ArgumentNullException(nameof(action));
#endif
            IAsyncEnumerator<T> e = asyncEnumerable.GetAsyncEnumerator(cancel);
            try
            {
                while (await Run(() => e.MoveNextAsync()))
                {
                    cancel.ThrowIfCancellationRequested();
                    action(e.Current);
                }
            }
            finally { if (e != null) await Run(() => e.DisposeAsync()); }
        }
        /// <summary>
        /// Iterates through the specified async enumerable using the ambient synchronicity and an asynchronous delegate.
        /// </summary>
        /// <typeparam name="T">The type being enumerated.</typeparam>
        /// <param name="asyncEnumerable">The <see cref="IAsyncEnumerable{T}"/> to enumerate.</param>
        /// <param name="func">The async action to perform on each enumerated item.</param>
        /// <param name="cancel">A <see cref="CancellationToken"/> that may be used to interrupt the enumeration.</param>
        /// <returns>A <see cref="ValueTask"/> for the iteration.</returns>
        /// <exception cref="ArgumentNullException">If <paramref name="asyncEnumerable"/> or <paramref name="func"/> are null.</exception>
        [DebuggerStepThrough]
        public static async ValueTask AwaitForEach<T>(IAsyncEnumerable<T> asyncEnumerable, Func<T, CancellationToken, ValueTask> func, CancellationToken cancel = default)
        {
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(asyncEnumerable);
#else
            if (asyncEnumerable == null) throw new ArgumentNullException(nameof(asyncEnumerable));
#endif
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(func);
#else
            if (func == null) throw new ArgumentNullException(nameof(func));
#endif
            IAsyncEnumerator<T> e = asyncEnumerable.GetAsyncEnumerator(cancel);
            try
            {
                while (await Run(() => e.MoveNextAsync()))
                {
                    cancel.ThrowIfCancellationRequested();
                    await Run(() => func(e.Current, cancel));
                }
            }
            finally { if (e != null) await Run(() => e.DisposeAsync()); }
        }
#endif
        }
        /// <summary>
        /// A static class to hold extensions to IEnumerable.
        /// </summary>
        public static class IEnumerableExtensions
    {
        /// <summary>
        /// Converts a single item to an enumerable.
        /// </summary>
        /// <typeparam name="T">The type for the item.</typeparam>
        /// <param name="singleItem">The item to put into an enumerable.</param>
        /// <returns>An enumerable with just <paramref name="singleItem"/> in it.</returns>
        public static IEnumerable<T> ToSingleItemEnumerable<T>(this T singleItem)
        {
            yield return singleItem;
        }
#if NETSTANDARD2_1 || NETCOREAPP3_1_OR_GREATER || NET5_0_OR_GREATER
        /// <summary>
        /// Converts an enumerable into an async enumerable.  Works with very large (or even infinite) enumerations.
        /// </summary>
        /// <typeparam name="T">The type for the item.</typeparam>
        /// <param name="e">The enumerable.</param>
        /// <param name="cancel">A <see cref="CancellationToken"/> the caller can use to cancel the operation before it completes.</param>
        /// <returns>An async enumerable with the elements from <paramref name="e"/> in it.</returns>
        public static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(this IEnumerable<T> e, [EnumeratorCancellation] CancellationToken cancel = default)
        {
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(e);
#else
            if (e == null) throw new ArgumentNullException(nameof(e));
#endif
            foreach (T t in e)
            {
                cancel.ThrowIfCancellationRequested();
                yield return t;
            }
            await Task.CompletedTask;
        }
#endif
        }
        /// <summary>
        /// A static class to hold extensions to IEnumerable.
        /// </summary>
        public static class IEnumeratorExtensions
    {
#if NETSTANDARD2_1 || NETCOREAPP3_1_OR_GREATER || NET5_0_OR_GREATER
        /// <summary>
        /// Converts an enumerable into an async enumerable.  Works with very large (or even infinite) enumerations.
        /// </summary>
        /// <typeparam name="T">The type for the item.</typeparam>
        /// <param name="e">The enumerable.</param>
        /// <param name="cancel">A <see cref="CancellationToken"/> the caller can use to cancel the operation before it completes.</param>
        /// <returns>An async enumerable with the elements from <paramref name="e"/> in it.</returns>
        public static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(this IEnumerator<T> e, [EnumeratorCancellation] CancellationToken cancel = default)
        {
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(e);
#else
            if (e == null) throw new ArgumentNullException(nameof(e));
#endif
            while (e.MoveNext())
            {
                cancel.ThrowIfCancellationRequested();
                yield return e.Current;
            }
            await Task.CompletedTask;
        }
#endif
    }
#if NETSTANDARD2_1 || NETCOREAPP3_1_OR_GREATER || NET5_0_OR_GREATER
    /// <summary>
    /// A static class to hold extensions to IAsyncEnumerable.
    /// </summary>
    public static class IAsyncEnumerableExtensions
    {
        /// <summary>
        /// Converts a single item to an enumerable.
        /// </summary>
        /// <typeparam name="T">The type for the item.</typeparam>
        /// <param name="singleItem">The item to put into an enumerable.</param>
        /// <returns>An enumerable with just <paramref name="singleItem"/> in it.</returns>
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        public static async IAsyncEnumerable<T> ToSingleItemAsyncEnumerable<T>(this T singleItem)
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
        {
            yield return singleItem;
        }
    }
    /// <summary>
    /// A static class to hold extensions to IAsyncEnumerator.
    /// </summary>
    public static class IAsyncEnumeratorExtensions
    {
        /// <summary>
        /// Asynchronously converts an <see cref="IAsyncEnumerable{T}"/> into a list.
        /// Note that since it returns a list, this function does NOT work with inifinite (or even very large) enumerations.
        /// </summary>
        /// <typeparam name="T">The type within the list.</typeparam>
        /// <param name="ae">The <see cref="IAsyncEnumerable{T}"/>.</param>
        /// <param name="cancel">A <see cref="CancellationToken"/> the caller can use to cancel the operation before it completes.</param>
        /// <returns>A <see cref="List{T}"/> containing all the items in the async enumerator.</returns>
        public static async ValueTask<List<T>> ToListAsync<T>(this IAsyncEnumerator<T> ae, CancellationToken cancel = default)
        {
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(ae);
#else
            if (ae == null) throw new ArgumentNullException(nameof(ae));
#endif
            List<T> ret = new();
            while (await ae.MoveNextAsync())
            {
                cancel.ThrowIfCancellationRequested();
                ret.Add(ae.Current);
            }
            return ret;
        }
        /// <summary>
        /// Asynchronously converts an <see cref="IAsyncEnumerator{T}"/> into an array.
        /// Note that since it returns a array, this function does NOT work with inifinite (or even very large) enumerations.
        /// </summary>
        /// <typeparam name="T">The type within the list.</typeparam>
        /// <param name="ae">The <see cref="IAsyncEnumerator{T}"/>.</param>
        /// <param name="cancel">A <see cref="CancellationToken"/> the caller can use to cancel the operation before it completes.</param>
        /// <returns>An array containing all the items in the async enumerator.</returns>
        public static async ValueTask<T[]> ToArrayAsync<T>(this IAsyncEnumerator<T> ae, CancellationToken cancel = default)
        {
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(ae);
#else
            if (ae == null) throw new ArgumentNullException(nameof(ae));
#endif
            List<T> ret = new();
            while (await ae.MoveNextAsync())
            {
                cancel.ThrowIfCancellationRequested();
                ret.Add(ae.Current);
            }
            return ret.ToArray();
        }
        /// <summary>
        /// Asynchronously converts an <see cref="IAsyncEnumerator{T}"/> into an <see cref="IEnumerable{T}"/>.
        /// Note that since it returns a array, this function does NOT work with inifinite (or even very large) enumerations.
        /// </summary>
        /// <typeparam name="T">The type within the list.</typeparam>
        /// <param name="ae">The <see cref="IAsyncEnumerator{T}"/>.</param>
        /// <param name="cancel">A <see cref="CancellationToken"/> the caller can use to cancel the operation before it completes.</param>
        /// <returns>An enumeration of all the items in the async enumerator.</returns>
        public static async ValueTask<IEnumerable<T>> ToEnumerableAsync<T>(this IAsyncEnumerator<T> ae, CancellationToken cancel = default)
        {
            // there may be a more efficient way to do this, but I haven't been able to find it
            return await Async.Run(() => ToListAsync<T>(ae, cancel));
        }
    }
#endif
    /// <summary>
    /// A <see cref="SynchronousTaskScheduler"/> that just runs each task immediately as it is queued.
    /// </summary>
    public sealed class SynchronousTaskScheduler : System.Threading.Tasks.TaskScheduler
    {
        private static readonly SynchronousTaskScheduler _Default = new();
        /// <summary>
        /// Gets the default instance for this singleton class.
        /// </summary>
        public static new SynchronousTaskScheduler Default => _Default;

        private SynchronousTaskScheduler()
        {
        }
        /// <summary>
        /// Gets the list of scheduled tasks, which for this class, is always empty.
        /// </summary>
        /// <returns>An empty enumeration.</returns>
        [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage] //  (no way to call externally, and I can't find a way to call it indirectly).
        protected override IEnumerable<Task> GetScheduledTasks()
        {
            return Enumerable.Empty<Task>();
        }
        /// <summary>
        /// Queues the specified task, which in this case, just executes it immediately.
        /// </summary>
        /// <param name="task">The <see cref="Task"/> which is to be executed.</param>
        [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage] //  (no way to call externally, and I can't find a way to call it indirectly).
        protected override void QueueTask(Task task)
        {
            TryExecuteTask(task);
        }
        /// <summary>
        /// Attempts to execute the specified task inline, which just runs the task.
        /// </summary>
        /// <param name="task">The <see cref="Task"/> to be executed.</param>
        /// <param name="taskWasPreviouslyQueued">Whether or not the task was previously queued.</param>
        /// <returns><b>true</b>.</returns>
        [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage] //  (no way to call externally, and I can't find a way to call it indirectly).
        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
        {
            return TryExecuteTask(task);
        }
        /// <summary>
        /// Gets the maximum number of tasks that can be concurrently running under this scheduler, which is one.
        /// </summary>
        public override int MaximumConcurrencyLevel => 1;
    }

    /// <summary>
    /// A <see cref="SynchronousSynchronizationContext"/> that schedules work items on the <see cref="SynchronousTaskScheduler"/>.
    /// </summary>
    public class SynchronousSynchronizationContext : System.Threading.SynchronizationContext
    {
        private static readonly SynchronousSynchronizationContext _Default = new();
        /// <summary>
        /// Gets the instance for this singleton class.
        /// </summary>
        public static SynchronousSynchronizationContext Default => _Default;

        private SynchronousSynchronizationContext()
        {
            SetWaitNotificationRequired();
        }

        /// <summary>
        /// Synchronously posts a message.
        /// </summary>
        /// <param name="d">The message to post.</param>
        /// <param name="state">The state to give to the post callback.</param>
        public override void Send(System.Threading.SendOrPostCallback d, object? state)
        {
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(d);
#else
            if (d == null) throw new ArgumentNullException(nameof(d));
#endif
            d(state);
        }
        /// <summary>
        /// Posts a message.  The caller intended to post it asynchronously, but the whole point of this class is to do everything synchronously, so this call is synchronous.
        /// </summary>
        /// <param name="d">The message to post.</param>
        /// <param name="state">The state to give to the post callback.</param>
        public override void Post(System.Threading.SendOrPostCallback d, object? state)
        {
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(d);
#else
            if (d == null) throw new ArgumentNullException(nameof(d));
#endif
            d(state);
        }
        /// <summary>
        /// Creates a "copy" of this <see cref="SynchronousSynchronizationContext"/>, which in this case just returns the singleton instance because there is nothing held in memory anyway.
        /// </summary>
        /// <returns>The same singleton <see cref="SynchronousSynchronizationContext"/> on which we were called.</returns>
        public override System.Threading.SynchronizationContext CreateCopy()
        {
            return this;
        }
    }
}
