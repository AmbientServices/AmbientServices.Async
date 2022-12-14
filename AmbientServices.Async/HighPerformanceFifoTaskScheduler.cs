using AmbientServices;
using AmbientServices.Utilities;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AmbientServices
{
    /// <summary>
    /// A <see cref="TaskFactory"/> that uses the <see cref="HighPerformanceFifoTaskScheduler"/> to schedule tasks.
    /// </summary>
#if NET5_0_OR_GREATER
    [UnsupportedOSPlatform("browser")]
#endif
    public sealed class HighPerformanceFifoTaskFactory : TaskFactory
    {
        private static readonly HighPerformanceFifoTaskFactory _DefaultTaskFactory = new();
        /// <summary>
        /// Gets the default <see cref="HighPerformanceFifoTaskFactory"/>.
        /// </summary>
        public static HighPerformanceFifoTaskFactory Default => _DefaultTaskFactory;
        /// <summary>
        /// Constructs a <see cref="TaskFactory"/> that uses the <see cref="HighPerformanceFifoTaskScheduler.Default"/> task scheduler.
        /// </summary>
        public HighPerformanceFifoTaskFactory()
            : this(CancellationToken.None, TaskCreationOptions.PreferFairness | TaskCreationOptions.LongRunning | TaskCreationOptions.RunContinuationsAsynchronously, TaskContinuationOptions.PreferFairness | TaskContinuationOptions.LongRunning, HighPerformanceFifoTaskScheduler.Default)
        {
        }
        /// <summary>
        /// Initializes a <see cref="HighPerformanceFifoTaskFactory"/> instance with the specified configuration.
        /// </summary>
        /// <param name="cancellationToken">The default <see cref="CancellationToken"/> to use for tasks that are started without an explicit <see cref="CancellationToken"/>.</param>
        public HighPerformanceFifoTaskFactory(CancellationToken cancellationToken)
            : this(cancellationToken, TaskCreationOptions.PreferFairness | TaskCreationOptions.LongRunning | TaskCreationOptions.RunContinuationsAsynchronously, TaskContinuationOptions.PreferFairness | TaskContinuationOptions.LongRunning, HighPerformanceFifoTaskScheduler.Default)
        {
        }
        /// <summary>
        /// Initializes a <see cref="HighPerformanceFifoTaskFactory"/> instance with the specified configuration.
        /// </summary>
        /// <param name="scheduler">The default <see cref="HighPerformanceFifoTaskScheduler"/> to use.</param>
        public HighPerformanceFifoTaskFactory(HighPerformanceFifoTaskScheduler scheduler)
            : this(CancellationToken.None, TaskCreationOptions.PreferFairness | TaskCreationOptions.LongRunning | TaskCreationOptions.RunContinuationsAsynchronously, TaskContinuationOptions.PreferFairness | TaskContinuationOptions.LongRunning, scheduler)
        {
        }
        /// <summary>
        /// Initializes a <see cref="HighPerformanceFifoTaskFactory"/> instance with the specified configuration.
        /// </summary>
        /// <param name="creationOptions">A set of <see cref="TaskCreationOptions"/> controlling task creation.</param>
        /// <param name="continuationOptions">A set of <see cref="TaskContinuationOptions"/> controlling task continuations.</param>
        public HighPerformanceFifoTaskFactory(TaskCreationOptions creationOptions, TaskContinuationOptions continuationOptions)
            : this(CancellationToken.None, creationOptions, continuationOptions, HighPerformanceFifoTaskScheduler.Default)
        {
        }
        /// <summary>
        /// Initializes a <see cref="HighPerformanceFifoTaskFactory"/> instance with the specified configuration.
        /// </summary>
        /// <param name="cancellationToken">The default <see cref="CancellationToken"/> to use for tasks that are started without an explicit <see cref="CancellationToken"/>.</param>
        /// <param name="creationOptions">A set of <see cref="TaskCreationOptions"/> controlling task creation.</param>
        /// <param name="continuationOptions">A set of <see cref="TaskContinuationOptions"/> controlling task continuations.</param>
        /// <param name="scheduler">The default <see cref="HighPerformanceFifoTaskScheduler"/> to use.</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1068:CancellationToken parameters must come last", Justification = "Just following .NET lead here")]
        public HighPerformanceFifoTaskFactory(CancellationToken cancellationToken, TaskCreationOptions creationOptions, TaskContinuationOptions continuationOptions, HighPerformanceFifoTaskScheduler scheduler)
            : base(cancellationToken, creationOptions, continuationOptions, scheduler)
        {
        }
    }
    /// <summary>
    /// A <see cref="SynchronizationContext"/> that schedules work items on the <see cref="HighPerformanceFifoTaskScheduler"/>.
    /// </summary>
#if NET5_0_OR_GREATER
    [UnsupportedOSPlatform("browser")]
#endif
    internal class HighPerformanceFifoSynchronizationContext : SynchronizationContext
    {
        private readonly HighPerformanceFifoTaskScheduler _scheduler;

        internal HighPerformanceFifoSynchronizationContext(HighPerformanceFifoTaskScheduler? scheduler = null)
        {
            SetWaitNotificationRequired();
            _scheduler = scheduler ?? HighPerformanceFifoTaskScheduler.Default;
        }

        /// <summary>
        /// Synchronously posts a message.
        /// </summary>
        /// <param name="d">The message to post.</param>
        /// <param name="state">The state to give to the post callback.</param>
        public override void Send(SendOrPostCallback d, object? state)
        {
            if (d == null) throw new ArgumentNullException(nameof(d));
            _ = _scheduler.QueueWork(() => d(state));
        }
        /// <summary>
        /// Posts a message.
        /// </summary>
        /// <param name="d">The message to post.</param>
        /// <param name="state">The state to give to the post callback.</param>
        public override void Post(SendOrPostCallback d, object? state)
        {
            if (d == null) throw new ArgumentNullException(nameof(d));
            _ = _scheduler.QueueWork(() => d(state));
        }
        /// <summary>
        /// Creates a "copy" of this <see cref="HighPerformanceFifoSynchronizationContext"/>, which in this case just returns the singleton instance because there is nothing held in memory anyway.
        /// </summary>
        /// <returns>The same singleton <see cref="HighPerformanceFifoSynchronizationContext"/> on which we were called.</returns>
        public override SynchronizationContext CreateCopy()
        {
            return this;
        }
    }
    /// <summary>
    /// A worker which contains a thread and various other objects needed to use the thread.  Disposes of itself when the thread is stopped.
    /// </summary>
#if NET5_0_OR_GREATER
    [UnsupportedOSPlatform("browser")]
#endif
    internal sealed class HighPerformanceFifoWorker : IntrusiveSinglyLinkedListNode, IDisposable
    {
        private static long _SlowestInvocation;     // interlocked

        private readonly HighPerformanceFifoTaskScheduler _scheduler;
        private readonly string _schedulerName;
        private readonly string _id;
        private readonly Thread _thread;
        private readonly ManualResetEvent _wakeThread = new(false);
        private readonly ManualResetEvent _allowDisposal = new(false);
        private Delegate? _actionToPerform; // interlocked
        private long _invokeTicks;          // interlocked
        private int _stop;                  // interlocked

        public static HighPerformanceFifoWorker Start(HighPerformanceFifoTaskScheduler scheduler, string id, ThreadPriority priority)
        {
            HighPerformanceFifoWorker ret = new(scheduler, id, priority);
            ret.Start();
            return ret;
        }
        private HighPerformanceFifoWorker(HighPerformanceFifoTaskScheduler scheduler, string id, ThreadPriority priority)
        {
            _scheduler = scheduler;
            _schedulerName = scheduler.Name;
            _id = id;
            // start the thread, it should block immediately until a work unit is ready
            _thread = new(new ThreadStart(WorkerFunc)) {
                Name = id,
                IsBackground = true,
                Priority = priority
            };
        }
        private void Start()
        {
            _thread.Start();
            _scheduler.SchedulerWorkersCreated?.Increment();
        }
#if DEBUG
        private readonly string fStackAtConstruction = new StackTrace().ToString();
        ~HighPerformanceFifoWorker()
        {
            Debug.Fail($"{nameof(HighPerformanceFifoWorker)} '{_schedulerName}' instance not disposed!  Constructed at: {fStackAtConstruction}");
            Dispose();
        }
#endif

        public void Dispose()
        {
            // dispose of the events
            _wakeThread.Dispose();
            _allowDisposal.Dispose();
#if DEBUG
            // suppress finalization
            System.GC.SuppressFinalize(this);
#endif
        }

        /// <summary>
        /// Gets whether or not this worker is currently executing a job.
        /// </summary>
        internal bool IsBusy => _actionToPerform != null;

        /// <summary>
        /// Schedules the specified delegate on this worker's thread and returns <b>true</b>, or returns <b>false</b> if the worker is already busy servicing another job.
        /// </summary>
        /// <param name="action">The action to attempt to carry out on this worker.</param>
        internal void Invoke(Action action)
        {
            // are we already stopped?
            if (_stop != 0 || _scheduler.Stopping)
            {
                throw new InvalidOperationException("The Worker has already been stopped!");
            }
            Action a = action;
            // try to put in this work--if there is something already there, we must be busy!
            if (Interlocked.CompareExchange(ref _actionToPerform, a, null) != null)
            {
                // the worker was busy so we couldn't marshal the action to it!
                throw new InvalidOperationException("Worker thread already in use!");
            }
            // start the timer
            Interlocked.Exchange(ref _invokeTicks, HighPerformanceFifoTaskScheduler.Ticks);
            // signal the thread to begin the work
            _wakeThread.Set();
            // we successfully issued the work request
        }
        /// <summary>
        /// Tells the thread to stop and exit.
        /// </summary>
        internal void Stop()
        {
            // record when we invoked the stop command
            Interlocked.Exchange(ref _invokeTicks, HighPerformanceFifoTaskScheduler.Ticks);
            // mark for stopping
            Interlocked.Exchange(ref _stop, 1);
            // worker has retired!
            _scheduler.SchedulerWorkersRetired?.Increment();
            HighPerformanceFifoTaskScheduler.Logger.Log(AmbientClock.UtcNow.ToShortTimeString() + ": Retiring worker: " + _id, "StartStop", AmbientLogLevel.Debug);
            // wake thread so it can exit gracefully
            _wakeThread.Set();
            // tell the thread it can dispose and exit
            _allowDisposal.Set();
        }

        internal static bool IsWorkerInternalMethod(MethodBase? method)
        {
            if (method == null || method.DeclaringType != typeof(HighPerformanceFifoWorker)) return false;
            return !method.IsPublic;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "CA1508:Avoid dead conditional code", Justification = "The compiler is clueless here because it doesn't understand Interlocked.Exchange")]
        private void WorkerFunc()
        {
            try
            {
                long maxInvokeTicks = 0;
                _scheduler.SchedulerWorkers?.Increment();
                try
                {
                    HighPerformanceFifoTaskScheduler.Logger.Log("Starting " + _id, "ThreadStartStop", AmbientLogLevel.Debug);
                    long startTicks = HighPerformanceFifoTaskScheduler.Ticks;
                    long completionTicks = startTicks;
                    // loop until we're told to stop
                    while (_stop == 0 && !_scheduler.Stopping)
                    {
                        startTicks = HighPerformanceFifoTaskScheduler.Ticks;
                        completionTicks = startTicks;
                        // wait for the wake thread event to be signalled so we can start some work
                        HighPerformanceFifoTaskScheduler.WaitForWork(_wakeThread);
                        // stop now?
                        if (_stop != 0 || _scheduler.Stopping)
                        {
                            break;
                        }
                        // record the start time
                        startTicks = HighPerformanceFifoTaskScheduler.Ticks;
                        // NO work to execute? (just in case--I don't think this can ever really happen)
                        if (_actionToPerform == null)
                        {
                            // nothing to do, so we're done
                            completionTicks = HighPerformanceFifoTaskScheduler.Ticks;
                        }
                        else
                        {
                            _scheduler.SchedulerBusyWorkers?.Increment();
                            try
                            {
                                // perform the work
                                _actionToPerform.DynamicInvoke();
                            }
                            finally
                            {
                                _scheduler.SchedulerBusyWorkers?.Decrement();
                                // mark the time
                                completionTicks = HighPerformanceFifoTaskScheduler.Ticks;
                            }
                        }
                        // finish work in success case--we're done with the work, so get rid of it so we're ready for the next one
                        Interlocked.Exchange(ref _actionToPerform, null);
                        // not stopping?
                        if (_stop == 0 && !_scheduler.Stopping)
                        {
                            // release the worker back to the ready list
                            _scheduler.OnWorkerReady(this);
                        }
                        // record statistics
                        long invokeTime = _invokeTicks;
                        // ignore how long it took if we never got invoked
                        if (invokeTime > 0)
                        {
                            long invokeTicks = startTicks - invokeTime;
                            long executionTicks = completionTicks - startTicks;
                            if (invokeTicks > maxInvokeTicks)
                            {
                                maxInvokeTicks = invokeTicks;
                                InterlockedUtilities.TryOptomisticMax(ref _SlowestInvocation, invokeTicks);
                                _scheduler.SchedulerSlowestInvocationMilliseconds?.SetValue(_SlowestInvocation * 1000 / HighPerformanceFifoTaskScheduler.TicksPerSecond);
                            }
                            _scheduler.SchedulerInvocationTime?.Add(invokeTicks);
                        }
                    }
                    HighPerformanceFifoTaskScheduler.Logger.Log($"Exiting '{_id}' worker thread: max invocation wait={maxInvokeTicks * 1000.0f / HighPerformanceFifoTaskScheduler.TicksPerSecond}ms", "ThreadStartStop", AmbientLogLevel.Debug);
                }
                finally
                {
                    _scheduler.SchedulerWorkers?.Decrement();
                }
            }
            finally
            {
                // wait for up to one second for the stopper to be done accessing us
                _allowDisposal.WaitOne(1000);
                Dispose();
            }
        }
    }
    /// <summary>
    /// A <see cref="TaskScheduler"/> that is high performance and runs tasks in first-in-first-out order.
    /// </summary>
#if NET5_0_OR_GREATER
    [UnsupportedOSPlatform("browser")]
#endif
    public sealed class HighPerformanceFifoTaskScheduler : TaskScheduler, IDisposable
    {
        private static readonly AmbientService<IAmbientStatistics> _AmbientStatistics = Ambient.GetService<IAmbientStatistics>();
        internal static readonly AmbientLogger<HighPerformanceFifoTaskScheduler> Logger = new();

        private static readonly int LogicalCpuCount = GetProcessorCount();
        private static readonly int MaxWorkerThreads = LogicalCpuCount * MaxThreadsPerLogicalCpu;
        private static readonly int HighThreadCountWarningEnvironmentTicks = (int)TimeSpan.FromHours(1).Ticks;
        private static readonly float MinAddThreadsCpuUsage = Math.Max(0.95f, 1.0f - (0.5f / LogicalCpuCount));
        private static readonly CpuMonitor CpuMonitor = new(1000);
        private static readonly ConcurrentHashSet<HighPerformanceFifoTaskScheduler> Schedulers = new();

        private const float MaxCpuUsage = 0.995f;
#if DEBUG
        private const int BufferThreadCount = 2;
        private static readonly int RetireCheckAfterCreationTickCount = (int)TimeSpan.FromSeconds(60).Ticks;
        private static readonly int RetireCheckFastestRetirementFrequencyTickCount = (int)TimeSpan.FromSeconds(3).Ticks;
        private const int MaxThreadsPerLogicalCpu = 50;

        private readonly bool _executeDisposalCheck;
#else
        private readonly static int BufferThreadCount = LogicalCpuCount;
        private static readonly int RetireCheckAfterCreationTickCount = (int)TimeSpan.FromMinutes(5).Ticks;
        private static readonly int RetireCheckFastestRetirementFrequencyTickCount = (int)TimeSpan.FromSeconds(60).Ticks;
        private const int MaxThreadsPerLogicalCpu = 150;
#endif

        // initialize this here to be sure all the above values have been set (it uses many of them)
        private static readonly HighPerformanceFifoTaskScheduler DefaultTaskScheduler = HighPerformanceFifoTaskScheduler.Start("Default", ThreadPriority.Normal, false);

        /// <summary>
        /// Gets the default <see cref="HighPerformanceFifoTaskScheduler"/>, one with normal priorities.
        /// </summary>
        public static new HighPerformanceFifoTaskScheduler Default => DefaultTaskScheduler;

        private readonly HighPerformanceFifoSynchronizationContext _synchronizationContext;

        internal readonly IAmbientStatistic? SchedulerInvocations;
        internal readonly IAmbientStatistic? SchedulerInvocationTime;
        internal readonly IAmbientStatistic? SchedulerWorkersCreated;
        internal readonly IAmbientStatistic? SchedulerWorkersRetired;
        internal readonly IAmbientStatistic? SchedulerInlineExecutions;
        internal readonly IAmbientStatistic? SchedulerWorkers;
        internal readonly IAmbientStatistic? SchedulerBusyWorkers;
        internal readonly IAmbientStatistic? SchedulerWorkersHighWaterMark;
        internal readonly IAmbientStatistic? SchedulerSlowestInvocationMilliseconds;

        private readonly IAmbientStatistics? _statistics;
        private readonly string _schedulerName;
        private readonly ThreadPriority _schedulerThreadPriority;
        private readonly int _schedulerMasterFrequencyMilliseconds;
        private readonly int _bufferWorkerThreads;
        private readonly int _maxWorkerThreads;
        private readonly bool _testMode;
        private readonly Thread _schedulerMasterThread;
        private readonly ManualResetEvent _wakeSchedulerMasterThread = new(false);  // controls the master thread waking up
        private readonly InterlockedSinglyLinkedList<HighPerformanceFifoWorker> _readyWorkerList = new();
        // everything from here down is interlocked
        private int _reset;                                             // triggers a one-time reset of the thread count back to the default buffer size
        private int _stopMasterThread;                                  // stops the master thread (shuts down the scheduler)            
        private int _workerId;                                          // an incrementing worker id used to name the worker threads
        private int _busyWorkers;                                       // the number of busy workers (interlocked)
        private int _workers;                                           // the total number of workers
        private long _peakConcurrentUsageSinceLastRetirementCheck;      // the peak concurrent usage since the last change in worker count
        private long _workersHighWaterMark;                             // the most workers we've ever seen (interlocked)
        private int _highThreadsWarningTickCount;                       // the last time we warned about too many threads (interlocked)
        private int _lastInlineExecutionTicks;                          // the last time we had to execute something inline because no workers were available (interlocked)
        private long _lastScaleUpTime;                                  // keeps track of the last time a scale up
        private long _lastScaleDownTime;                                // keeps track of the last time a scale down
        private long _lastResetTime;                                    // keeps track of the last time a reset happened

        /// <summary>
        /// Gets the <see cref="SynchronizationContext"/> for this task scheduler.
        /// </summary>
        public SynchronizationContext SynchronizationContext => _synchronizationContext;

        internal bool Stopping => _stopMasterThread != 0;
        /// <summary>
        /// Gets the <see cref="DateTime"/> of the completion of the last scale up.
        /// </summary>
        public DateTime LastScaleUp => new(_lastScaleUpTime);
        /// <summary>
        /// Gets the <see cref="DateTime"/> of the completion of the last scale down.
        /// </summary>
        public DateTime LastScaleDown => new(_lastScaleDownTime);
        /// <summary>
        /// Gets the <see cref="DateTime"/> of the completion of the last reset.
        /// </summary>
        public DateTime LastResetTime => new(_lastResetTime);
        /// <summary>
        /// Gets the current number of workers.
        /// </summary>
        public int Workers => _workers;
        /// <summary>
        /// Gets the number of currently busy workers.
        /// </summary>
        public int BusyWorkers => _busyWorkers;
        /// <summary>
        /// Gets the number of currently ready workers.
        /// </summary>
        public int ReadyWorkers => _readyWorkerList.Count;

        /// <summary>
        /// Stops the all the task schedulers by disposing of all of them.
        /// </summary>
        public static void Stop()
        {
            foreach (HighPerformanceFifoTaskScheduler scheduler in Schedulers)
            {
                scheduler.Dispose();
            }
        }

        /// <summary>
        /// Gets the name of the scheduler.
        /// </summary>
        public string Name => _schedulerName;

        /// <summary>
        /// Gets the ticks used internally for performance tracking.
        /// </summary>
        public static long Ticks => AmbientStopwatch.GetTimestamp();

        /// <summary>
        /// Gets the number of ticks per second used internally for performance tracking.
        /// </summary>
        public static long TicksPerSecond => AmbientStopwatch.Frequency;

        [ExcludeFromCodeCoverage]   // I've seen this throw an exception, but I have no idea how that's possible, let alone how to force it on demand
        private static int GetProcessorCount()
        {
            try
            {
                return Environment.ProcessorCount;
            }
            catch
            {
                // default to sixteen if we can't query
                return 16;
            }
        }
        /// <summary>
        /// Starts a new <see cref="HighPerformanceFifoTaskScheduler"/> with the specified configuration.
        /// </summary>
        /// <param name="schedulerName">The name of the task scheduler (used in logging and exceptions).</param>
        /// <param name="priority">The <see cref="ThreadPriority"/> for the threads that will be used ot execute the tasks.</param>
        /// <param name="executeDisposalCheck">Whether or not to verify that the instance is properly disposed.</param>
        /// <param name="statistics">An optional <see cref="IAmbientStatistics"/> to use for reporting statistics, if not specified or null, uses the ambient implementation.</param>
        /// <returns>A new <see cref="HighPerformanceFifoTaskScheduler"/> instance.</returns>
        public static HighPerformanceFifoTaskScheduler Start(string schedulerName, ThreadPriority priority, bool executeDisposalCheck, IAmbientStatistics? statistics = null)
        {
            HighPerformanceFifoTaskScheduler ret = new(schedulerName, priority, executeDisposalCheck, statistics);
            ret.Start();
            return ret;
        }
        private HighPerformanceFifoTaskScheduler (string scheduler, ThreadPriority priority, bool executeDisposalCheck, IAmbientStatistics? statistics = null)
            : this(scheduler, priority, statistics)
        {
#if DEBUG
            _executeDisposalCheck = executeDisposalCheck;
#endif
        }

        /// <summary>
        /// Starts a new <see cref="HighPerformanceFifoTaskScheduler"/> with the specified configuration.
        /// </summary>
        /// <param name="schedulerName">The name of the task scheduler (used in logging and exceptions).</param>
        /// <param name="priority">The <see cref="ThreadPriority"/> for the threads that will be used ot execute the tasks.</param>
        /// <returns>A new <see cref="HighPerformanceFifoTaskScheduler"/> instance.</returns>
        public static HighPerformanceFifoTaskScheduler Start(string schedulerName, ThreadPriority priority = ThreadPriority.Normal)
        {
            HighPerformanceFifoTaskScheduler ret = new(schedulerName, priority);
            ret.Start();
            return ret;
        }
        /// <summary>
        /// Starts a new <see cref="HighPerformanceFifoTaskScheduler"/> in test mode with the specified configuration.
        /// </summary>
        /// <param name="schedulerName">The name of the task scheduler (used in logging and exceptions).</param>
        /// <param name="schedulerMasterFrequencyMilliseconds">How many milliseconds to wait each time around the master scheduler loop, ie. the frequency with which to check to see if we should alter the number of worker threads.</param>
        /// <param name="bufferThreadCount">The number of threads to start with and to keep as a buffer after resetting.</param>
        /// <param name="maxThreads">The maximum number of threads to use, or zero to let the system decide.</param>
        /// <param name="statistics">An optional <see cref="IAmbientStatistics"/> to use for reporting statistics, if not specified or null, uses the ambient implementation.</param>
        /// <returns>A new <see cref="HighPerformanceFifoTaskScheduler"/> instance.</returns>
        internal static HighPerformanceFifoTaskScheduler Start(string schedulerName, int schedulerMasterFrequencyMilliseconds, int bufferThreadCount, int maxThreads, IAmbientStatistics? statistics = null)
        {
            HighPerformanceFifoTaskScheduler ret = new(schedulerName, ThreadPriority.Normal, statistics, schedulerMasterFrequencyMilliseconds, bufferThreadCount, maxThreads, true);
            ret.Start();
            return ret;
        }
        private HighPerformanceFifoTaskScheduler(string schedulerName, ThreadPriority priority = ThreadPriority.Normal, IAmbientStatistics? statistics = null, int schedulerMasterFrequencyMilliseconds = 1000, int bufferThreadCount = 0, int maxThreads = 0, bool testMode = false)
        {
            _synchronizationContext = new HighPerformanceFifoSynchronizationContext(this);

            // save the scheduler name and priority
            _statistics = statistics ?? _AmbientStatistics.Local;
            _schedulerName = schedulerName;
            _schedulerThreadPriority = priority;
            _schedulerMasterFrequencyMilliseconds = schedulerMasterFrequencyMilliseconds;
            _bufferWorkerThreads = (bufferThreadCount == 0) ? BufferThreadCount : bufferThreadCount;
            _maxWorkerThreads = (maxThreads == 0) ? MaxWorkerThreads : maxThreads;
            _testMode = testMode;
            _lastResetTime = _lastScaleDownTime = _lastScaleUpTime = AmbientClock.UtcNow.AddSeconds(-1).Ticks;

            SchedulerInvocations = _statistics?.GetOrAddStatistic(false, $"{nameof(SchedulerInvocations)}:{schedulerName}", "The total number of scheduler invocations");
            SchedulerInvocationTime = _statistics?.GetOrAddStatistic(true, $"{nameof(SchedulerInvocationTime)}:{schedulerName}", "The total number of ticks spent doing invocations");
            SchedulerWorkersCreated = _statistics?.GetOrAddStatistic(false, $"{nameof(SchedulerWorkersCreated)}:{schedulerName}", "The total number of scheduler workers created");
            SchedulerWorkersRetired = _statistics?.GetOrAddStatistic(false, $"{nameof(SchedulerWorkersRetired)}:{schedulerName}", "The total number of scheduler workers retired");
            SchedulerInlineExecutions = _statistics?.GetOrAddStatistic(false, $"{nameof(SchedulerInlineExecutions)}:{schedulerName}", "The total number of scheduler inline executions");
            SchedulerWorkers = _statistics?.GetOrAddStatistic(false, $"{nameof(SchedulerWorkers)}:{schedulerName}", "The current number of scheduler workers");
            SchedulerBusyWorkers = _statistics?.GetOrAddStatistic(false, $"{nameof(SchedulerBusyWorkers)}:{schedulerName}", "The current number of busy scheduler workers");
            SchedulerWorkersHighWaterMark = _statistics?.GetOrAddStatistic(false, $"{nameof(SchedulerWorkersHighWaterMark)}:{schedulerName}", "The highest number of scheduler workers");
            SchedulerSlowestInvocationMilliseconds = _statistics?.GetOrAddStatistic(false, $"{nameof(SchedulerSlowestInvocationMilliseconds)}:{schedulerName}", "The highest number of milliseconds required for an invocation");

            _schedulerMasterThread = new(new ThreadStart(SchedulerMaster)) {
                Name = "High Performance FIFO Shceduler '" + schedulerName + "' Master",
                IsBackground = true,
                Priority = (priority >= ThreadPriority.Highest) ? priority : (priority + 1)
            };
        }
        private void Start()
        {
            // initialize at least one worker immediately
            for (int i = 0; i < Math.Min(1, _bufferWorkerThreads); ++i)
            {
#pragma warning disable CA2000 // Dispose objects before losing scope  This is put into a collection and disposed later
                HighPerformanceFifoWorker worker = CreateWorker();
#pragma warning restore CA2000 // Dispose objects before losing scope
                Debug.Assert(!worker.IsBusy);
                _readyWorkerList.Push(worker);
            }
            _workersHighWaterMark = _readyWorkerList.Count;
            SchedulerWorkersHighWaterMark?.SetValue(_readyWorkerList.Count);
            _schedulerMasterThread.Start();
            Schedulers.Add(this);
        }

#if DEBUG
        private readonly string fStackAtConstruction = new StackTrace().ToString();
        ~HighPerformanceFifoTaskScheduler()
        {
            if (_executeDisposalCheck)
            {
                Debug.Fail($"Failed to dispose/close {nameof(HighPerformanceFifoTaskScheduler)} {_schedulerName}: Stack at construction was:\n{fStackAtConstruction}");
            }

            Dispose();
        }
#endif
        /// <summary>
        /// Disposes of the instance.
        /// </summary>
        public void Dispose()
        {
            // signal the master thread to stop
            Interlocked.Exchange(ref _stopMasterThread, 1);
            _wakeSchedulerMasterThread.Set();
            // wait for the scheduler master thread to recieve the message and shut down
            _schedulerMasterThread.Join();
            // retire all the the workers (now that the master thread has stopped, it shouldn't be able to start any more workers)
            while (RetireOneWorker()) { }
            // if there are any busy workers, they will just stop and dispose of themselves when they see that the scheduler is stopping
            // remove us from the master list of schedulers
            Schedulers.Remove(this);
            _wakeSchedulerMasterThread.Dispose();

            SchedulerInvocations?.Dispose();
            SchedulerInvocationTime?.Dispose();
            SchedulerWorkersCreated?.Dispose();
            SchedulerWorkersRetired?.Dispose();
            SchedulerInlineExecutions?.Dispose();
            SchedulerWorkers?.Dispose();
            SchedulerBusyWorkers?.Dispose();
            SchedulerWorkersHighWaterMark?.Dispose();
            SchedulerSlowestInvocationMilliseconds?.Dispose();

#if DEBUG
            // we're being disposed properly, so no need to call the finalizer
            GC.SuppressFinalize(this);
#endif
        }

        private string ThreadName(int thread)
        {
            return $"High Performance FIFO Task Scheduler '{_schedulerName}' Thread {thread + 1}";
        }

        internal static void WaitForWork(ManualResetEvent wakeWorkerThreadEvent)
        {
            // wait (forever) for an operation or a stop
            wakeWorkerThreadEvent.WaitOne();
            // reset the event so we're ready to go again
            wakeWorkerThreadEvent.Reset();
        }

        internal HighPerformanceFifoWorker CreateWorker()
        {
            int id = Interlocked.Increment(ref _workerId);
            // initialize the worker (starts their threads)
            HighPerformanceFifoWorker worker = HighPerformanceFifoWorker.Start(this, ThreadName(id), _schedulerThreadPriority);
            Interlocked.Increment(ref _workers);
            // update the high water mark if needed
            InterlockedUtilities.TryOptomisticMax(ref _workersHighWaterMark, _workers);
            SchedulerWorkersHighWaterMark?.SetValue(_workersHighWaterMark);
            return worker;
        }

        private static int TimeElapsed(int startTime, int endTime)
        {
            unchecked
            {
                return endTime - startTime;
            }
        }

        /// <summary>
        /// Tells the scheduler master thread to reset because we've just finished a massively parallel operation has finished and excess threads are no longer needed.
        /// </summary>
        public void Reset()
        {
            // trigger reset
            Interlocked.Exchange(ref _reset, 1);
            // wake up the master thread so it does it ASAP
            _wakeSchedulerMasterThread.Set();
        }

        //[System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "CA1508:Avoid dead conditional code", Justification = "This is just the compiler not being smart enough to understand that Interlocked.Exchange(ref _stopMasterThread, 1) actually changes this value")]
        //[System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The workers created here are not disposed locally because they're kept in a collection.  They are disposed eventually.")]
        private void SchedulerMaster()
        {
            ExecuteWithCatchAndLog(() =>
            {
                int lastRetirementTicks = Environment.TickCount;
                int lastCreationTicks = Environment.TickCount;
                ManualResetEvent wakeSchedulerMasterThread = _wakeSchedulerMasterThread;
                // loop forever (we're a background thread, so if the process exits, no problem!)
                while (_stopMasterThread == 0)
                {
                    ExecuteWithCatchAndLog(() =>
                    {
                        // sleep for up to one second or until we are awakened
                        if (wakeSchedulerMasterThread.WaitOne(_schedulerMasterFrequencyMilliseconds))
                        {
                            wakeSchedulerMasterThread.Reset();
                        }
                        // have we just been disposed?
                        if (_stopMasterThread != 0) return;
                        // calculate how long it's been since various events
                        int ticksNow = Environment.TickCount;
                        int ticksSinceInlineExecution = TimeElapsed(_lastInlineExecutionTicks, ticksNow);
                        int ticksSinceCreation = TimeElapsed(lastCreationTicks, ticksNow);
                        int ticksSinceRetirement = TimeElapsed(lastRetirementTicks, ticksNow);
                        // do we need to add more workers?
                        int totalWorkers = _workers;
                        int readyWorkers = _readyWorkerList.Count;
                        int busyWorkers = _busyWorkers;
                        // were we explicitly asked to reset the scheduler by retiring threads?
                        if (_reset != 0)
                        {
                            while (_readyWorkerList.Count > _bufferWorkerThreads)
                            {
                                // retire one worker--did it turn out to be busy?
                                if (!RetireOneWorker())
                                {
                                    // bail out now--there must have been a surge in work just when we were told to reset the scheduler, so there's no point now!
                                    break;
                                }
                            }
                            Interlocked.Exchange(ref _reset, 0);
                            HighPerformanceFifoTaskScheduler.Logger.Log($"{_schedulerName} workers:{_workers}", "Reset", AmbientLogLevel.Debug);
                            // record that we retired threads just now
                            lastRetirementTicks = Environment.TickCount;
                            // record that we just finished a reset
                            Interlocked.Exchange(ref _lastResetTime, AmbientClock.UtcNow.Ticks);
                        }
                        else if (readyWorkers <= _bufferWorkerThreads)
                        {
                            float recentCpuUsage = CpuMonitor.RecentUsage;
                            // too many workers already, or CPU too high?  (if the CPU is too high, we risk starving internal status checking processes and the server might appear to be offline)
                            if (totalWorkers > _maxWorkerThreads || recentCpuUsage > MaxCpuUsage)
                            {
                                // have we NOT already logged that we are using an insane number of threads in the past 60 minutes?
                                int now = Environment.TickCount;
                                int lastWarning = _highThreadsWarningTickCount;
                                if ((_testMode && lastWarning == 0) || TimeElapsed(lastWarning, now) > HighThreadCountWarningEnvironmentTicks)
                                {
                                    // race to log a warning--did we win the race?
                                    if (Interlocked.CompareExchange(ref _highThreadsWarningTickCount, now, lastWarning) == lastWarning)
                                    {
                                        Logger.Log($"'{_schedulerName}': {readyWorkers + busyWorkers} threads exceeds the limit of {_maxWorkerThreads} for this scheduler!", "Busy", AmbientLogLevel.Warning);
                                    }
                                }
                                // now we will just carry on because we will not expand beyond the number of threads we have now
                            }
                            // CPU low enough to create new workers?  (if the CPU is high, more workers would just gum up the system!)
                            else if (recentCpuUsage < MinAddThreadsCpuUsage)
                            {
                                // initialize enough workers to maintain the buffer thread count
                                for (int i = 0; i < _bufferWorkerThreads - readyWorkers; ++i)
                                {
                                    // create a new worker
                                    HighPerformanceFifoWorker worker = CreateWorker();
                                    Debug.Assert(!worker.IsBusy);
                                    _readyWorkerList.Push(worker);
                                }
                                HighPerformanceFifoTaskScheduler.Logger.Log($"{_schedulerName} workers:{_workers}", "Scale", AmbientLogLevel.Debug);
                                // record the expansion time so we don't retire anything for at least a minute
                                lastCreationTicks = Environment.TickCount;
                                // record that we just finished a scale up
                                Interlocked.Exchange(ref _lastScaleUpTime, AmbientClock.UtcNow.Ticks);
                            }
                            // else we *might* be able to use more workers, but the CPU is pretty high, so let's not
                        }
                        // enough ready workers that we could get by with less?
                        else if (readyWorkers > Math.Max(2, _bufferWorkerThreads))
                        {
                            // haven't had an inline execution or new worker creation or removed any workers for a while?
                            if (_testMode || (
                                    ticksSinceCreation > RetireCheckAfterCreationTickCount &&
                                    ticksSinceInlineExecution > RetireCheckAfterCreationTickCount &&
                                    ticksSinceRetirement > RetireCheckFastestRetirementFrequencyTickCount
                                ))
                            {
                                // was the highest concurrency since the last time we checked such that we didn't need many of the threads?
                                if (_peakConcurrentUsageSinceLastRetirementCheck < Math.Max(1, totalWorkers - _bufferWorkerThreads))
                                {
                                    // retire one worker
                                    RetireOneWorker();
                                    HighPerformanceFifoTaskScheduler.Logger.Log($"{_schedulerName} workers:{_workers}", "Scale", AmbientLogLevel.Debug);
                                    // record that we retired threads just now
                                    lastRetirementTicks = Environment.TickCount;
                                }
                                // reset the concurrency
                                Interlocked.Exchange(ref _peakConcurrentUsageSinceLastRetirementCheck, 0);
                                // record that we just finished a scale down
                                Interlocked.Exchange(ref _lastScaleDownTime, AmbientClock.UtcNow.Ticks);
                            }
                        }
                    });
                }
            });
        }
        internal void ExecuteWithCatchAndLog(Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                if (ex is ThreadAbortException || ex is TaskCanceledException) Logger.Log($"'{_schedulerName}' Exception: {ex}", "AsyncException", AmbientLogLevel.Debug);
                else Logger.Log($"'{_schedulerName}' Critical Exception: {ex}", "AsyncException", AmbientLogLevel.Error);
            }
        }

        private bool RetireOneWorker()
        {
            HighPerformanceFifoWorker? workerToStop = _readyWorkerList.Pop();
            // none left?
            if (workerToStop is null)
            {
                return false;
            }
            else Debug.Assert(!workerToStop.IsBusy);

            Interlocked.Decrement(ref _workers);
            workerToStop.Stop();
            return true;
        }

        internal void OnWorkerReady(HighPerformanceFifoWorker worker)
        {
            // add the worker back to the ready worker list
            Debug.Assert(!worker.IsBusy);
            _readyWorkerList.Push(worker);
        }

        /// <summary>
        /// Queues synchronous work to this scheduler, running it within the scheduler if workers are available, and running it inline (synchronously) if not.
        /// </summary>
        /// <param name="action">The action to run.</param>
        /// <returns>A <see cref="Task"/> representing the work, no matter whether it was scheduled on a worker thread or run inline.</returns>
        /// <remarks>Exceptions thrown from <paramref name="action"/> are placed into the returned <see cref="Task"/>.</remarks>
        public Task QueueWork(Action action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            TaskCompletionSource<bool> tcs = new();
            SchedulerInvocations?.Increment();
            // try to get a ready thread
            HighPerformanceFifoWorker? worker = _readyWorkerList.Pop();
            // no ready workers?
            if (worker is null || Stopping)
            {
                if (!Stopping)
                {
                    // record this miss
                    Interlocked.Exchange(ref _lastInlineExecutionTicks, Environment.TickCount);
                    SchedulerInlineExecutions?.Increment();
                    // wake the master thread so it will add more threads ASAP
                    _wakeSchedulerMasterThread.Set();
                    Logger.Log($"'{_schedulerName}': no available workers--invoking inline.", "Busy", AmbientLogLevel.Warning);
                }
                // execute the action inline
                ExecuteActionWithTaskCompletionSource(action, tcs);
                return tcs.Task;
            }
            else
            {
                Debug.Assert(!worker.IsBusy);
            }
            worker.Invoke(() => 
            {
                ExecuteActionWithTaskCompletionSource(action, tcs);
            });
            return tcs.Task;
        }
        internal void ExecuteActionWithTaskCompletionSource(Action action, TaskCompletionSource<bool> tcs)
        {
            // execute the action inline
            try
            {
                ExecuteAction(action);
                tcs.SetResult(true);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        }

        /// <summary>
        /// Queues asynchronous work to this scheduler, running it within the scheduler if workers are available, and running it inline if not.
        /// </summary>
        /// <typeparam name="T">The type returned by the function.</typeparam>
        /// <param name="func">The asynchronous function that does the work.</param>
        public async ValueTask<T> QueueWork<T>(Func<ValueTask<T>> func)
        {
            if (func == null) throw new ArgumentNullException(nameof(func));
            return await ExecuteTask(() => func()).ConfigureAwait(false);   // the whole point of this function is to possibly run the work in the HP context if there are workers available
        }

        /// <summary>
        /// Queues asynchronous work to this scheduler, running it within the scheduler if workers are available, and running it inline if not.
        /// </summary>
        /// <param name="func">The asynchronous function that does the work.</param>
        public async ValueTask QueueWork(Func<ValueTask> func)
        {
            if (func == null) throw new ArgumentNullException(nameof(func));
            await ExecuteTask(() => func()).ConfigureAwait(false);   // the whole point of this function is to possibly run the work in the HP context if there are workers available
        }

        /// <summary>
        /// Runs a long-running function asynchronously on a scheduler thread.
        /// </summary>
        /// <param name="func">The func to run asynchronously.  The function cannot be "async void" but may be async.  If async, this function returns a "wrapped" task.</param>
        /// <returns>A <see cref="Task"/> which may be used to wait for the action to complete.  Presumably you want the result, or you would have used <see cref="FireAndForget(Action)"/>.</returns>
        /// <remarks>Exceptions thrown from the function will be available to be observed through the returned <see cref="Task"/>.</remarks>
        public Task<T> Run<T>(Func<T> func)
        {
            if (func == null) throw new ArgumentNullException(nameof(func));
            if (Stopping) throw new ObjectDisposedException(nameof(HighPerformanceFifoTaskScheduler));
            TaskCompletionSource<T> tcs = new();
            SchedulerInvocations?.Increment();
            // try to get a ready thread
            HighPerformanceFifoWorker? worker = _readyWorkerList.Pop();
            // no ready workers?
            if (worker is null)
            {
                // create a new worker right now, but don't put it on the ready list because we're going to use it immediately
#pragma warning disable CA2000 // Dispose objects before losing scope (this gets put into a collection and disposed of later)
                worker = CreateWorker();
#pragma warning restore CA2000 // Dispose objects before losing scope
                Debug.Assert(!worker.IsBusy);
                // wake the master thread so it will add more threads ASAP so we don't have to do this here
                _wakeSchedulerMasterThread.Set();
            }
            else
            {
                Debug.Assert(!worker.IsBusy);
            }
            worker.Invoke(() =>
            {
                try
                {
                    ExecuteAction(() => tcs.SetResult(func()));
                }
                catch (Exception e)
                {
                    tcs.SetException(e);
                }
            });
            return tcs.Task;
        }
        /// <summary>
        /// Runs a long-running action asynchronously on a scheduler thread, but gets a <see cref="Task"/> we can use wait for completion when it does finally finish or get canceled.
        /// </summary>
        /// <param name="action">The action to run asynchronously.  May be an "async void" action.</param>
        /// <returns>A <see cref="Task"/> which may be used to wait for the function to complete after it is cancelled (or exits on its own).</returns>
        /// <remarks>Exceptions thrown from the function will be available to be observed through the returned <see cref="Task"/>.</remarks>
        public Task Run(Action action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (Stopping) throw new ObjectDisposedException(nameof(HighPerformanceFifoTaskScheduler));
            TaskCompletionSource<bool> tcs = new();
            SchedulerInvocations?.Increment();
            // try to get a ready thread
            HighPerformanceFifoWorker? worker = _readyWorkerList.Pop();
            // no ready workers?
            if (worker is null)
            {
                // create a new worker right now, but don't put it on the ready list because we're going to use it immediately
#pragma warning disable CA2000 // Dispose objects before losing scope (this gets put into a collection and disposed of later)
                worker = CreateWorker();
#pragma warning restore CA2000 // Dispose objects before losing scope
                Debug.Assert(!worker.IsBusy);
                // wake the master thread so it will add more threads ASAP so we don't have to do this here
                _wakeSchedulerMasterThread.Set();
            }
            else
            {
                Debug.Assert(!worker.IsBusy);
            }
            worker.Invoke(() =>
            {
                try
                {
                    ExecuteAction(() => { action(); tcs.SetResult(true); });
                }
                catch (Exception e)
                {
                    tcs.SetException(e);
                }
            });
            return tcs.Task;
        }
        /// <summary>
        /// Runs a fire-and-forget action asynchronously on a scheduler thread.
        /// </summary>
        /// <param name="action">The action to run asynchronously.  May be an "async void" action.</param>
        /// <remarks>Note that exceptions throw from <paramref name="action"/> will be unobserved.</remarks>
        [SuppressMessage("Design", "CA1030:Use events where appropriate", Justification = "This should be obvious.  The prefix 'Fire' doesn't always imply something that should be an event!")]
        public void FireAndForget(Action action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (Stopping) throw new ObjectDisposedException(nameof(HighPerformanceFifoTaskScheduler));
            SchedulerInvocations?.Increment();
            // try to get a ready thread
            HighPerformanceFifoWorker? worker = _readyWorkerList.Pop();
            // no ready workers?
            if (worker is null)
            {
                // create a new worker right now, but don't put it on the ready list because we're going to use it immediately
#pragma warning disable CA2000 // Dispose objects before losing scope (this gets put into a collection and disposed of later)
                worker = CreateWorker();
#pragma warning restore CA2000 // Dispose objects before losing scope
                Debug.Assert(!worker.IsBusy);
                // wake the master thread so it will add more threads ASAP so we don't have to do this here
                _wakeSchedulerMasterThread.Set();
            }
            else
            {
                Debug.Assert(!worker.IsBusy);
            }
            worker.Invoke(() =>
            {
                ExecuteAction(() => { action(); });
            });
        }
        private void ExecuteAction(Action action)
        {
            System.Threading.SynchronizationContext? oldContext = SynchronizationContext.Current;
            // we now have a worker that is busy
            Interlocked.Increment(ref _busyWorkers);
            // keep track of the maximum concurrent usage
            InterlockedUtilities.TryOptomisticMax(ref _peakConcurrentUsageSinceLastRetirementCheck, _busyWorkers);
            try
            {
                if (oldContext is not HighPerformanceFifoSynchronizationContext) SynchronizationContext.SetSynchronizationContext(_synchronizationContext);
                action();
            }
            finally
            {
                // the worker is no longer busy
                Interlocked.Decrement(ref _busyWorkers);
                SynchronizationContext.SetSynchronizationContext(oldContext);
            }
        }
        private async ValueTask<T> ExecuteTask<T>(Func<ValueTask<T>> func)
        {
            System.Threading.SynchronizationContext? oldContext = SynchronizationContext.Current;
            // we now have a worker that is busy
            Interlocked.Increment(ref _busyWorkers);
            // keep track of the maximum concurrent usage
            InterlockedUtilities.TryOptomisticMax(ref _peakConcurrentUsageSinceLastRetirementCheck, _busyWorkers);
            try
            {
                if (oldContext is not HighPerformanceFifoSynchronizationContext) SynchronizationContext.SetSynchronizationContext(_synchronizationContext);
                return await func().ConfigureAwait(false); // the whole point of this function is to execute the task in the hight performance synchronization context
            }
            finally
            {
                // the worker is no longer busy
                Interlocked.Decrement(ref _busyWorkers);
                SynchronizationContext.SetSynchronizationContext(oldContext);
            }
        }
        private async ValueTask ExecuteTask(Func<ValueTask> func)
        {
            System.Threading.SynchronizationContext? oldContext = SynchronizationContext.Current;
            // we now have a worker that is busy
            Interlocked.Increment(ref _busyWorkers);
            // keep track of the maximum concurrent usage
            InterlockedUtilities.TryOptomisticMax(ref _peakConcurrentUsageSinceLastRetirementCheck, _busyWorkers);
            try
            {
                if (oldContext is not HighPerformanceFifoSynchronizationContext) SynchronizationContext.SetSynchronizationContext(_synchronizationContext);
                await func().ConfigureAwait(false); // the whole point of this function is to execute the task in the hight performance synchronization context
            }
            finally
            {
                // the worker is no longer busy
                Interlocked.Decrement(ref _busyWorkers);
                SynchronizationContext.SetSynchronizationContext(oldContext);
            }
        }
        internal IEnumerable<Task> GetScheduledTasksDirect()
        {
            return GetScheduledTasks();
        }
        /// <summary>
        /// Gets the list of scheduled tasks.  In this case, this is always empty, as all tasks are either executed immediately on another thread or executed inline.
        /// </summary>
        /// <returns>An empty enumeration.</returns>
        protected override IEnumerable<Task> GetScheduledTasks()
        {
            return Array.Empty<Task>();
        }
        internal void QueueTaskDirect(Task task)
        {
            QueueTask(task);
        }
        /// <summary>
        /// Queues the specified task to the high performance FIFO scheduler, or runs it immediately if there are no workers available.
        /// </summary>
        /// <param name="task">The <see cref="Task"/> which is to be executed.</param>
        protected override void QueueTask(Task task)
        {
            if (task == null) throw new ArgumentNullException(nameof(task));
            if (_stopMasterThread != 0) throw new ObjectDisposedException(nameof(HighPerformanceFifoTaskScheduler));
            _ = QueueWork(() =>
            {
                TryExecuteTask(task);
            });
        }
        /// <summary>
        /// Attempts to execute the specified task inline.
        /// </summary>
        /// <param name="task">The <see cref="Task"/> to be executed.</param>
        /// <param name="taskWasPreviouslyQueued">Whether or not the task was previously queued.</param>
        /// <returns>Whether or not the task ran inline.</returns>
        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
        {
            return !taskWasPreviouslyQueued && TryExecuteTask(task);
        }
        /// <summary>
        /// Gets the maximum number of tasks that can be concurrently running under this scheduler.
        /// </summary>
        public override int MaximumConcurrencyLevel => _maxWorkerThreads;
    }

    internal class IntrusiveSinglyLinkedListNode
    {
        internal IntrusiveSinglyLinkedListNode? fNextNode;
    }

    /// <summary>
    /// A thread-safe intrusive stack.  All methods are thread-safe.
    /// </summary>
    /// <typeparam name="TYPE">The type of item to store in the stack.  Must inherit from <see cref="IntrusiveSinglyLinkedListNode"/>.</typeparam>
    internal class InterlockedSinglyLinkedList<TYPE> where TYPE : IntrusiveSinglyLinkedListNode
    {
        private int fCount;
        private int fPopping;
        private readonly IntrusiveSinglyLinkedListNode fRoot; // we use a circular list instead of null because it allows us to detect if the node has been removed without traversing the entire list, because all removed nodes will have their next node set to null, while no nodes in the list will have their next node set to null.

        /// <summary>
        /// Constructs the list.
        /// </summary>
        public InterlockedSinglyLinkedList()
        {
            fRoot = new IntrusiveSinglyLinkedListNode();
            fRoot.fNextNode = fRoot;
            Validate();
        }

        /// <summary>
        /// Pushes the specified node onto the top of the stack.
        /// </summary>
        /// <param name="node">The node to push onto the top of the stack.</param>
        /// <remarks>The specified node must NOT already be in another stack and must not be simultaneously added or removed by another thread.</remarks>
        public void Push(TYPE node)
        {
            if (node == null) throw new ArgumentNullException(nameof(node));
            Validate();
            // already in another list?
            if (node.fNextNode != null)
            {
                Validate();
                throw new InvalidOperationException("The specified node is already in a list!");
            }
            // get the current top, which will be the old one if we succeed
            IntrusiveSinglyLinkedListNode? oldTop = fRoot.fNextNode;
            // loop until we win the race to insert us at the top
            do
            {
                // set this node's next node as the node we just got (we'll change it if we don't win the race)
                node.fNextNode = oldTop;
                // race to place this node in as the top node--did we win?
                if (Interlocked.CompareExchange(ref fRoot.fNextNode, node, oldTop) == oldTop)
                {
                    // increment the node count (it will be one off temporarily, but since the list can change at any time, nobody can reliably detect this anyway)
                    Interlocked.Increment(ref fCount);
                    Validate();
                    return;
                }
                // else do it all again, get the current top, which will be the old one if we succeed
                oldTop = fRoot.fNextNode;
            } while (true);
        }

        /// <summary>
        /// Pops off the top node on the stack and returns it.
        /// </summary>
        /// <returns>The top node, or <b>null</b> if there are no items in the stack.</returns>
        public TYPE? Pop()
        {
            Validate();
            IntrusiveSinglyLinkedListNode? ret;
            // wait until no other threads are popping
            while (Interlocked.CompareExchange(ref fPopping, 1, 0) != 0) { }
            try
            {
                // loop while there something in the list to remove
                while ((ret = fRoot.fNextNode) != fRoot)
                {
                    // get the new top
                    IntrusiveSinglyLinkedListNode? newTop = ret?.fNextNode;
                    // another thread better not have already popped this node, but just in case, check here
                    if (newTop != null)
                    {
                        // try to exchange the new top into the top position--did we win the race against a pusher?
                        if (Interlocked.CompareExchange(ref fRoot.fNextNode, newTop, ret) == ret)
                        {
                            // decrement the node count (it will be one off temporarily, but since the list can change at any time, nobody can reliably detect this anyway)
                            Interlocked.Decrement(ref fCount);
                            // clear the next pointer because we are no longer in the list
                            ret!.fNextNode = null;      // this is a little complicated, but if you look closely, ret cannot actually be null here
                            Validate();
                            // return the node that was removed
                            return (TYPE)ret;
                        }
                        // try again from the beginning
                    }
                    else Debug.Fail("newTop was null!");
                }
            }
            finally
            {
                // we are no longer popping
                Interlocked.Exchange(ref fPopping, 0);
            }
            Validate();
            // nothing there to pop!
            return null;
        }

        [Conditional("DEBUG")]
        internal void Validate()
        {
            // we better still have a valid root!
            Debug.Assert(fRoot is not null && fRoot is not TYPE);
            // root node better not have a null next pointer
            Debug.Assert(fRoot?.fNextNode != null);
        }

        /// <summary>
        /// Clear all items from the list.  May remove nodes that are added after this function starts, and items may be added before the function returns, but at some point before the call returns, the list will be empty.
        /// </summary>
        public void Clear()
        {
            Validate();
            // pop from the top until the list is empty
            while (Pop() != null)
            {
                // do nothing
            }
            Validate();
        }

        /// <summary>
        /// Gets the number of items in the list, which could change before if could be useful.
        /// </summary>
        public int Count
        {
            get
            {
                Validate();
                return fCount;
            }
        }
    }
}
