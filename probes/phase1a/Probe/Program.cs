using System.Globalization;
using Phase1A;

if (args.Length != 1 || !ulong.TryParse(args[0], NumberStyles.None, CultureInfo.InvariantCulture, out var seed))
{
    Console.Error.WriteLine("Usage: Phase1A.Probe <unsigned-seed>");
    return 2;
}
Console.OutputEncoding = new System.Text.UTF8Encoding(false);
Console.Write(CanonicalLog.Format(Scenario.Run(seed)));
return 0;
