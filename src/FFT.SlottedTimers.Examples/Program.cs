// Copyright (c) True Goodwill. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace FFT.SlottedTimers.Examples
{
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using System.Reflection;
  using System.Threading.Tasks;

  internal class Program
  {
    private static async Task Main(string[] args)
    {
      var error = false;
      try
      {
        await RunExamplesAsync();
      }
      catch (Exception x)
      {
        error = true;
        Console.WriteLine(x.ToString());
      }

      Console.WriteLine();
      if (error)
      {
        Console.WriteLine("Errored. Press a key to exit.");
      }
      else
      {
        Console.WriteLine("Finished. Press a key to exit.");
      }

      Console.ReadKey();
    }

    private static async Task RunExamplesAsync()
    {
      var examples = Assembly.GetExecutingAssembly().GetTypes()
        .Where(t => !t.IsInterface && !t.IsAbstract && typeof(IExample).IsAssignableFrom(t))
        .Select(t => (IExample)(Activator.CreateInstance(t)!))
        .OrderBy(t => t!.Name)
        .ToArray();

      var names = new HashSet<string>();
      foreach(var example in examples)
      {
        if (string.IsNullOrWhiteSpace(example.Name))
        {
          throw new Exception($"Example type '{example.GetType()}' has an empty name.");
        }

        if (!names.Add(example.Name))
        {
          throw new Exception($"More than one example has the name '{example.Name}'.");
        }
      }

      foreach (var example in examples)
      {
        Console.WriteLine("===========================================");
        Console.WriteLine("= " + example.Name);
        Console.WriteLine("===========================================");
        await example.RunAsync();
        Console.WriteLine();
        Console.WriteLine();
      }
    }
  }
}
