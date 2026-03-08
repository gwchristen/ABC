namespace ABC.Helpers;

public static class BarcodeParser
{
    private static int _prefixLength = 3;
    private static int _serialLength = 9;
    private static int _suffixLength = 5;

    public static int PrefixLength => _prefixLength;
    public static int SerialLength => _serialLength;
    public static int SuffixLength => _suffixLength;
    public static int ExpectedLength => _prefixLength + _serialLength + _suffixLength;

    public static void Configure(int prefixLength, int serialLength, int suffixLength)
    {
        _prefixLength = prefixLength;
        _serialLength = serialLength;
        _suffixLength = suffixLength;
    }

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
