using System.Runtime.CompilerServices;

// The EditMode test assembly may exercise internal helpers (pure mapping functions)
// without widening the public API surface of the Core assembly.
[assembly: InternalsVisibleTo("MultiTravel.Tests.EditMode")]
