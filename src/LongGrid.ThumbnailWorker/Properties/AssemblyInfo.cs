using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

[assembly: ExcludeFromCodeCoverage(
    Justification = "Covered by the mandatory restricted-worker isolation matrix.")]
[assembly: InternalsVisibleTo("LongGrid.Core.Tests")]
[assembly: InternalsVisibleTo("LongGrid.Infrastructure")]
[assembly: InternalsVisibleTo("LongGrid.Spikes.ShellItemImages")]
