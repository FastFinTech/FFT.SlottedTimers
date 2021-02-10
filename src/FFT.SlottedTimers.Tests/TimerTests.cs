// Copyright (c) True Goodwill. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace FFT.SlottedTimers.Tests
{
  using System;
  using System.Diagnostics;
  using System.Threading;
  using System.Threading.Tasks;
  using Microsoft.VisualStudio.TestTools.UnitTesting;

  [TestClass]
  public class TimerTests
  {
    [TestMethod]
    public async Task TimerWorks()
    {
      using var timer = new SlottedTimer(50);
      using var cts = new CancellationTokenSource(2000);

      var sw1 = Stopwatch.StartNew();
      var sw2 = Stopwatch.StartNew();
      var t1 = Task.Run(async () =>
      {
        await timer.WaitAsync(100, cts.Token);
        sw1.Stop();
      });
      var t2 = Task.Run(async () =>
      {
        await timer.WaitAsync(1000, cts.Token);
        sw2.Stop();
      });
      await Task.WhenAll(t1, t2);
      Assert.IsTrue(sw1.ElapsedMilliseconds > 50 && sw1.ElapsedMilliseconds < 150);
      Assert.IsTrue(sw2.ElapsedMilliseconds > 900 && sw1.ElapsedMilliseconds < 1100);
    }

    [TestMethod]
    public async Task CancellationWorks()
    {
      using var timer = new SlottedTimer(50);
      using var cts = new CancellationTokenSource(900);
      await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => Task.Run(async () => await timer.WaitAsync(1000, cts.Token)));
    }
  }
}
