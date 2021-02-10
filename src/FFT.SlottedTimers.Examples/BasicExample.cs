//// Copyright (c) True Goodwill. All rights reserved.
//// Licensed under the MIT license. See LICENSE file in the project root for full license information.

//namespace FFT.SlottedTimers.Examples
//{
//  using System;
//  using System.Collections.Generic;
//  using System.Linq;
//  using System.Net.Sockets;
//  using System.Text;
//  using System.Threading.Tasks;

//  public class BasicExample : IExample
//  {
//    private static readonly SlottedTimer _timer = new SlottedTimer(250);

//    public string Name => "Example 1";

//    public async ValueTask RunAsync()
//    {
//      var connections = new ConnectionExample[100];
//      for (var i = 0; i < 100; i++)
//        connections[i] = new ConnectionExample(null!);
//      await Task.Delay(60000);
//    }

//    private class ConnectionExample
//    {
//      private readonly Socket _socket;

//      public ConnectionExample(Socket socket)
//      {
//        _socket = socket;
//        _ = Task.Run(WorkAsync);
//      }

//      private async Task WorkAsync()
//      {
//        var count = 0;
//        while (true)
//        {
//          // get a task which completes after a one=second interval.
//          var interval = _timer.WaitAsync(1000);

//          // send data via the socket (not implemented)

//          // wait until the one-second interval has completed.
//          await interval.ConfigureAwait(false);

//          if (++count % 10 == 0)
//          {
//            Console.WriteLine($"completed {count}.");
//          }

//          if (count == 100)
//            return;
//        }
//      }
//    }
//  }
//}
