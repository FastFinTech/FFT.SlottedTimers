// Copyright (c) True Goodwill. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace FFT.SlottedTimers
{
  using System;
  using System.Collections.Concurrent;
  using System.Diagnostics;
  using System.Linq;
  using System.Runtime.CompilerServices;
  using System.Runtime.InteropServices;
  using System.Runtime.Versioning;
  using System.Threading;
  using System.Threading.Tasks;
  using FFT.Disposables;
  using static System.Math;
  using FILETIME = System.Runtime.InteropServices.ComTypes.FILETIME;

  /// <summary>
  /// Use this class for high-performance, allocation-free, inaccurate,
  /// asynchronous waits to replace usage of the resource-intensive <see
  /// cref="Task.Delay(int)"/> method in a scalable application that has
  /// thousands of concurrent waits in progress.
  /// </summary>
  public sealed class SlottedTimer : DisposeBase
  {
    /// <summary>
    /// A slot is a list of jobs that will be completed at the same time.
    /// </summary>
    private const int NUM_SLOTS = 1024;

    /// <summary>
    /// The accuracy of the timer. Timer jobs will be marked complete at their
    /// trigger time plus or minus this value in milliseconds.
    /// </summary>
    private readonly int _resolutionMS;

    /// <summary>
    /// Each list "slot" in this array is a list of timer jobs that will be
    /// completed at the same time.
    /// </summary>
    private readonly TimerJobLinkedList[] _slots;

    /// <summary>
    /// A list of new jobs that have been newly created and need to be inserted
    /// into a slot in a thread-safe way.
    /// </summary>
    private readonly ConcurrentQueue<TimerJob> _newJobs = new();

    /// <summary>
    /// A list of TimerJob objects that have been completed or canceled, and are
    /// coming up for recycling after their task has been awaited.
    /// </summary>
    private readonly TimerJobLinkedList _recycleJobs = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="SlottedTimer"/> class.
    /// </summary>
    /// <param name="resolutionMS">The accuracy of the timer. Larger values are more efficient but less accurate.</param>
    /// <exception cref="ArgumentException">Thrown if <paramref name="resolutionMS"/> is invalid.</exception>
    public SlottedTimer(int resolutionMS = 250)
    {
      if (resolutionMS < 10)
        throw new ArgumentException("Resolution must be greater than or equal to 10ms.", nameof(resolutionMS));

      _resolutionMS = resolutionMS;

      _slots = new TimerJobLinkedList[NUM_SLOTS];
      for (var i = 0; i < NUM_SLOTS; i++)
        _slots[i] = new TimerJobLinkedList();

      Task.Run(WorkAsync).ContinueWith(
        t =>
        {
          Debug.Fail($"{nameof(SlottedTimer)}.{nameof(WorkAsync)} method failed.", t.Exception!.ToString());
        },
        TaskContinuationOptions.OnlyOnFaulted);
    }

    /// <summary>
    /// Creates a <see cref="ValueTask"/> that completes after the given <paramref name="milliseconds"/>.
    /// </summary>
    /// <param name="milliseconds">The number of milliseconds to wait.</param>
    /// <param name="cancellationToken">Cancels the pending wait.</param>
    public ValueTask WaitAsync(int milliseconds, CancellationToken cancellationToken = default)
    {
      if (cancellationToken.IsCancellationRequested)
      {
        return new ValueTask(Task.FromCanceled(cancellationToken)); // Canceled task
      }
      else if (milliseconds <= _resolutionMS)
      {
        return default; // Completed task
      }
      else
      {
        // Get an initialised timer job object from the pool.
        var job = TimerJob.Get(triggerTimeMS: NowMS() + milliseconds, cancellationToken);
        // Enqueue the job so it can be added to a slot in a threadsafe way.
        _newJobs.Enqueue(job);
        // Return the timer task to the calling code.
        return job.Task;
      }
    }

    /// <summary>
    /// Gets the current time, expressed in milliseconds.
    /// </summary>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long NowMS()
    {
      GetSystemTimeAsFileTime(out var fileTime);
      long time = 0;
      time |= (uint)fileTime.dwHighDateTime;
      time <<= sizeof(uint) * 8;
      time |= (uint)fileTime.dwLowDateTime;
      return (time + 0x701ce1722770000L) / TimeSpan.TicksPerMillisecond;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [ResourceExposure(ResourceScope.None)]
    private static extern void GetSystemTimeAsFileTime([Out] out FILETIME time);

    private async Task WorkAsync()
    {
      await RunTimersUntilDisposedAsync();
      await CleanupAsync();
    }

    private async Task RunTimersUntilDisposedAsync()
    {
      try
      {
        // Index of the timer jobs that will be triggered next.
        var slotIndex = 0;
        var startMS = NowMS();

        // Keep running this loop until an "OperationCanceledException" is
        // thrown at disposal when "DisposedToken" is canceled.
        while (true)
        {
          // Create a timer interval now, while starting work, but don't await
          // it until after we have completed the work.
          var interval = Task.Delay(_resolutionMS, DisposedToken);

          // Store the current time.
          var nowMs = NowMS();

          // First, do a little cleanup and recycle any jobs in the recycle
          // queue that are ready to be recycled.
          var job = _recycleJobs.First;
          while (job is not null)
          {
            var next = job.Next;
            if (job.CanRecycle)
            {
              _recycleJobs.Remove(job);
              job.Recycle();
            }

            job = next;
          }

          // Now get all the new jobs out of the new jobs queue and insert them
          // to their correct slot.
          while (_newJobs.TryDequeue(out job))
          {
            // If the job is already completed (probably as a result of
            // cancellation), we can skip the slots and add it to the queue for
            // recycling.
            if (job.Task.IsCompleted)
            {
              _recycleJobs.Add(job);
            }
            else
            {
              // Otherwise, we set its "rounds remaining" property and add it to a slot.
              var slotsFromNow = Max(0, (int)((job.TriggerTimeMS - nowMs) / _resolutionMS));
              job.SetRoundsRemaining(slotsFromNow / NUM_SLOTS);
              _slots[(slotIndex + slotsFromNow) % NUM_SLOTS].Add(job);
            }
          }

          // Due to Task.Delay variability, and cpu load variability, we
          // may have fallen behind, and may need to trigger the timers in
          // more than one slot.
          var expectedSlotIndex = (nowMs - startMS) / _resolutionMS;
          while (slotIndex <= expectedSlotIndex)
          {
            var slot = _slots[slotIndex++ % NUM_SLOTS];
            job = slot.First;
            while (job is not null)
            {
              var next = job.Next;
              if (job.PopRoundsRemaining() || job.Task.IsCompleted)
              {
                slot.Remove(job);
                // Jobs cannot be immediately recycled after their task is
                // completed ... we must wait for their task to be awaited by
                // calling code. So we add them to this wait queue.
                _recycleJobs.Add(job);
              }

              job = next;
            }
          }

          // Now we await the interval. The actual time waited will vary
          // depending how long it took to complete the work above.
          await interval.ConfigureAwait(false);
        }
      }

      // Happens at disposal
      catch (OperationCanceledException) { }
    }

    private async Task CleanupAsync()
    {
      // Cancel all the new timer jobs and add them to the recycle queue.
      while (_newJobs.TryDequeue(out var job))
      {
        job.Cancel();
        _recycleJobs.Add(job);
      }

      // Cancel all the other timer jobs in progress and add them to the recycle
      // queue as well.
      foreach (var slot in _slots)
      {
        var job = slot.First;
        while (job is not null)
        {
          var next = job.Next;
          job.Cancel();
          _recycleJobs.Add(job);
          job = next;
        }
      }

      // A timer job can only be recycled after its task has been awaited by
      // calling code. We'll give up to ten seconds for all the jobs to be ready
      // for recycling. Any timer job not recycled by then will be collected by
      // the garbage collector instead of recycled.
      var count = 0;
      while (_recycleJobs.First is not null && count++ < 10)
      {
        var job = _recycleJobs.First;
        while (job is not null)
        {
          var next = job.Next;
          if (job.CanRecycle)
          {
            _recycleJobs.Remove(job);
            job.Recycle();
          }

          job = next;
        }

        await Task.Delay(1000);
      }
    }
  }
}
