// Copyright (c) True Goodwill. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace FFT.SlottedTimers
{
  using System;
  using System.Collections.Concurrent;
  using System.Runtime.CompilerServices;
  using System.Threading;
  using System.Threading.Tasks;
  using System.Threading.Tasks.Sources;

  internal class TimerJob : IValueTaskSource
  {
    // Storage of recycled instances.
    private static readonly ConcurrentStack<TimerJob> _pool = new();

    private int _roundsRemaining;
    private CancellationTokenRegistration? _cancelTokenRegistration;
    private ManualResetValueTaskSourceCore<object?> _taskSource;

    private TimerJob()
    {
      _taskSource.RunContinuationsAsynchronously = true;
      Task = new ValueTask(this, _taskSource.Version);
    }

    /// <summary>
    /// These properties allow this class to be used with an allocation-free
    /// linked list.
    /// </summary>
    public TimerJob? Next { get; set; }

    /// <summary>
    /// These properties allow this class to be used with an allocation-free
    /// linked list.
    /// </summary>
    public TimerJob? Previous { get; set; }

    /// <summary>
    /// The time the that the timer job task should be completed. Used by the
    /// <see cref="SlottedTimer"/> to determine which slot to place this timer
    /// job instance in.
    /// </summary>
    public long TriggerTimeMS { get; private set; }

    /// <summary>
    /// The task that will be completed when the timer period expires, or
    /// canceled when the relevant cancellation token is canceled. To prevent
    /// invalid task source state, this task must be awaited by using code
    /// before this instance can be recycled.
    /// </summary>
    public ValueTask Task { get; private set; }

    /// <summary>
    /// Indicates whether this instance can be recycled. This value is set to
    /// true after the current task has been awaited by the using code.
    /// Recycling before then would result in invalid task source state.
    /// </summary>
    public bool CanRecycle { get; private set; }

    /// <summary>
    /// Gets a <see cref="TimerJob"/> instance from a pool, if one is available,
    /// or creates a new one.
    /// </summary>
    /// <param name="triggerTimeMS">The time that the timer task should be
    /// completed.</param>
    /// <param name="cancellationToken">Cancels the timer task.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TimerJob Get(long triggerTimeMS, CancellationToken cancellationToken = default)
    {
      if (!_pool.TryPop(out var job))
      {
        job = new TimerJob();
      }

      job.TriggerTimeMS = triggerTimeMS;

      if (cancellationToken.CanBeCanceled)
      {
        job._cancelTokenRegistration = cancellationToken.Register(Cancel, (job, cancellationToken));

        static void Cancel(object? state)
        {
          var (job, cancellationToken) = ((TimerJob, CancellationToken))state!;
          // Eat exceptions that may occur due to race condition with task completion.
          try
          {
            job._taskSource.SetException(new OperationCanceledException(cancellationToken));
          }
          catch (InvalidOperationException) { }
        }
      }

      return job;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetRoundsRemaining(int roundsRemaining) => _roundsRemaining = roundsRemaining;

    /// <summary>
    /// Decrements the "rounds remaining" counter held inside this job. If the
    /// "rounds remaining" counter is zero, the job's task will be completed.
    /// Returns true if the task completion was triggered, false otherwise.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool PopRoundsRemaining()
    {
      if (_roundsRemaining-- == 0)
      {
        _cancelTokenRegistration?.Dispose();
        _cancelTokenRegistration = null;
        // Eat exceptions that may occur due to race condition with cancellation
        // token.
        try
        {
          _taskSource.SetResult(null);
        }
        catch (InvalidOperationException) { }
        return true;
      }

      return false;
    }

    /// <summary>
    /// This method is called by the slotted timer. It immediately cancels the
    /// timer job's task when the timer itself is disposed.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Cancel()
    {
      if (_taskSource.GetStatus(_taskSource.Version) == ValueTaskSourceStatus.Pending)
      {
        _cancelTokenRegistration?.Dispose();
        _cancelTokenRegistration = null;
        // Eat exceptions that may occur due to race condition with task completion.
        try
        {
          _taskSource.SetException(new OperationCanceledException());
        }
        catch (InvalidOperationException) { }
      }
    }

    /// <summary>
    /// Resets and returns this instance to a pool, allowing it to be re-used to reduce allocations.
    /// </summary>
    public void Recycle()
    {
      Next = null;
      Previous = null;
      CanRecycle = false;
      _cancelTokenRegistration?.Dispose();
      _cancelTokenRegistration = null;
      _taskSource.Reset();
      Task = new ValueTask(this, _taskSource.Version);
      _pool.Push(this);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void IValueTaskSource.GetResult(short token)
    {
      // May throw an OperationCanceledException if the cancellation token was
      // canceled. I did consider putting "CanRecycle = true" above here to make
      // sure it executes regardless of exceptions whilst keeping code simple,
      // but realised that could lead to race conditions if this object is then
      // recycled before the line below executes. So I heaved a sigh and added
      // the try/finally block to set CanRecycle true only AFTER
      // _taskSource.GetResult has executed.
      try
      {
        _taskSource.GetResult(token);
      }
      finally
      {
        CanRecycle = true;
      }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTaskSourceStatus IValueTaskSource.GetStatus(short token) => _taskSource.GetStatus(token);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void IValueTaskSource.OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags) => _taskSource.OnCompleted(continuation, state, token, flags);
  }
}
