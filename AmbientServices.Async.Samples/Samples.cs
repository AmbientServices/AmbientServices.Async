using AmbientServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data.SqlClient;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Text;
using System.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting.Logging;
using System.Threading.Channels;
#if NET5_0_OR_GREATER
using System.Net.Http;
#else
using System.Net;
#endif

[assembly: System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]

/*
    * 
    * 
    * 
    * Samples included in README.md begin here
    * 
    * 
    * 
    * */



#region AsyncSample1
sealed class MySoonToBeAsyncClass : IDisposable
{
    private readonly Stream _file;
    /// <summary>
    /// Opens an output file.
    /// </summary>
    /// <param name="filename">The filename of the file to append to.</param>
    public MySoonToBeAsyncClass(string filename)
    {
        _file = new FileStream(filename, FileMode.Append, FileAccess.Write);
    }
    /// <summary>
    /// Writes the data into the file in UTF8 format.
    /// </summary>
    /// <param name="s"></param>
    public void WriteData(string s)
    {
        byte[] buffer = Encoding.UTF8.GetBytes(s);
        _file.Write(buffer, 0, buffer.Length);
    }
    /// <summary>
    /// Flushes data to the file.
    /// </summary>
    public void Flush()
    {
        _file.Flush();
    }
    /// <summary>
    /// Disposes of the instance.
    /// </summary>
    public void Dispose()
    {
        _file.Dispose();
    }
}
#endregion



#region AsyncSample2
sealed class MyAlmostAsyncClass : IDisposable
{
    private readonly Stream _file;
    /// <summary>
    /// Opens an output file.
    /// </summary>
    /// <param name="filename">The filename of the file to append to.</param>
    public MyAlmostAsyncClass(string filename)
    {
        _file = new FileStream(filename, FileMode.Append, FileAccess.Write);
    }
    /// <summary>
    /// Writes the data into the file in UTF8 format.
    /// </summary>
    /// <param name="s"></param>
    public void WriteData(string s)
    {
        byte[] buffer = Encoding.UTF8.GetBytes(s);
        Async.RunTaskSync(() => _file.WriteAsync(buffer, 0, buffer.Length));
    }
    /// <summary>
    /// Flushes data to the file.
    /// </summary>
    public void Flush()
    {
        Async.RunTaskSync(() => _file.FlushAsync());
    }
    /// <summary>
    /// Disposes of the instance.
    /// </summary>
    public void Dispose()
    {
        _file.Dispose();
    }
}
#endregion



#region AsyncSample3
sealed class MyAsyncReadyClass : IDisposable
{
    private readonly Stream _file;
    /// <summary>
    /// Opens an output file.
    /// </summary>
    /// <param name="filename">The filename of the file to append to.</param>
    public MyAsyncReadyClass(string filename)
    {
        _file = new FileStream(filename, FileMode.Append, FileAccess.Write);
    }
    /// <summary>
    /// Writes the data into the file in UTF8 format.
    /// </summary>
    /// <param name="s"></param>
    public async ValueTask WriteData(string s, CancellationToken cancel = default)
    {
        byte[] buffer = Encoding.UTF8.GetBytes(s);
        await Async.RunTask(() => _file.WriteAsync(buffer, 0, buffer.Length, cancel));
    }
    /// <summary>
    /// Flushes data to the file.
    /// </summary>
    public async ValueTask Flush()
    {
        await Async.RunTask(() => _file.FlushAsync());
    }
    /// <summary>
    /// Disposes of the instance.
    /// </summary>
    public void Dispose()
    {
        _file.Dispose();
    }
}
#endregion




#region AsyncSample4
sealed class MyFullyAsyncClass : IDisposable
{
    private readonly Stream _file;
    /// <summary>
    /// Opens an output file.
    /// </summary>
    /// <param name="filename">The filename of the file to append to.</param>
    public MyFullyAsyncClass(string filename)
    {
        _file = new FileStream(filename, FileMode.Append, FileAccess.Write);
    }
    /// <summary>
    /// Writes the data into the file in UTF8 format.
    /// </summary>
    /// <param name="s"></param>
    public async ValueTask WriteData(string s, CancellationToken cancel = default)
    {
        byte[] buffer = Encoding.UTF8.GetBytes(s);
        await _file.WriteAsync(buffer, 0, buffer.Length, cancel);
    }
    /// <summary>
    /// Flushes data to the file.
    /// </summary>
    public async ValueTask Flush()
    {
        await _file.FlushAsync();
    }
    /// <summary>
    /// Disposes of the instance.
    /// </summary>
    public void Dispose()
    {
        _file.Dispose();
    }
}
#endregion


#region LongRunningTask
/// <summary>
/// A class that handles a long-running task synchronously.
/// </summary>
public abstract class SynchronousLongRunningTask
{
    private int _stop;
    private readonly Thread _loopThread;             // note that this could also have used ThreadPool.UnsafeQueueUserWorkItem or another similar ThreadPool invoker

    public SynchronousLongRunningTask()
    {
        _loopThread = new Thread(Loop);
    }

    public void Start()
    {
        _loopThread.Start();
    }
    public void Stop()
    {
        Interlocked.Exchange(ref _stop, 1);
        _loopThread.Join();
    }
    public void Loop(object? state)
    {
        // loop until we're told to stop
        while (_stop == 0)
        {
            try
            {
                LoopProcess();
            }
            catch (Exception)
            {
                // do something here to log the exception because this loop is important and we can't stop looping
            }
        }
    }
    protected abstract void LoopProcess();
}
/// <summary>
/// A class that handles a long-running task asynchronously.
/// </summary>
public abstract class AsynchronousLongRunningTask
{
    private readonly Task _longRunningTask;
    private readonly CancellationTokenSource _stop = new();

    public AsynchronousLongRunningTask()
    {
        _longRunningTask = FifoTaskScheduler.Default.Run(() => Loop(_stop.Token));
    }

    public ValueTask Start()
    {
        return new ValueTask();     // Note that in .NET Core, this is more elegantly expressed as ValueTask.CompletedTask
    }
    public async ValueTask Stop()
    {
        _stop.Cancel();
        await _longRunningTask;
    }
    public int Loop(CancellationToken cancel)       // Note that we return an int here because we want to use the version of Run that returns a task, and there isn't one that returns a bare task
    {
        while (!cancel.IsCancellationRequested)
        {
            try
            {
                LoopProcess(cancel);
            }
            catch (Exception)
            {
                // do something here to log the exception because this loop is important and we can't stop looping
            }
        }
        return 0;
    }
    protected abstract void LoopProcess(CancellationToken cancel = default);
}
#endregion




namespace Tests // 2021-12-29: under net6.0 currently, tests cannot be discovered if they're not in a namespace
{
#region HPFTS
/// <summary>
/// Unit tests for <see cref="FifoTaskScheduler"/>.
/// </summary>
[TestClass]
public class TestFifoTaskScheduler
{
    [TestMethod]
    public void StartFireAndForgetWork()
    {
        // fire and forget the work, discarding the returned task (it may not finish running until after the test is marked as successful--sometimes this is what you want, but usually not--we're just testing it here)
        FifoTaskScheduler.Default.FireAndForget(() =>
        {
            while (true)
            {
                try
                {
                    // do some periodic work here!
                }
                catch (Exception)
                {
                    // log exceptions here!
                }
                // sleep in case there was no IO above to make sure we con't consume all the CPU just spinning
                Thread.Sleep(100);
            }
        });
    }
    [TestMethod]
    public async Task StartLongRunningAsyncWorkAsync()
    {
        FakeWork w = new(-2, false);
        // push the work over to the high performance scheduler, leaving this thread to do other async work in the mean time
        await FifoTaskScheduler.Default.Run(() => w.DoMixedWorkAsync());
    }
    [TestMethod]
    public async Task StartNew()
    {
        List<Task> tasks = new();
        for (int i = 0; i < 100; ++i)
        {
            FakeWork w = new(i, true);
            // note the use of AsTask here because Task.WaitAll might await the resulting Task more than once (it probably doesn't, but just to be safe...)
            tasks.Add(FifoTaskFactory.Default.StartNew(() => w.DoMixedSyncWork()));
        }
        await Task.WhenAll(tasks.ToArray());
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
    public void DoMixedSyncWork()
    {
        ulong hash = GetHash(_id);

        Assert.AreEqual(FifoTaskScheduler.Default, TaskScheduler.Current);
        for (int outer = 0; outer < (int)(hash % 256); ++outer)
        {
            Stopwatch cpu = Stopwatch.StartNew();
            CpuWork(hash);
            cpu.Stop();
            Assert.AreEqual(FifoTaskScheduler.Default, TaskScheduler.Current);
            Stopwatch mem = Stopwatch.StartNew();
            MemoryWork(hash);
            mem.Stop();
            Assert.AreEqual(FifoTaskScheduler.Default, TaskScheduler.Current);
            Stopwatch io = Stopwatch.StartNew();
            // simulate I/O by sleeping
            Thread.Sleep((int)((hash >> 32) % (_fast ? 5UL : 500UL)));
            io.Stop();
        }
        Assert.AreEqual(FifoTaskScheduler.Default, TaskScheduler.Current);
    }
    public async ValueTask DoMixedWorkAsync(CancellationToken cancel = default)
    {
        ulong hash = GetHash(_id);
        await Task.Yield();
        //string? threadName = Thread.CurrentThread.Name;

        Assert.AreEqual(FifoTaskScheduler.Default, TaskScheduler.Current);
        for (int outer = 0; outer < (int)(hash % 256) && !cancel.IsCancellationRequested; ++outer)
        {
            Stopwatch cpu = Stopwatch.StartNew();
            CpuWork(hash);
            cpu.Stop();
            Assert.AreEqual(FifoTaskScheduler.Default, TaskScheduler.Current);
            Stopwatch mem = Stopwatch.StartNew();
            MemoryWork(hash);
            mem.Stop();
            Assert.AreEqual(FifoTaskScheduler.Default, TaskScheduler.Current);
            Stopwatch io = Stopwatch.StartNew();
            // simulate I/O by blocking
            await Task.Delay((int)((hash >> 32) % (_fast ? 5UL : 500UL)), cancel);
            io.Stop();
            Assert.AreEqual(FifoTaskScheduler.Default, TaskScheduler.Current);
        }
        Assert.AreEqual(FifoTaskScheduler.Default, TaskScheduler.Current);
        //Debug.WriteLine($"Ran work {_id} on {threadName}!", "Work");
    }
    private void CpuWork(ulong hash)
    {
        // use some CPU
        for (int spin = 0; spin < (int)((hash >> 6) % (_fast ? 16UL : 256UL)); ++spin)
        {
            double d1 = 0.0000000000000001;
            double d2 = 0.0000000000000001;
            for (int inner = 0; inner < (_fast ? 100 : 1000000); ++inner) { d2 = d1 * d2; }
        }
    }
    private void MemoryWork(ulong hash)
    {
        // use some memory
        int bytesPerLoop = (int)((hash >> 12) % (_fast ? 10UL : 1024UL));
        int loops = (int)((hash >> 22) % 1024);
        for (int memory = 0; memory < loops; ++memory)
        {
            byte[] bytes = new byte[bytesPerLoop];
        }
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
#endregion



}

