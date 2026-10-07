#:project ../src/AIUsage.Core/AIUsage.Core.csproj

// Console smoke test against the real endpoint: dotnet run tools/smoke.cs [en]
using System.Globalization;
using AIUsage.Core;
using AIUsage.Core.Claude;
using Microsoft.Extensions.Logging.Abstractions;

CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(args.FirstOrDefault() ?? "de");
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
var provider = new ClaudeCodeProvider(http, () => null, NullLogger<ClaudeCodeProvider>.Instance, TimeProvider.System);
var snapshot = await provider.FetchAsync(CancellationToken.None);

Console.WriteLine($"{snapshot.DisplayName} ({snapshot.Plan ?? "-"}): {snapshot.Status} {UsageFormat.StatusMessage(snapshot.Status)}");
foreach (var w in snapshot.Windows)
{
    Console.WriteLine($"  {w.Label,-30} {w.UsedPercent,6:0.0} %  {UsageFormat.ResetText(w.ResetsAt, DateTimeOffset.UtcNow)}");
}

return snapshot.Status == UsageStatus.Ok ? 0 : 1;
