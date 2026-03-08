namespace ABC.Helpers;

public static class BarcodeParser
{
    public const int ExpectedLength = 17;
    public const int PrefixLength = 3;
    public const int SerialLength = 9;
    public const int SuffixLength = 5;

    public static bool TryParse(string barcode, out string prefix, out long serial, out string suffix)
    {
        prefix = string.Empty;
        serial = 0;
        suffix = string.Empty;

        if (string.IsNullOrEmpty(barcode) || barcode.Length != ExpectedLength)
            return false;

        prefix = barcode[..PrefixLength];
        string serialStr = barcode[PrefixLength..(PrefixLength + SerialLength)];
        suffix = barcode[(PrefixLength + SerialLength)..];

        return long.TryParse(serialStr, out serial);
    }

    public static string Build(string prefix, long serial, string suffix)
        => prefix + serial.ToString("D" + SerialLength) + suffix;
}
