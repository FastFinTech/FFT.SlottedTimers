// Copyright (c) True Goodwill. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace FFT.SlottedTimers.Tests
{
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using System.Text;
  using System.Threading;
  using System.Threading.Tasks;
  using System.Threading.Tasks.Sources;
  using Microsoft.VisualStudio.TestTools.UnitTesting;

  [TestClass]
  public class TimerJobTests
  {
    [TestMethod]
    public async Task TasksWork()
    {
      using var cts = new CancellationTokenSource();
      cts.Cancel();
      var someClass = new SomeClass(cts.Token);
      someClass.Trigger();
      await someClass.Task;
    }


    private class SomeClass : IValueTaskSource
    {
      private CancellationTokenRegistration? _cancelRegistration;
      private ManualResetValueTaskSourceCore<object?> _taskSource;

      public SomeClass(CancellationToken cancellationToken)
      {
        _taskSource.RunContinuationsAsynchronously = true;
      }

      public ValueTask Task { get; private set; }

      public bool CanRecycle { get; private set; }

      public void Init(CancellationToken cancellationToken)
      {
        CanRecycle = false;
        _taskSource.Reset();

        if (cancellationToken.CanBeCanceled)
        {
          _cancelRegistration = cancellationToken.Register(Cancel, (this, cancellationToken));

          static void Cancel(object? state)
          {
            var (someClass, cancellationToken) = ((SomeClass, CancellationToken))state!;
            try
            {
              someClass._taskSource.SetException(new OperationCanceledException(cancellationToken));
            }
            catch (InvalidOperationException) { }
          }
        }

        Task = new ValueTask(this, _taskSource.Version);
      }

      public void Trigger()
      {
        if (_cancelRegistration is not null)
        {
          _cancelRegistration.Value.Dispose();
          _cancelRegistration = null;
        }

        if (_taskSource.GetStatus(_taskSource.Version) == ValueTaskSourceStatus.Pending)
          _taskSource.SetResult(null);
      }

      public void GetResult(short token)
      {
        try
        {
          _taskSource.GetResult(token);
        }
        finally
        {
          CanRecycle = true;
        }
      }

      public ValueTaskSourceStatus GetStatus(short token) => _taskSource.GetStatus(token);

      public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags) => _taskSource.OnCompleted(continuation, state, token, flags);
    }
  }
}
