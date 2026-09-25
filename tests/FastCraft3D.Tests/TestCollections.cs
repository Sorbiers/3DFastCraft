using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// Tests that measure time, run on their own once everything else has finished.
///
/// xUnit runs test classes side by side, and a wall clock read while other tests are busy
/// measures them as much as the code: the bore through a dense ball, 600 ms on its own, took
/// over 4 seconds beside the generator sweep and the motion runs, and failed a limit it had
/// seven times over. Run alone they measure what they were written to.
/// </summary>
[CollectionDefinition("Timing", DisableParallelization = true)]
public class TimingCollection;
