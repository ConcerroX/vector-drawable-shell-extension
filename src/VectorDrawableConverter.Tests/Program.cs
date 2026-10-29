using VectorDrawableConverter;

if (args.Length < 1)
{
    Console.Error.WriteLine("Usage: VectorDrawableConverter.Tests <input.xml> [output.svg]");
    return 1;
}

string input = args[0];
string xml = File.ReadAllText(input);

var converter = new VectorDrawableToSvgConverter();

try
{
    string svg = converter.Convert(xml);

    if (args.Length >= 2)
    {
        File.WriteAllText(args[1], svg);
        Console.WriteLine($"OK -> {args[1]} ({svg.Length} bytes)");
    }
    else
    {
        Console.WriteLine(svg);
    }

    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"FAIL: {ex.Message}");
    return 2;
}
