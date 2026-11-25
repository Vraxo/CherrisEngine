using Xunit;

// Physics and the scene lifecycle both keep engine-wide state — the active world, the active scene, the clock and
// the layer collision matrix — and a test that sets one of those would leak into any test running beside it. The
// suite is small enough that running it one test at a time costs nothing worth measuring.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
