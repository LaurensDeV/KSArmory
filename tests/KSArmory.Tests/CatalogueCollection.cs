using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// The catalogue is one static registry for the process, so anything that registers into it, shuts
/// it or walks its lists has to run alone. xUnit parallelises across classes by default: a freeze
/// taken while another class is registering fails whichever lost the race, and a list walked while
/// another class adds to it throws.
/// </summary>
[CollectionDefinition("catalogue", DisableParallelization = true)]
public class CatalogueCollection;
