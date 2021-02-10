// Copyright (c) True Goodwill. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace FFT.SlottedTimers.Examples
{
  using System.Threading.Tasks;

  internal interface IExample
  {
    string Name { get; }

    ValueTask RunAsync();
  }
}
