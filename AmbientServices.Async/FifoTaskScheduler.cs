using AmbientServices.Utilities;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
#if NET5_0_OR_GREATER
using System.Runtime.Versioning;
#endif
namespace AmbientServices
{
    /// <summary>
    /// A <see cref="TaskFactory"/> that uses the <see cref="FifoTaskScheduler"/> to schedule tasks.
    /// </summary>
#if NET5_0_OR_GREATER
    [UnsupportedOSPlatform("browser")]
#endif
    public sealed class FifoTaskFactory : TaskFactory
    {
        private static readonly FifoTaskFactory _DefaultTaskFactory = new();
        /// <summary>
        /// Gets the default <see cref="FifoTaskFactory"/>.
        /// </summary>
        public static FifoTaskFactory Default => _DefaultTaskFactory;
        /// <summary>
        /// Constructs a <see cref="TaskFactory"/> that uses the <see cref="FifoTaskScheduler.Default"/> task scheduler.
        /// </summary>
        public FifoTaskFactory()
            : this(CancellationToken.None, TaskCreationOptions.PreferFairness | TaskCreationOptions.LongRunning | TaskCreationOptions.RunContinuationsAsynchronously, TaskContinuationOptions.PreferFairness | TaskContinuationOptions.LongRunning, FifoTaskScheduler.Default)
        {
        }
        /// <summary>
        /// Initializes a <see cref="FifoTaskFactory"/> instance with the specified configuration.
        /// </summary>
        /// <param name="cancellationToken">The default <see cref="CancellationToken"/> to use for tasks that are started without an explicit <see cref="CancellationToken"/>.</param>
        public FifoTaskFactory(CancellationToken cancellationToken)
            : this(cancellationToken, TaskCreationOptions.PreferFairness | TaskCreationOptions.LongRunning | TaskCreationOptions.RunContinuationsAsynchronously, TaskContinuationOptions.PreferFairness | TaskContinuationOptions.LongRunning, FifoTaskScheduler.Default)
        {
        }
        /// <summary>
        /// Initializes a <see cref="FifoTaskFactory"/> instance with the specified configuration.
        /// </summary>
        /// <param name="scheduler">The default <see cref="FifoTaskScheduler"/> to use.</param>
        public FifoTaskFactory(FifoTaskScheduler scheduler)
            : this(CancellationToken.None, TaskCreationOptions.PreferFairness | TaskCreationOptions.LongRunning | TaskCreationOptions.RunContinuationsAsynchronously, TaskContinuationOptions.PreferFairness | TaskContinuationOptions.LongRunning, scheduler)
        {
        }
        /// <summary>
        /// Initializes a <see cref="FifoTaskFactory"/> instance with the specified configuration.
        /// </summary>
        /// <param name="creationOptions">A set of <see cref="TaskCreationOptions"/> controlling task creation.</param>
        /// <param name="continuationOptions">A set of <see cref="TaskContinuationOptions"/> controlling task continuations.</param>
        public FifoTaskFactory(TaskCreationOptions creationOptions, TaskContinuationOptions continuationOptions)
            : this(CancellationToken.None, creationOptions, continuationOptions, FifoTaskScheduler.Default)
        {
        }
        /// <summary>
        /// Initializes a <see cref="FifoTaskFactory"/> instance with the specified configuration.
        /// </summary>
        /// <param name="cancellationToken">The default <see cref="CancellationToken"/> to use for tasks that are started without an explicit <see cref="CancellationToken"/>.</param>
        /// <param name="creationOptions">A set of <see cref="TaskCreationOptions"/> controlling task creation.</param>
        /// <param name="continuationOptions">A set of <see cref="TaskContinuationOptions"/> controlling task continuations.</param>
        /// <param name="scheduler">The default <see cref="FifoTaskScheduler"/> to use.</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1068:CancellationToken parameters must come last", Justification = "Just following .NET lead here")]
        public FifoTaskFactory(CancellationToken cancellationToken, TaskCreationOptions creationOptions, TaskContinuationOptions continuationOptions, FifoTaskScheduler scheduler)
            : base(cancellationToken, creationOptions, continuationOptions, scheduler)
        {
        }
    }
#if false
    /// <summary>
    /// A <see cref="SynchronizationContext"/> that schedules work items on the <see cref="FifoTaskScheduler"/>.
    /// </summary>
#if NET5_0_OR_GREATER
    [UnsupportedOSPlatform("browser")]
#endif
    internal class FifoSynchronizationContext : SynchronizationContext
    {
        private readonly FifoTaskScheduler _scheduler;

        internal FifoSynchronizationContext(FifoTaskScheduler? scheduler = null)
        {
            SetWaitNotificationRequired();
            _scheduler = scheduler ?? FifoTaskScheduler.Default;
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
        /// Creates a "copy" of this <see cref="FifoSynchronizationContext"/>, which in this case just returns the singleton instance because there is nothing held in memory anyway.
        /// </summary>
        /// <returns>The same singleton <see cref="FifoSynchronizationContext"/> on which we were called.</returns>
        public override SynchronizationContext CreateCopy()
        {
            return this;
        }
    }
#endif
    /// <summary>
    /// A class that holds arguments for the task inlined event arguments.
    /// </summary>
    public class TaskInlinedEventArgs : EventArgs
    {
        /// <summary>
        /// Constructs a task inlined arguments.
        /// </summary>
        public TaskInlinedEventArgs()
        {
        }
    }
    /// <summary>
    /// A worker which contains a thread and various other objects needed to use the thread.  Disposes of itself when the thread is stopped.
    /// </summary>
#if NET5_0_OR_GREATER
    [UnsupportedOSPlatform("browser")]
#endif
    internal sealed class FifoWorker : IntrusiveSinglyLinkedListNode, IDisposable
    {
        private static long _SlowestInvocation;     // interlocked

        private readonly FifoTaskScheduler _scheduler;
        private readonly string _schedulerName;
        private readonly string _id;
        private readonly Thread _thread;
        private readonly ManualResetEvent _wakeThread = new(false);
        private readonly ManualResetEvent _allowDisposal = new(false);
        private Action? _actionToPerform;   // interlocked
        private long _invokeTicks;          // interlocked
        private int _stop;                  // interlocked

        public static FifoWorker Start(FifoTaskScheduler scheduler, string id, ThreadPriority priority)
        {
            FifoWorker ret = new(scheduler, id, priority);
            ret.Start();
            return ret;
        }
        private FifoWorker(FifoTaskScheduler scheduler, string id, ThreadPriority priority)
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
            _scheduler.SchedulerWorkersCreated?.IncrementRaw();
        }
#if DEBUG
        private readonly string fStackAtConstruction = new StackTrace().ToString();
        ~FifoWorker()
        {
            Debug.Fail($"{nameof(FifoWorker)} '{_schedulerName}:{_id}' instance not disposed!  Constructed at: {fStackAtConstruction}");
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
                throw new InvalidOperationException("The worker has already been stopped!");
            }
            // try to put in this work--if there is something already there, we must be busy!
            if (Interlocked.CompareExchange(ref _actionToPerform, action, null) != null)
            {
                // the worker was busy so we couldn't marshal the action to it!
                throw new InvalidOperationException("Worker thread already in use!");
            }
            // start the timer
            Interlocked.Exchange(ref _invokeTicks, FifoTaskScheduler.Ticks);
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
            Interlocked.Exchange(ref _invokeTicks, FifoTaskScheduler.Ticks);
            // mark for stopping
            Interlocked.Exchange(ref _stop, 1);
            // worker has retired!
            _scheduler.SchedulerWorkersRetired?.IncrementRaw();
            FifoTaskScheduler.Logger.Filter("StartStop", AmbientLogLevel.Debug)?.Log(new { Action = "RetireWorker", WorkerId = _id });
            // wake thread so it can exit gracefully
            _wakeThread.Set();
            // tell the thread it can dispose and exit
            _allowDisposal.Set();
        }

        internal static bool IsWorkerInternalMethod(MethodBase? method)
        {
            if (method == null || method.DeclaringType != typeof(FifoWorker)) return false;
            return !method.IsPublic;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "CA1508:Avoid dead conditional code", Justification = "The compiler is clueless here because it doesn't understand Interlocked.Exchange")]
        private void WorkerFunc()
        {
            try
            {
                long maxInvokeTicks = 0;
                _scheduler.SchedulerWorkers?.IncrementRaw();
                try
                {
                    FifoTaskScheduler.Logger.Filter("ThreadStartStop", AmbientLogLevel.Debug)?.Log(new { Action = "StartWorker", WorkerId = _id });
                    long startTicks = FifoTaskScheduler.Ticks;
                    long completionTicks = startTicks;
                    // loop until we're told to stop
                    while (_stop == 0 && !_scheduler.Stopping)
                    {
                        startTicks = FifoTaskScheduler.Ticks;
                        completionTicks = startTicks;
                        // wait for the wake thread event to be signalled so we can start some work
                        FifoTaskScheduler.WaitForWork(_wakeThread);
                        // stop now?
                        if (_stop != 0 || _scheduler.Stopping)
                        {
                            break;
                        }
                        // record the start time
                        startTicks = FifoTaskScheduler.Ticks;
                        // NO work to execute? (just in case--I don't think this can ever really happen)
                        if (_actionToPerform == null)
                        {
                            // nothing to do, so we're done
                            completionTicks = FifoTaskScheduler.Ticks;
                        }
                        else
                        {
                            _scheduler.SchedulerBusyWorkers?.IncrementRaw();
                            try
                            {
                                // perform the work (handling any uncaught exception)
                                _scheduler.ExecuteAction(_actionToPerform);
                            }
                            finally
                            {
                                _scheduler.SchedulerBusyWorkers?.DecrementRaw();
                                // mark the time
                                completionTicks = FifoTaskScheduler.Ticks;
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
                                _scheduler.SchedulerSlowestInvocationMilliseconds?.SetValue(_SlowestInvocation * 1000 / FifoTaskScheduler.TicksPerSecond);
                            }
                            _scheduler.SchedulerInvocationTime?.AddRaw(invokeTicks);
                        }
                    }
                    FifoTaskScheduler.Logger.Filter("ThreadStartStop", AmbientLogLevel.Debug)?.Log(new { Action = "ExitWorker", WorkerId = _id, MaxInvocatoinWaitMs = maxInvokeTicks * 1000.0f / FifoTaskScheduler.TicksPerSecond, });
                }
                finally
                {
                    _scheduler.SchedulerWorkers?.DecrementRaw();
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
    public sealed class FifoTaskScheduler : TaskScheduler, IDisposable
    {
        private static readonly AmbientService<IAmbientStatistics> _AmbientStatistics = Ambient.GetService<IAmbientStatistics>();
        internal static readonly AmbientLogger<FifoTaskScheduler> Logger = new();

        private static readonly int LogicalCpuCount = GetProcessorCount();
        private static readonly int MaxWorkerThreads = LogicalCpuCount * MaxThreadsPerLogicalCpu;
        private static readonly int HighThreadCountWarningEnvironmentTicks = (int)TimeSpan.FromHours(1).Ticks;
        private static readonly float MinAddThreadsCpuUsage = Math.Max(0.95f, 1.0f - (0.5f / LogicalCpuCount));
        private static readonly CpuMonitor CpuMonitor = new(1000);
        private static readonly ConcurrentHashSet<FifoTaskScheduler> Schedulers = new();

        private const float MaxCpuUsage = 0.97f;
#if DEBUG
        private const int BufferThreadCount = 2;
        private static readonly int RetireCheckAfterCreationTickCount = (int)TimeSpan.FromSeconds(60).Ticks;
        private static readonly int RetireCheckFastestRetirementFrequencyTickCount = (int)TimeSpan.FromSeconds(3).Ticks;
        private const int MaxThreadsPerLogicalCpu = 5;

        private readonly bool _executeDisposalCheck;
#else
        private const int BufferThreadsPerCpu = 2;
        private readonly static int BufferThreadCount = LogicalCpuCount * BufferThreadsPerCpu;
        private static readonly int RetireCheckAfterCreationTickCount = (int)TimeSpan.FromMinutes(5).Ticks;
        private static readonly int RetireCheckFastestRetirementFrequencyTickCount = (int)TimeSpan.FromSeconds(60).Ticks;
        private const int MaxThreadsPerLogicalCpu = 50;
#endif

        // initialize this here to be sure all the above values have been set (it uses many of them)
        private static readonly FifoTaskScheduler DefaultTaskScheduler = FifoTaskScheduler.Start("FIFO Default", 0, 0, ThreadPriority.Normal, false);

        /// <summary>
        /// Gets the default <see cref="FifoTaskScheduler"/>, one with normal priorities.
        /// </summary>
        public static new FifoTaskScheduler Default => DefaultTaskScheduler;

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
        private readonly InterlockedSinglyLinkedList<FifoWorker> _readyWorkerList = new();
        // everything from here down is interlocked
        private int _reset;                                             // triggers a one-time reset of the thread count back to the default buffer size
        private int _stopMasterThread;                                  // stops the master thread (shuts down the scheduler)            
        private int _workerId;                                          // an incrementing worker id used to name the worker threads
        private int _busyWorkers;                                       // the number of busy workers (interlocked)
        private int _workers;                                           // the total number of workers
        private long _peakConcurrentUsageSinceLastRetirementCheck;      // the peak concurrent usage since the last reduction in worker count
        private long _workersHighWaterMark;                             // the most workers we've ever seen (interlocked)
        private int _highThreadsWarningTickCount;                       // the last time we warned about too many threads (interlocked)
        private int _lastInlineExecutionTicks;                          // the last time we had to execute something inline because no workers were available (interlocked)
        private long _lastScaleUpTime;                                  // keeps track of the last time a scale up
        private long _lastScaleDownTime;                                // keeps track of the last time a scale down
        private long _lastResetTime;                                    // keeps track of the last time a reset happened

        /// <summary>
        /// An event that notifies scubscribers whenever an exception is thrown and not handled by the specified non-Task delegate.
        /// </summary>
        public static event EventHandler<UnobservedTaskExceptionEventArgs>? UnhandledException;
        /// <summary>
        /// An event that notifies scubscribers whenever a task is inlined due to all threads currently being busy.
        /// </summary>
        public static event EventHandler<TaskInlinedEventArgs>? TaskInlined;

        internal void RaiseUnhandledException(Exception ex)
        {
            UnhandledException?.Invoke(this, new UnobservedTaskExceptionEventArgs((ex as AggregateException) ?? new AggregateException(ex)));
        }

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
            foreach (FifoTaskScheduler scheduler in Schedulers)
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
        /// Starts a new <see cref="FifoTaskScheduler"/> with the specified configuration.
        /// </summary>
        /// <param name="schedulerName">The name of the task scheduler (used in logging and exceptions).</param>
        /// <param name="bufferThreadCount">The number of threads to start with and to keep as a buffer after resetting.</param>
        /// <param name="maxThreads">The maximum number of threads to use, or zero to let the system decide.</param>
        /// <param name="priority">The <see cref="ThreadPriority"/> for the threads that will be used ot execute the tasks.</param>
        /// <param name="executeDisposalCheck">Whether or not to verify that the instance is properly disposed.  Defaults to true.</param>
        /// <param name="statistics">An optional <see cref="IAmbientStatistics"/> to use for reporting statistics, if not specified or null, uses the ambient implementation.</param>
        /// <returns>A new <see cref="FifoTaskScheduler"/> instance.</returns>
        internal static FifoTaskScheduler Start(string schedulerName, int bufferThreadCount, int maxThreads, ThreadPriority priority, bool executeDisposalCheck, IAmbientStatistics? statistics = null)
        {
            FifoTaskScheduler ret = new(schedulerName, bufferThreadCount, maxThreads, priority, executeDisposalCheck, statistics);
            ret.Start();
            return ret;
        }
        private FifoTaskScheduler (string scheduler, int bufferThreadCount, int maxThreads, ThreadPriority priority, bool executeDisposalCheck, IAmbientStatistics? statistics = null)
            : this(scheduler, bufferThreadCount, maxThreads, priority, statistics)
        {
#if DEBUG
            _executeDisposalCheck = executeDisposalCheck;
#endif
        }

        /// <summary>
        /// Starts a new <see cref="FifoTaskScheduler"/> with the specified configuration.
        /// </summary>
        /// <param name="schedulerName">The name of the task scheduler (used in logging and exceptions).</param>
        /// <param name="bufferThreadCount">The number of threads to start with and to keep as a buffer after resetting.  Defaults to zero which uses the system-wide default.</param>
        /// <param name="maxThreads">The maximum number of threads to use, or zero to let the system decide.  Defaults to zero, which uses the system-wide default.</param>
        /// <param name="priority">The <see cref="ThreadPriority"/> for the threads that will be used ot execute the tasks.  Defaults to <see cref="ThreadPriority.Normal"/>.</param>
        /// <returns>A new <see cref="FifoTaskScheduler"/> instance.</returns>
        public static FifoTaskScheduler Start(string schedulerName, int bufferThreadCount = 0, int maxThreads = 0, ThreadPriority priority = ThreadPriority.Normal)
        {
            FifoTaskScheduler ret = new(schedulerName, bufferThreadCount, maxThreads, priority);
            ret.Start();
            return ret;
        }
        /// <summary>
        /// Starts a new <see cref="FifoTaskScheduler"/> in test mode with the specified configuration.
        /// </summary>
        /// <param name="schedulerName">The name of the task scheduler (used in logging and exceptions).</param>
        /// <param name="bufferThreadCount">The number of threads to start with and to keep as a buffer after resetting.</param>
        /// <param name="maxThreads">The maximum number of threads to use, or zero to let the system decide.</param>
        /// <param name="schedulerMasterFrequencyMilliseconds">How many milliseconds to wait each time around the master scheduler loop, ie. the frequency with which to check to see if we should alter the number of worker threads.</param>
        /// <param name="statistics">An optional <see cref="IAmbientStatistics"/> to use for reporting statistics, if not specified or null, uses the ambient implementation.</param>
        /// <returns>A new <see cref="FifoTaskScheduler"/> instance.</returns>
        internal static FifoTaskScheduler Start(string schedulerName, int bufferThreadCount, int maxThreads, int schedulerMasterFrequencyMilliseconds, IAmbientStatistics? statistics = null)
        {
            FifoTaskScheduler ret = new(schedulerName, bufferThreadCount, maxThreads, ThreadPriority.Normal, statistics, schedulerMasterFrequencyMilliseconds, true);
            ret.Start();
            return ret;
        }
        private FifoTaskScheduler(string schedulerName, int bufferThreadCount = 0, int maxThreads = 0, ThreadPriority priority = ThreadPriority.Normal, IAmbientStatistics? statistics = null, int schedulerMasterFrequencyMilliseconds = 1000, bool testMode = false)
        {
            // save the scheduler name and priority
            _statistics = statistics ?? _AmbientStatistics.Local;
            _schedulerName = schedulerName;
            _schedulerThreadPriority = priority;
            _schedulerMasterFrequencyMilliseconds = schedulerMasterFrequencyMilliseconds;
            _bufferWorkerThreads = (bufferThreadCount == 0) ? BufferThreadCount : bufferThreadCount;
            _maxWorkerThreads = (maxThreads == 0) ? MaxWorkerThreads : maxThreads;
            _testMode = testMode;
            _lastResetTime = _lastScaleDownTime = _lastScaleUpTime = AmbientClock.UtcNow.AddSeconds(-1).Ticks;

            SchedulerInvocations = _statistics?.GetOrAddStatistic(AmbientStatisicType.Cumulative, $"{nameof(SchedulerInvocations)}:{schedulerName}", $"{nameof(SchedulerInvocations)}:{schedulerName}", "The total number of scheduler invocations");
            SchedulerInvocationTime = _statistics?.GetOrAddTimeBasedStatistic(AmbientStatisicType.Cumulative, $"{nameof(SchedulerInvocationTime)}:{schedulerName}", $"{nameof(SchedulerInvocationTime)}:{schedulerName}", "The total number of ticks spent doing invocations");
            SchedulerWorkersCreated = _statistics?.GetOrAddStatistic(AmbientStatisicType.Cumulative, $"{nameof(SchedulerWorkersCreated)}:{schedulerName}", $"{nameof(SchedulerWorkersCreated)}:{schedulerName}", "The total number of scheduler workers created");
            SchedulerWorkersRetired = _statistics?.GetOrAddStatistic(AmbientStatisicType.Cumulative, $"{nameof(SchedulerWorkersRetired)}:{schedulerName}", $"{nameof(SchedulerWorkersRetired)}:{schedulerName}", "The total number of scheduler workers retired");
            SchedulerInlineExecutions = _statistics?.GetOrAddStatistic(AmbientStatisicType.Cumulative, $"{nameof(SchedulerInlineExecutions)}:{schedulerName}", $"{nameof(SchedulerInlineExecutions)}:{schedulerName}", "The total number of scheduler inline executions");
            SchedulerWorkers = _statistics?.GetOrAddStatistic(AmbientStatisicType.Raw, $"{nameof(SchedulerWorkers)}:{schedulerName}", $"{nameof(SchedulerWorkers)}:{schedulerName}", "The current number of scheduler workers");
            SchedulerBusyWorkers = _statistics?.GetOrAddStatistic(AmbientStatisicType.Raw, $"{nameof(SchedulerBusyWorkers)}:{schedulerName}", $"{nameof(SchedulerBusyWorkers)}:{schedulerName}", "The current number of busy scheduler workers");
            SchedulerWorkersHighWaterMark = _statistics?.GetOrAddStatistic(AmbientStatisicType.Max, $"{nameof(SchedulerWorkersHighWaterMark)}:{schedulerName}", $"{nameof(SchedulerWorkersHighWaterMark)}:{schedulerName}", "The highest number of scheduler workers");
            SchedulerSlowestInvocationMilliseconds = _statistics?.GetOrAddStatistic(AmbientStatisicType.Max, $"{nameof(SchedulerSlowestInvocationMilliseconds)}:{schedulerName}", $"{nameof(SchedulerSlowestInvocationMilliseconds)}:{schedulerName}", "The highest number of milliseconds required for an invocation");

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
                FifoWorker worker = CreateWorker();
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
        ~FifoTaskScheduler()
        {
            if (_executeDisposalCheck)
            {
                Debug.Fail($"Failed to dispose/close {nameof(FifoTaskScheduler)} {_schedulerName}: Stack at construction was:\n{fStackAtConstruction}");
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

        internal FifoWorker CreateWorker()
        {
            int id = Interlocked.Increment(ref _workerId);
            // initialize the worker (starts their threads)
            FifoWorker worker = FifoWorker.Start(this, ThreadName(id), _schedulerThreadPriority);
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
                            FifoTaskScheduler.Logger.Filter("Reset", AmbientLogLevel.Debug)?.Log( new { Action = "Reset", SchedulerName = _schedulerName });
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
                                        Logger.Filter("Busy", AmbientLogLevel.Warning)?.Log(new { Action = "WorkerBusy", SchedulerName = _schedulerName, ReadyWorkers = readyWorkers, BusyWorkers = busyWorkers, MaxThreadWorkers = _maxWorkerThreads });
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
                                    FifoWorker worker = CreateWorker();
                                    Debug.Assert(!worker.IsBusy);
                                    _readyWorkerList.Push(worker);
                                }
                                FifoTaskScheduler.Logger.Filter("Scale", AmbientLogLevel.Debug)?.Log(new { Action = "ScaleUp", SchedulerName = _schedulerName, Workers = _workers });
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
                                    FifoTaskScheduler.Logger.Filter("Scale", AmbientLogLevel.Debug)?.Log(new { Action = "ScaleDown", SchedulerName = _schedulerName, Workers = _workers });
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
                if (ex is ThreadAbortException || ex is TaskCanceledException) Logger.Filter("AsyncException", AmbientLogLevel.Debug)?.Log(new { Action = "AsyncException", SchedulerName = _schedulerName }, ex);
                else Logger.Filter("AsyncException", AmbientLogLevel.Error)?.Log(new { Action = "CriticalException", SchedulerName = _schedulerName }, ex);
            }
        }

        private bool RetireOneWorker()
        {
            FifoWorker? workerToStop = _readyWorkerList.Pop();
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

        internal void OnWorkerReady(FifoWorker worker)
        {
            // add the worker back to the ready worker list
            Debug.Assert(!worker.IsBusy);
            _readyWorkerList.Push(worker);
        }
        private void ReportQueueMiss()
        {
            if (!Stopping)
            {
                // record this miss
                Interlocked.Exchange(ref _lastInlineExecutionTicks, Environment.TickCount);
                SchedulerInlineExecutions?.IncrementRaw();
                // notify any subscribers of the miss
                TaskInlined?.Invoke(this, new TaskInlinedEventArgs());
                // wake the master thread so it will add more threads ASAP
                _wakeSchedulerMasterThread.Set();
                Logger.Filter("Busy", AmbientLogLevel.Warning)?.Log(new { Action = "NoAvailableWorkers", SchedulerName = _schedulerName, Invocation = "Inline" });
            }
        }

        /// <summary>
        /// Transfers asynchronous work to this scheduler, running continuations within the scheduler so that subsequent work also runs on this scheduler.
        /// </summary>
        /// <typeparam name="T">The type returned by the function.</typeparam>
        /// <param name="func">The asynchronous function that does the work.</param>
        public Task<T> TransferWork<T>(Func<ValueTask<T>> func)
        {
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(func);
#else
            if (func == null) throw new ArgumentNullException(nameof(func));
#endif
            return Task.Factory.StartNew(() => ExecuteTask(async () =>
            {
                return await func();
            }).AsTask(), CancellationToken.None, TaskCreationOptions.None, this).Unwrap();
        }
        /// <summary>
        /// Transfers asynchronous work to this scheduler, running continuations within the scheduler so that subsequent work also runs on this scheduler.
        /// Exceptions are thrown from the function out to the caller.
        /// </summary>
        /// <param name="func">The asynchronous function that does the work.</param>
        public Task TransferWork(Func<ValueTask> func)
        {
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(func);
#else
            if (func == null) throw new ArgumentNullException(nameof(func));
#endif
            return Task.Factory.StartNew(() => ExecuteTask(async () =>
            {
                await func();
            }).AsTask(), CancellationToken.None, TaskCreationOptions.None, this).Unwrap();
        }
        /// <summary>
        /// Runs a long-running function, running it entirely on a scheduler thread.
        /// </summary>
        /// <param name="func">The func to run on a scheduler thread.  The function may be async but should not be "async void" (use <see cref="Run(Action)"/> for those).  If async, this function returns a "wrapped" task.</param>
        /// <returns>A <see cref="Task"/> which may be used to wait for the action to complete.  Presumably you want the result, or you would have used <see cref="FireAndForget(Action)"/>.</returns>
        /// <remarks>Exceptions thrown from the function will be available to be observed through the returned <see cref="Task"/>.</remarks>
        public Task<T> Run<T>(Func<T> func)
        {
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(func);
#else
            if (func == null) throw new ArgumentNullException(nameof(func));
#endif
#if NET7_0_OR_GREATER
            ObjectDisposedException.ThrowIf(Stopping, this);
#else
            if (Stopping) throw new ObjectDisposedException(nameof(FifoTaskScheduler));
#endif
            TaskCompletionSource<T> tcs = new();
            SchedulerInvocations?.IncrementRaw();
            // try to get a ready thread
            FifoWorker? worker = _readyWorkerList.Pop();
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
                _ = Task.Factory.StartNew(() => 
                {
                    try
                    {
                        // run SetResult inside ExecuteAction so that continuations also run with this task scheduler
                        tcs.SetResult(func());
                    }
                    catch (Exception e)
                    {
                        // run SetException inside ExecuteAction so that continuations also run with this task scheduler
                        tcs.SetException(e);
                    }
                }, CancellationToken.None, TaskCreationOptions.None, this);
            });
            return tcs.Task;
        }
        /// <summary>
        /// Runs a long-running action, running it entirely on a scheduler thread, but gets a <see cref="Task"/> we can use wait for completion when it does finally finish or get canceled.
        /// </summary>
        /// <param name="action">The action to run on a scheduler thread.  May be an "async void" action.</param>
        /// <returns>A <see cref="Task"/> which may be used to wait for the function to complete after it is cancelled (or exits on its own).</returns>
        /// <remarks>Exceptions thrown from the function will be available to be observed through the returned <see cref="Task"/>.</remarks>
        public Task Run(Action action)
        {
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(action);
#else
            if (action == null) throw new ArgumentNullException(nameof(action));
#endif
#if NET7_0_OR_GREATER
            ObjectDisposedException.ThrowIf(Stopping, this);
#else
            if (Stopping) throw new ObjectDisposedException(nameof(FifoTaskScheduler));
#endif
            TaskCompletionSource<bool> tcs = new();
            SchedulerInvocations?.IncrementRaw();
            // try to get a ready thread
            FifoWorker? worker = _readyWorkerList.Pop();
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
                _ = Task.Factory.StartNew(() => 
                {
                    try
                    {
                        action();
                        // run SetResult inside ExecuteAction so that any continuations also run with this task scheduler
                        tcs.SetResult(true);
                    }
                    catch (Exception e)
                    {
                        // run SetException inside ExecuteAction so that continuations also run with this task scheduler
                        tcs.SetException(e);
                    }
                }, CancellationToken.None, TaskCreationOptions.None, this);
            });
            return tcs.Task;
        }
        /// <summary>
        /// Runs a fire-and-forget action, running it entirely on a scheduler thread.
        /// </summary>
        /// <param name="action">The action to run asynchronously.  May be an "async void" action.</param>
        /// <remarks>Note that exceptions throw from <paramref name="action"/> will be unobserved.</remarks>
        [SuppressMessage("Design", "CA1030:Use events where appropriate", Justification = "This should be obvious.  The prefix 'Fire' doesn't always imply something that should be an event!")]
        public void FireAndForget(Action action)
        {
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(action);
#else
            if (action == null) throw new ArgumentNullException(nameof(action));
#endif
#if NET7_0_OR_GREATER
            ObjectDisposedException.ThrowIf(Stopping, this);
#else
            if (Stopping) throw new ObjectDisposedException(nameof(FifoTaskScheduler));
#endif
            SchedulerInvocations?.IncrementRaw();
            // try to get a ready thread
            FifoWorker? worker = _readyWorkerList.Pop();
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
                _ = Task.Factory.StartNew(action, CancellationToken.None, TaskCreationOptions.None, this);
            });
        }
        /// <summary>
        /// Runs the specified action in the context of the high performance FIFO task scheduler.
        /// Everything up to the first await will run inline, but continuations will run in the context of the high performance FIFO task scheduler.
        /// </summary>
        /// <param name="action">The <see cref="Action"/> to run.</param>
        internal void ExecuteAction(Action action)
        {
            // we now have a worker that is busy
            Interlocked.Increment(ref _busyWorkers);
            // keep track of the maximum concurrent usage
            InterlockedUtilities.TryOptomisticMax(ref _peakConcurrentUsageSinceLastRetirementCheck, _busyWorkers);
            try
            {
                action();
            }
            catch (Exception ex)
            {
                RaiseUnhandledException(ex);
            }
            finally
            {
                // the worker is no longer busy
                Interlocked.Decrement(ref _busyWorkers);
            }
        }
        /// <summary>
        /// Executes an asynchronous function with the high performance FIFO task scheduler as the default synchronization context.
        /// This function must not be awaited because awaits outside of this context may cause continuations to run outside of this task scheduler.
        /// </summary>
        /// <typeparam name="T">The type that will be returned from the async function.</typeparam>
        /// <param name="func">The async function to invoke within the context of the high performance FIFO task scheduler.</param>
        private async ValueTask<T> ExecuteTask<T>(Func<ValueTask<T>> func)
        {
            // we now have a worker that is busy
            Interlocked.Increment(ref _busyWorkers);
            // keep track of the maximum concurrent usage
            InterlockedUtilities.TryOptomisticMax(ref _peakConcurrentUsageSinceLastRetirementCheck, _busyWorkers);
            try
            {
                return await func(); // the whole point of this function is to execute the task in the high performance synchronization context
            }
            finally
            {
                // the worker is no longer busy
                Interlocked.Decrement(ref _busyWorkers);
            }
        }
        /// <summary>
        /// Executes an asynchronous function with the high performance FIFO task scheduler as the default synchronization context.
        /// This function must not be awaited because awaits outside of this context may cause continuations to run outside of this task scheduler.
        /// </summary>
        /// <param name="func">The async function to invoke within the context of the high performance FIFO task scheduler.</param>
        private async ValueTask ExecuteTask(Func<ValueTask> func)
        {
            // we now have a worker that is busy
            Interlocked.Increment(ref _busyWorkers);
            // keep track of the maximum concurrent usage
            InterlockedUtilities.TryOptomisticMax(ref _peakConcurrentUsageSinceLastRetirementCheck, _busyWorkers);
            try
            {
                await func(); // the whole point of this function is to execute the task in the high performance synchronization context
            }
            finally
            {
                // the worker is no longer busy
                Interlocked.Decrement(ref _busyWorkers);
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
        /// Any exceptions will go to the unhandled exception handler.
        /// </summary>
        /// <param name="task">The <see cref="Task"/> which is to be executed.</param>
        protected override void QueueTask(Task task)
        {
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(task);
#else
            if (task == null) throw new ArgumentNullException(nameof(task));
#endif
#if NET7_0_OR_GREATER
            ObjectDisposedException.ThrowIf(_stopMasterThread != 0, this);
#else
            if (_stopMasterThread != 0) throw new ObjectDisposedException(nameof(FifoTaskScheduler));
#endif
            SchedulerInvocations?.IncrementRaw();
            // try to get a ready thread
            FifoWorker? worker = _readyWorkerList.Pop();
            // no ready workers?
            if (worker is null || Stopping)
            {
                ReportQueueMiss();
                // if the CPU is above the max, let's slow things down here by just sleeping for a bit
                if (CpuMonitor.RecentUsage > MaxCpuUsage) Thread.Sleep(10);
                // execute the action inline
                TryExecuteTaskInline(task, false);
                return;
            }
            else
            {
                Debug.Assert(!worker.IsBusy);
            }
            worker.Invoke(() => 
            {
                TryExecuteTask(task);                  // calling this system function takes care of unobserved task exceptions through the system event handler
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
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(node);
#else
            if (node == null) throw new ArgumentNullException(nameof(node));
#endif
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
