// Copyright (c) True Goodwill. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace FFT.SlottedTimers
{
  /// <summary>
  /// This is an allocation-free linked list of timer jobs. A standard linked
  /// list causes allocations because it creates "node" objects that have
  /// "value", "next", and "previous" properties. We avoid this by adding "Next"
  /// and "Previous" properties directly to the TimerJob object and utilising
  /// them for the operation of the linked list. This comes with the following
  /// caveats:
  /// 1. A TimerJob can only belong to one TimerJobList at at time.
  /// 2. Since Add/Remove operations mutate the TimerJob, it must be removed from
  ///    one list BEFORE it is added to another. Getting these operations in the
  ///    wrong order will result in bugs. This class is NOT thread-safe.
  /// </summary>
  internal sealed class TimerJobLinkedList
  {
    public TimerJob? First { get; private set; }

    public TimerJob? Last { get; private set; }

    public void Add(TimerJob job)
    {
      if (Last is null)
      {
        First = Last = job;
      }
      else
      {
        Last.Next = job;
        job.Previous = Last;
        job.Next = null;
        Last = job;
      }
    }

    public void Remove(TimerJob job)
    {
      if (job.Previous is not null)
        job.Previous.Next = job.Next;
      else
        First = job.Next;

      if (job.Next is not null)
        job.Next.Previous = job.Previous;
      else
        Last = job.Previous;

      job.Previous = null;
      job.Next = null;
    }
  }
}
