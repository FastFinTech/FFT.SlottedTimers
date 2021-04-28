// Copyright (c) True Goodwill. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace FFT.SlottedTimers.Examples
{
  using System;
  using System.Collections.Generic;
  using System.Text;
  using System.Threading;
  using System.Threading.Tasks;

  public class ScalableServerApp : IExample
  {
    private const int ClientConnectionIntervalMS = 250; // 250 millisecond interval between each client connection update
    private const int TotalWorkTimeMS = 10000; // ten seconds of total work time

    private readonly SlottedTimer _timer;

    public ScalableServerApp()
    {
      _timer = new SlottedTimer(50);
    }

    public string Name => "Scalable Server App";

    public async ValueTask RunAsync()
    {
      Console.WriteLine("Creating 1000 worker tasks (pretending 1000 clients are connected).");
      var connections = new ClientConnection[1000];
      for (var i = 0; i < connections.Length; i++)
      {
        connections[i] = new ClientConnection(this);
      }

      Console.WriteLine($"Waiting for ten seconds ... Expecting about {(connections.Length * TotalWorkTimeMS) / ClientConnectionIntervalMS} operations to be completed.");
      await _timer.WaitAsync(TotalWorkTimeMS).ConfigureAwait(false);

      // Count the total number of updates completed.
      var totalCount = 0;
      for (var i = 0; i < connections.Length; i++)
      {
        connections[i].Dispose();
        totalCount += connections[i].UpdatesCompleted;
      }

      Console.WriteLine($"There were {totalCount} updates completed at {ClientConnectionIntervalMS}ms intervals. The extra updates are because the timer tends to trigger a little early.");
      _timer.Dispose();
    }

    private class ClientConnection : IDisposable
    {
      private readonly ScalableServerApp _app;
      private readonly CancellationTokenSource _cts;

      public ClientConnection(ScalableServerApp app)
      {
        _app = app;
        _cts = new();
        _ = Task.Run(Work);
      }

      public int UpdatesCompleted { get; private set; }

      public async Task Work()
      {
        try
        {
          while (true)
          {
            // Create a 250ms interval from the time we started doing work.
            var interval = _app._timer.WaitAsync(ClientConnectionIntervalMS, _cts.Token);
            try
            {
              // Do some pretend work
            }
            finally
            {
              // Wait until the end of the interval - the actual wait varies
              // depending on how long it took to do the work above. Also note
              // that we have put the await inside the finally block in order to
              // make sure the value task is awaited even if an exception
              // happened in the work block, so that internal resources are
              // properly recycled.
              await interval.ConfigureAwait(false);
            }

            UpdatesCompleted++;
          }
        }
        catch (OperationCanceledException) { }
      }

      public void Dispose()
      {
        _cts.Cancel();
        _cts.Dispose();
      }
    }
  }
}
