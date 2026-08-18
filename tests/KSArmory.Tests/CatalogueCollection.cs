using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// The catalogue is one static registry for the process, so anything that registers into it or
/// shuts it has to run alone. xUnit parallelises across classes by default, and a freeze taken
/// while another class is registering fails whichever of them lost the race.
/// </summary>
[CollectionDefinition("catalogue", DisableParallelization = true)]
public class CatalogueCollection;
